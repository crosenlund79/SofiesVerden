// IndexedDB access for Sofies Univers.
// Records are stored as plain objects (JSON from C#) in one object store per record type.
// Every write runs in a single IndexedDB transaction, so it either succeeds completely or not at all.

const DB_NAME = 'sofies-univers';
const DB_VERSION = 1;
const RECORD_STORES = ['sessions', 'payments', 'elementTypes', 'expenseCategories', 'paymentMethods', 'expenses', 'trips', 'settings'];
const ATTACHMENTS = 'attachments';
const ALL_STORES = [...RECORD_STORES, ATTACHMENTS];

let dbPromise = null;

function openDb() {
    if (dbPromise) return dbPromise;
    dbPromise = new Promise((resolve, reject) => {
        if (!('indexedDB' in globalThis) || !globalThis.indexedDB) {
            reject(new Error('IndexedDB er ikke tilgængelig i denne browser.'));
            return;
        }
        let request;
        try {
            request = indexedDB.open(DB_NAME, DB_VERSION);
        } catch (e) {
            reject(e);
            return;
        }
        request.onupgradeneeded = () => {
            const db = request.result;
            // Version 1: one store per record type, keyed by "id".
            for (const name of ALL_STORES) {
                if (!db.objectStoreNames.contains(name)) db.createObjectStore(name, { keyPath: 'id' });
            }
        };
        request.onsuccess = () => {
            const db = request.result;
            db.onversionchange = () => { db.close(); dbPromise = null; };
            db.onclose = () => { dbPromise = null; };
            resolve(db);
        };
        request.onerror = () => { dbPromise = null; reject(request.error || new Error('Databasen kunne ikke åbnes.')); };
        request.onblocked = () => { dbPromise = null; reject(new Error('Databasen er blokeret af et andet vindue. Luk andre faner med appen.')); };
    });
    return dbPromise;
}

function completion(tx) {
    return new Promise((resolve, reject) => {
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error || new Error('Transaktionen fejlede.'));
        tx.onabort = () => reject(tx.error || new Error('Transaktionen blev afbrudt.'));
    });
}

function req(r) {
    return new Promise((resolve, reject) => {
        r.onsuccess = () => resolve(r.result);
        r.onerror = () => reject(r.error);
    });
}

function describe(e) {
    if (!e) return 'Ukendt fejl';
    if (e.name === 'QuotaExceededError') return 'Der er ikke mere lagerplads på enheden.';
    return `${e.name || 'Error'}: ${e.message || e}`;
}

async function run(fn) {
    try {
        return await fn();
    } catch (e) {
        throw new Error(describe(e));
    }
}

export function isAvailable() {
    return 'indexedDB' in globalThis && !!globalThis.indexedDB;
}

export function loadAll() {
    return run(async () => {
        const db = await openDb();
        const tx = db.transaction(RECORD_STORES, 'readonly');
        const done = completion(tx);
        const result = {};
        await Promise.all(RECORD_STORES.map(async name => { result[name] = await req(tx.objectStore(name).getAll()); }));
        await done;
        return JSON.stringify(result);
    });
}

function toBytes(data) {
    // Copy so the stored value never references memory owned by the .NET runtime.
    return data instanceof Uint8Array ? data.slice() : new Uint8Array(data);
}

export function commit(operationsJson, attachmentMetaJson, attachmentBytes) {
    return run(async () => {
        const ops = JSON.parse(operationsJson);
        for (const op of ops) {
            if (!ALL_STORES.includes(op.store)) throw new Error(`Ukendt lager: ${op.store}`);
        }
        const stores = new Set(ops.map(o => o.store));
        if (attachmentMetaJson) stores.add(ATTACHMENTS);
        if (stores.size === 0) return;
        const db = await openDb();
        const tx = db.transaction([...stores], 'readwrite');
        const done = completion(tx);
        try {
            for (const op of ops) {
                const store = tx.objectStore(op.store);
                if (op.op === 'put') store.put(op.value);
                else if (op.op === 'delete') store.delete(op.key);
                else throw new Error(`Ukendt operation: ${op.op}`);
            }
            if (attachmentMetaJson) {
                const meta = JSON.parse(attachmentMetaJson);
                tx.objectStore(ATTACHMENTS).put({ ...meta, data: toBytes(attachmentBytes) });
            }
        } catch (e) {
            try { tx.abort(); } catch { /* already finished */ }
            await done.catch(() => {});
            throw e;
        }
        await done;
    });
}

