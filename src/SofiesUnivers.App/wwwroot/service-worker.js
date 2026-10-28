// Development service worker: no offline caching, so changes show up immediately.
// The published app uses service-worker.published.js.
self.addEventListener('fetch', () => { });
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SKIP_WAITING') self.skipWaiting();
});
