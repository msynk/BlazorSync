// Caches the application shell (not data) so the app starts offline. Notes are stored in IndexedDB by
// Bsync, never in this cache, and sync requests always go to the network.
self.importScripts("./service-worker-assets.js");
self.addEventListener("install", event => event.waitUntil(onInstall()));
self.addEventListener("activate", event => event.waitUntil(onActivate()));
self.addEventListener("fetch", event => {
    // Sync traffic (including the long-lived hint stream) never goes through the worker: an in-flight proxied
    // request would keep this worker alive and block a new version from taking over.
    if (event.request.method !== "GET" || new URL(event.request.url).pathname.startsWith("/sync/")) return;
    event.respondWith(onFetch(event));
});
// A new version waits until the page asks it to take over (see the banner in index.html).
self.addEventListener("message", event => {
    if (event.data && event.data.type === "SKIP_WAITING") self.skipWaiting();
});

const cacheNamePrefix = "offline-cache-";
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];
const baseUrl = new URL("/", self.origin);
const manifestUrls = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall() {
    const requests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: "no-cache" }));
    const cache = await caches.open(cacheName);
    await cache.addAll(requests);
}

async function onActivate() {
    const keys = await caches.keys();
    await Promise.all(keys.filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName).map(key => caches.delete(key)));
}

async function onFetch(event) {
    const serveIndex = event.request.mode === "navigate" && !manifestUrls.includes(event.request.url);
    const cache = await caches.open(cacheName);
    const cached = await cache.match(serveIndex ? "index.html" : event.request);
    return cached || fetch(event.request);
}