export function getAttachment(id) {
    return run(async () => {
        const db = await openDb();
        const tx = db.transaction([ATTACHMENTS], 'readonly');
        const record = await req(tx.objectStore(ATTACHMENTS).get(id));
        if (!record) return null;
        return JSON.stringify({ id: record.id, fileName: record.fileName, contentType: record.contentType });
    });
}

export function getAttachmentBytes(id) {
    return run(async () => {
        const db = await openDb();
        const tx = db.transaction([ATTACHMENTS], 'readonly');
        const record = await req(tx.objectStore(ATTACHMENTS).get(id));
        return record ? new Uint8Array(record.data) : null;
    });
}

export function putAttachment(metaJson, bytes) {
    return run(async () => {
        const meta = JSON.parse(metaJson);
        const db = await openDb();
        const tx = db.transaction([ATTACHMENTS], 'readwrite');
        const done = completion(tx);
        tx.objectStore(ATTACHMENTS).put({ ...meta, data: toBytes(bytes) });
        await done;
    });
}

export function listAttachmentIds() {
    return run(async () => {
        const db = await openDb();
        const tx = db.transaction([ATTACHMENTS], 'readonly');
        const keys = await req(tx.objectStore(ATTACHMENTS).getAllKeys());
        return keys.map(String);
    });
}

export function deleteAttachments(ids) {
    return run(async () => {
        const db = await openDb();
        const tx = db.transaction([ATTACHMENTS], 'readwrite');
        const done = completion(tx);
        const store = tx.objectStore(ATTACHMENTS);
        for (const id of ids) store.delete(id);
        await done;
    });
}

// Replaces all records atomically: one readwrite transaction over every store.
// If anything fails, IndexedDB rolls the whole transaction back and the old data stays.
export function replaceAll(allStoresJson, keepAttachmentIds) {
    return run(async () => {
        const all = JSON.parse(allStoresJson);
        for (const name of RECORD_STORES) {
            if (!Array.isArray(all[name])) throw new Error(`Gendannelsesdata mangler "${name}".`);
        }
        const keep = new Set(keepAttachmentIds);
        const db = await openDb();
        const tx = db.transaction(ALL_STORES, 'readwrite');
        const done = completion(tx);
        try {
            for (const name of RECORD_STORES) {
                const store = tx.objectStore(name);
                store.clear();
                for (const record of all[name]) store.put(record);
            }
            const attachments = tx.objectStore(ATTACHMENTS);
            const keyRequest = attachments.getAllKeys();
            keyRequest.onsuccess = () => {
                for (const key of keyRequest.result) {
                    if (!keep.has(String(key))) attachments.delete(key);
                }
            };
        } catch (e) {
            try { tx.abort(); } catch { /* already finished */ }
            await done.catch(() => {});
            throw e;
        }
        await done;
    });
}

export async function storageInfo() {
    const info = { persisted: null, usage: null, quota: null, persistSupported: false };
    try {
        if (navigator.storage) {
            info.persistSupported = typeof navigator.storage.persist === 'function';
            if (navigator.storage.persisted) info.persisted = await navigator.storage.persisted();
            if (navigator.storage.estimate) {
                const e = await navigator.storage.estimate();
                info.usage = e.usage ?? null;
                info.quota = e.quota ?? null;
            }
        }
    } catch { /* not supported */ }
    return JSON.stringify(info);
}

export async function requestPersistence() {
    try {
        if (navigator.storage && navigator.storage.persist) return await navigator.storage.persist();
    } catch { /* not supported */ }
    return false;
}

// Self test on a separate, temporary database. Never touches Sofie's data.
export async function selfTest() {
    const name = 'sofies-univers-systemtjek';
    const open = () => new Promise((resolve, reject) => {
        const r = indexedDB.open(name, 1);
        r.onupgradeneeded = () => r.result.createObjectStore('t', { keyPath: 'id' });
        r.onsuccess = () => resolve(r.result);
        r.onerror = () => reject(r.error);
    });
    const bytes = new Uint8Array(256 * 1024);
    for (let i = 0; i < bytes.length; i++) bytes[i] = i % 251;
    let db = await open();
    let tx = db.transaction(['t'], 'readwrite');
    tx.objectStore('t').put({ id: 'a', text: 'Hej æøå 🌺', data: bytes });
    await completion(tx);
    db.close();
    db = await open();
    tx = db.transaction(['t'], 'readonly');
    const record = await req(tx.objectStore('t').get('a'));
    db.close();
    await new Promise(resolve => { const d = indexedDB.deleteDatabase(name); d.onsuccess = d.onerror = d.onblocked = () => resolve(); });
    const ok = !!record && record.text === 'Hej æøå 🌺' && record.data && record.data.length === bytes.length && record.data[1000] === bytes[1000];
    return ok;
}
