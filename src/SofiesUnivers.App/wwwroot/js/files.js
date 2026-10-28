// File helpers: share sheet / download, reading images for compression, previews.

function makeBlob(bytes, mime) {
    return new Blob([bytes], { type: mime || 'application/octet-stream' });
}

export function canShareFiles() {
    try {
        if (!navigator.canShare) return false;
        const probe = new File([new Uint8Array([1])], 'test.zip', { type: 'application/zip' });
        return navigator.canShare({ files: [probe] });
    } catch {
        return false;
    }
}

// Opens the system share sheet (iPhone: Mail, Gem i Filer, AirDrop …) and falls back to a normal download.
// Must be called directly from a click so the browser sees a user gesture.
// Returns "shared", "downloaded", "cancelled", "notallowed" or "failed:<reason>".
export async function shareOrDownload(bytes, fileName, mime, title) {
    const blob = makeBlob(bytes, mime);
    let file = null;
    try {
        file = new File([blob], fileName, { type: blob.type });
    } catch { /* very old browsers */ }

    if (file && navigator.canShare && navigator.share) {
        let canShare = false;
        try { canShare = navigator.canShare({ files: [file] }); } catch { canShare = false; }
        if (canShare) {
            try {
                await navigator.share({ files: [file], title: title || fileName });
                return 'shared';
            } catch (e) {
                if (e && e.name === 'AbortError') return 'cancelled';
                // The tap was "used up" (e.g. the file took long to prepare). The app then shows a
                // second button so Sofie can share with a fresh tap.
                if (e && e.name === 'NotAllowedError') return 'notallowed';
                // Anything else: fall through to download.
            }
        }
    }
    try {
        download(blob, fileName);
        return 'downloaded';
    } catch (e) {
        return 'failed:' + (e && e.message ? e.message : e);
    }
}

export function downloadBytes(bytes, fileName, mime) {
    download(makeBlob(bytes, mime), fileName);
    return 'downloaded';
}

function download(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.rel = 'noopener';
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export function createObjectUrl(bytes, mime) {
    return URL.createObjectURL(makeBlob(bytes, mime));
}

export function revokeObjectUrl(url) {
    try { URL.revokeObjectURL(url); } catch { /* ignore */ }
}

export function openBytes(bytes, mime) {
    const url = URL.createObjectURL(makeBlob(bytes, mime));
    const w = window.open(url, '_blank');
    setTimeout(() => URL.revokeObjectURL(url), 120_000);
    return !!w;
}

// Reads a file chosen in an <input type="file"> as bytes (avoids streaming through Blazor's InputFile limits).
export async function readInputFile(input, index) {
    const file = input && input.files && input.files[index || 0];
    if (!file) return null;
    const buffer = await file.arrayBuffer();
    return new Uint8Array(buffer);
}

export function inputFileInfo(input) {
    const file = input && input.files && input.files[0];
    if (!file) return null;
    return JSON.stringify({ name: file.name, type: file.type, size: file.size });
}

export function clearInput(input) {
    if (input) input.value = '';
}

export function clickElement(element) {
    if (element) element.click();
}

async function decodeImage(blob) {
    if (globalThis.createImageBitmap) {
        try {
            return await createImageBitmap(blob, { imageOrientation: 'from-image' });
        } catch { /* fall back */ }
    }
    return await new Promise((resolve, reject) => {
        const url = URL.createObjectURL(blob);
        const img = new Image();
        img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
        img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('Billedet kunne ikke læses.')); };
        img.src = url;
    });
}

// Scales a photo down so its longest side is at most maxSide pixels and re-encodes as JPEG.
// Receipts stay readable at 2400 px. Returns null if the browser cannot decode the image or the result is not smaller.
export async function compressImage(bytes, mime, maxSide, quality) {
    try {
        const source = await decodeImage(makeBlob(bytes, mime));
        const width = source.width, height = source.height;
        if (!width || !height) return null;
        const scale = Math.min(1, maxSide / Math.max(width, height));
        const w = Math.max(1, Math.round(width * scale));
        const h = Math.max(1, Math.round(height * scale));
        const canvas = document.createElement('canvas');
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext('2d');
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, w, h);
        ctx.drawImage(source, 0, 0, w, h);
        if (source.close) source.close();
        const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
        canvas.width = canvas.height = 0;
        if (!blob) return null;
        if (blob.size >= bytes.length && (mime === 'image/jpeg' || mime === 'image/png')) return null;
        return new Uint8Array(await blob.arrayBuffer());
    } catch {
        return null;
    }
}
