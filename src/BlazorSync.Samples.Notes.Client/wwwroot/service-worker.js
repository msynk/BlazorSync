// Development: no offline caching, so code changes are picked up on reload.
// The published build uses service-worker.published.js.
self.addEventListener("fetch", () => { });
self.addEventListener("message", event => {
    if (event.data && event.data.type === "SKIP_WAITING") self.skipWaiting();
});
