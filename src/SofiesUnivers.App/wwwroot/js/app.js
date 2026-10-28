// Small browser helpers: theme, preferences, install state, app updates.

const THEME_KEY = 'sofies-univers.theme';
const PREFIX = 'sofies-univers.pref.';

const THEME_COLORS = {
    boheme: '#b5452f',
    victoriansk: '#5b2245',
    hippie: '#b45309',
    muse: '#141a42',
    diva: '#a3164b',
};

export function getTheme() {
    try { return localStorage.getItem(THEME_KEY); } catch { return null; }
}

export function setTheme(name) {
    const root = document.documentElement;
    root.dataset.theme = name;
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta && THEME_COLORS[name]) meta.setAttribute('content', THEME_COLORS[name]);
    try { localStorage.setItem(THEME_KEY, name); } catch { /* storage may be unavailable */ }
}

export function getPref(key) {
    try { return localStorage.getItem(PREFIX + key); } catch { return null; }
}

export function setPref(key, value) {
    try {
        if (value === null || value === undefined) localStorage.removeItem(PREFIX + key);
        else localStorage.setItem(PREFIX + key, value);
    } catch { /* ignore */ }
}

export function isStandalone() {
    return window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
}

export function isIos() {
    const ua = navigator.userAgent || '';
    return /iPhone|iPad|iPod/.test(ua) || (ua.includes('Macintosh') && navigator.maxTouchPoints > 1);
}

export function confirmLeave(message) {
    return window.confirm(message);
}

export function scrollToTop() {
    window.scrollTo({ top: 0, behavior: 'instant' in window ? 'instant' : 'auto' });
}

export function focusElement(element) {
    try { element && element.focus(); } catch { /* ignore */ }
}

export function scrollIntoView(selector) {
    const el = document.querySelector(selector);
    if (el) el.scrollIntoView({ block: 'center', behavior: 'smooth' });
}

export function userAgent() {
    return navigator.userAgent;
}

// Notifies .NET when a new version of the app has been downloaded and is waiting.
export function watchForUpdates(dotnetRef) {
    if (!('serviceWorker' in navigator)) return;
    const notify = () => dotnetRef.invokeMethodAsync('OnUpdateAvailable').catch(() => {});
    navigator.serviceWorker.getRegistration().then(reg => {
        if (!reg) return;
        if (reg.waiting && navigator.serviceWorker.controller) notify();
        reg.addEventListener('updatefound', () => {
            const worker = reg.installing;
            if (!worker) return;
            worker.addEventListener('statechange', () => {
                if (worker.state === 'installed' && navigator.serviceWorker.controller) notify();
            });
        });
        // Check for a new version when the app returns to the foreground.
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') reg.update().catch(() => {});
        });
    });
}

export function applyUpdate() {
    if (!('serviceWorker' in navigator)) { location.reload(); return; }
    navigator.serviceWorker.getRegistration().then(reg => {
        if (reg && reg.waiting) {
            navigator.serviceWorker.addEventListener('controllerchange', () => location.reload(), { once: true });
            reg.waiting.postMessage({ type: 'SKIP_WAITING' });
        } else {
            location.reload();
        }
    });
}
