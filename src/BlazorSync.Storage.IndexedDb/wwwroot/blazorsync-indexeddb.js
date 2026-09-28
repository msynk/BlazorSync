// BlazorSync IndexedDB store: a deliberately small bridge. All protocol logic stays in .NET.
//
// Layout (schema version 3), one database per account namespace:
//   records: keyPath ["collection", "id"]; values carry serialized documents as strings and every 64-bit
//            number as a decimal string (JavaScript numbers lose precision above 2^53).
//            Sparse index keys (present only when the record qualifies):
//              pendingKey [collection, updatedAt, id]   dirty and not rejected (push queue order)
//              dirtyKey   [collection, id]              dirty (count)
//              staleKey   [collection, id]              clean and not missing (resnapshot sweep)
//              visibleKey [collection, id]              not missing (queries)
//              liveKey    [collection, id]              not missing and not deleted (queries)
//              conflictKey [collection, id]             an unresolved conflict is kept (schema 2)
//   meta:    keyPath ["collection", "key"]
//
// Writes are optimistic: .NET reads records with their write stamps, computes new states, and commits in
// one readwrite transaction that aborts if any stamp changed. No transaction spans a .NET await.

const SCHEMA_VERSION = 3;
const databases = new Map();
let nextHandle = 1;

function fail(code, message) {
  const error = new Error(`BlazorSync:${code}:${message}`);
  error.name = "BlazorSyncStoreError";
  return error;
}

function classify(error) {
  if (error && typeof error.message === "string" && error.message.startsWith("BlazorSync:")) {
    return error;
  }
  const name = error && error.name;
  if (name === "QuotaExceededError") return fail("quota", "Storage quota exceeded.");
  if (name === "VersionError") return fail("outdated", "The database was upgraded by a newer version of the application.");
  if (name === "InvalidStateError" || name === "SecurityError" || name === "UnknownError") {
    return fail("unavailable", `IndexedDB is unavailable (${name}).`);
  }
  return fail("error", `${name || "Error"}: ${(error && error.message) || error}`);
}

function request(req) {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(classify(req.error));
  });
}

function openDatabase(name, blockedTimeoutMs) {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === "undefined" || indexedDB === null) {
      reject(fail("unavailable", "IndexedDB is not available in this browser context."));
      return;
    }
    let req;
    try {
      req = indexedDB.open(name, SCHEMA_VERSION);
    } catch (error) {
      reject(classify(error));
      return;
    }
    let blockedTimer = null;
    req.onupgradeneeded = (event) => {
      const db = req.result;
      if (event.oldVersion < 1) {
        const records = db.createObjectStore("records", { keyPath: ["collection", "id"] });
        records.createIndex("pending", "pendingKey");
        records.createIndex("dirty", "dirtyKey");
        records.createIndex("stale", "staleKey");
        records.createIndex("visible", "visibleKey");
        records.createIndex("live", "liveKey");
        db.createObjectStore("meta", { keyPath: ["collection", "key"] });
      }
      if (event.oldVersion < 2) {
        // 1 -> 2: unresolved conflicts. Additive; existing records are untouched.
        req.transaction.objectStore("records").createIndex("conflicts", "conflictKey");
      }
      // 2 -> 3: dependency groups are plain record fields (no index). The version still changes so that tabs running
      // an older application, which would drop the group fields when writing, are closed ("outdated").
    };
    req.onblocked = () => {
      blockedTimer = setTimeout(() => reject(fail("blocked", "Another tab keeps an older version of the database open.")), blockedTimeoutMs);
    };
    req.onsuccess = () => {
      if (blockedTimer) clearTimeout(blockedTimer);
      resolve(req.result);
    };
    req.onerror = () => {
      if (blockedTimer) clearTimeout(blockedTimer);
      reject(classify(req.error));
    };
  });
}

async function connection(handle) {
  const entry = databases.get(handle);
  if (!entry) throw fail("closed", "The store has been disposed.");
  if (entry.closedReason) throw fail(entry.closedReason, "The database was closed because another tab upgraded it; reload the page.");
  return entry.db;
}

export async function open(name, blockedTimeoutMs) {
  const db = await openDatabase(name, blockedTimeoutMs);
  const handle = nextHandle++;
  const entry = { db, name, closedReason: null };
  // Another tab wants a newer schema: step aside instead of blocking it, and fail later calls explicitly.
  db.onversionchange = () => {
    db.close();
    entry.closedReason = "outdated";
  };
  db.onclose = () => {
    entry.closedReason = entry.closedReason || "unavailable";
  };
  databases.set(handle, entry);

  // Replica identity is created once per database.
  const tx = db.transaction("meta", "readwrite");
  const meta = tx.objectStore("meta");
  const existing = await request(meta.get(["", "replicaId"]));
  if (!existing) {
    meta.put({ collection: "", key: "replicaId", value: crypto.randomUUID().replaceAll("-", "") });
    meta.put({ collection: "", key: "incarnation", value: crypto.randomUUID().replaceAll("-", "") });
  }
  await transactionDone(tx);
  return handle;
}

export function close(handle) {
  const entry = databases.get(handle);
  if (entry) {
    entry.db.close();
    databases.delete(handle);
  }
}

function transactionDone(tx) {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(classify(tx.error));
    tx.onabort = () => reject(classify(tx.error || new DOMException("Transaction aborted", "AbortError")));
  });
}

function decorate(collection, record) {
  const id = record.id;
  const rejected = record.rejectionCode !== null && record.rejectionCode !== undefined;
  const stored = { ...record, collection };
  delete stored.pendingKey;
  delete stored.dirtyKey;
  delete stored.staleKey;
  delete stored.visibleKey;
  delete stored.liveKey;
  delete stored.conflictKey;
  if (record.isDirty && !rejected) stored.pendingKey = [collection, record.updatedAt, id];
  if (record.isDirty) stored.dirtyKey = [collection, id];
  if (!record.isDirty && !record.missing) stored.staleKey = [collection, id];
  if (!record.missing) stored.visibleKey = [collection, id];
  if (!record.missing && !record.deleted) stored.liveKey = [collection, id];
  if (record.conflictLocal !== null && record.conflictLocal !== undefined) stored.conflictKey = [collection, id];
  return stored;
}

function strip(stored) {
  if (!stored) return null;
  const { collection, pendingKey, dirtyKey, staleKey, visibleKey, liveKey, conflictKey, ...record } = stored;
  return record;
}

export async function readMany(handle, collection, ids) {
  const db = await connection(handle);
  const tx = db.transaction(["records", "meta"], "readonly");
  const records = tx.objectStore("records");
  const meta = tx.objectStore("meta");
  const results = await Promise.all(ids.map((id) => request(records.get([collection, id]))));
  const highWater = await request(meta.get([collection, "clockHighWater"]));
  return JSON.stringify({
    records: results.map(strip),
    highWater: highWater ? highWater.value : null,
  });
}

// writesJson: [{ record, expectedStamp }] for every id in the batch (record null = unchanged).
// Returns "ok" or "conflict" (a stamp changed; the caller re-reads and retries).
export async function commit(handle, collection, entriesJson, metaJson) {
  const db = await connection(handle);
  const entries = JSON.parse(entriesJson);
  const metaUpdate = metaJson ? JSON.parse(metaJson) : null;
  // "strict" asks the browser to flush before reporting completion; engines without the option ignore it.
  const tx = db.transaction(["records", "meta"], "readwrite", { durability: "strict" });
  const done = transactionDone(tx);
  const records = tx.objectStore("records");
  const meta = tx.objectStore("meta");
  let outcome = "ok";
  try {
    const current = await Promise.all(entries.map((e) => request(records.get([collection, e.id]))));
    for (let i = 0; i < entries.length; i++) {
      const stamp = current[i] ? current[i].stamp : null;
      if (stamp !== entries[i].expectedStamp) {
        outcome = "conflict";
        break;
      }
    }

    if (outcome === "ok" && metaUpdate && metaUpdate.cursor) {
      const generation = await request(meta.get([collection, "generation"]));
      if (generation && BigInt(generation.value) > BigInt(metaUpdate.cursor.generation)) {
        outcome = "stale-generation";
      }
    }

    if (outcome !== "ok") {
      tx.abort();
      await done.catch(() => {});
      if (outcome === "stale-generation") {
        throw fail("stale-generation", "Another replica session already moved this store to a newer generation.");
      }
      return outcome;
    }

    for (const entry of entries) {
      if (entry.record) {
        const stored = decorate(collection, { ...entry.record, stamp: crypto.randomUUID() });
        records.put(stored);
      }
    }

    if (metaUpdate) {
      if (metaUpdate.highWater) {
        const existing = await request(meta.get([collection, "clockHighWater"]));
        if (!existing || existing.value < metaUpdate.highWater) {
          meta.put({ collection, key: "clockHighWater", value: metaUpdate.highWater });
        }
      }
      if (metaUpdate.cursor) {
        if (metaUpdate.cursor.checkpoint === null) {
          meta.delete([collection, "checkpoint"]);
        } else {
          meta.put({ collection, key: "checkpoint", value: metaUpdate.cursor.checkpoint });
        }
        meta.put({ collection, key: "generation", value: metaUpdate.cursor.generation });
        meta.put({ collection, key: "resnapshot", value: metaUpdate.cursor.resnapshot });
        meta.put({ collection, key: "purgeMissing", value: metaUpdate.cursor.purgeMissing });
      }
    }
  } catch (error) {
    try { tx.abort(); } catch { /* already finished */ }
    await done.catch(() => {});
    throw classify(error);
  }
  await done;
  return "ok";
}

async function collect(index, range, limit, accept) {
  const found = [];
  await new Promise((resolve, reject) => {
    const req = index.openCursor(range);
    req.onerror = () => reject(classify(req.error));
    req.onsuccess = () => {
      const cursor = req.result;
      if (!cursor || found.length >= limit) {
        resolve();
        return;
      }
      if (accept(cursor.value)) found.push(strip(cursor.value));
      cursor.continue();
    };
  });
  return found;
}

function prefix(collection) {
  // Every string sorts below an array, so [collection, []] is past every [collection, "..."] key.
  return IDBKeyRange.bound([collection], [collection, []]);
}

// Removes clean records of older generations (not seen by a completed resnapshot); dirty ones are kept.
export async function purge(handle, collection, ids, generation) {
  const db = await connection(handle);
  const g = BigInt(generation);
  const tx = db.transaction("records", "readwrite", { durability: "strict" });
  const done = transactionDone(tx);
  const records = tx.objectStore("records");
  let removed = 0;
  const current = await Promise.all(ids.map((id) => request(records.get([collection, id]))));
  for (const record of current) {
    if (record && !record.isDirty && (record.conflictLocal === null || record.conflictLocal === undefined) && BigInt(record.generation) < g) {
      records.delete([collection, record.id]);
      removed++;
    }
  }
  await done;
  return removed;
}

export async function pending(handle, collection, limit, exclude) {
  const db = await connection(handle);
  const excluded = new Set(exclude);
  const index = db.transaction("records", "readonly").objectStore("records").index("pending");
  return JSON.stringify(await collect(index, prefix(collection), limit, (r) => !excluded.has(r.id)));
}

export async function stale(handle, collection, generation, limit) {
  const db = await connection(handle);
  const g = BigInt(generation);
  const index = db.transaction("records", "readonly").objectStore("records").index("stale");
  return JSON.stringify(await collect(index, prefix(collection), limit, (r) => BigInt(r.generation) < g));
}

export async function conflicts(handle, collection, limit) {
  const db = await connection(handle);
  const index = db.transaction("records", "readonly").objectStore("records").index("conflicts");
  return JSON.stringify(await collect(index, prefix(collection), limit, () => true));
}

// Rejected records are dirty, so the sparse dirty index bounds the scan.
export async function rejected(handle, collection, limit) {
  const db = await connection(handle);
  const index = db.transaction("records", "readonly").objectStore("records").index("dirty");
  return JSON.stringify(await collect(index, prefix(collection), limit, (r) => r.rejectionCode !== null && r.rejectionCode !== undefined));
}

// One bounded page in id order; string keys compare by UTF-16 code units, which is ordinal order.
export async function queryPage(handle, collection, afterId, limit, includeDeleted) {
  const db = await connection(handle);
  const index = db.transaction("records", "readonly").objectStore("records").index(includeDeleted ? "visible" : "live");
  const range = afterId === null || afterId === undefined
    ? prefix(collection)
    : IDBKeyRange.bound([collection, afterId], [collection, []], true, false);
  const records = await collect(index, range, limit, () => true);
  return JSON.stringify(records.map((r) => r.current));
}

export async function query(handle, collection, includeDeleted) {
  const db = await connection(handle);
  const index = db.transaction("records", "readonly").objectStore("records").index(includeDeleted ? "visible" : "live");
  const records = await collect(index, prefix(collection), Number.MAX_SAFE_INTEGER, () => true);
  return JSON.stringify(records.map((r) => r.current));
}

export async function countDirty(handle, collection) {
  const db = await connection(handle);
  const index = db.transaction("records", "readonly").objectStore("records").index("dirty");
  return await request(index.count(prefix(collection)));
}

export async function getMeta(handle, collection) {
  const db = await connection(handle);
  const store = db.transaction("meta", "readonly").objectStore("meta");
  const read = async (scope, key) => {
    const value = await request(store.get([scope, key]));
    return value ? value.value : null;
  };
  return JSON.stringify({
    checkpoint: await read(collection, "checkpoint"),
    generation: (await read(collection, "generation")) ?? "0",
    resnapshot: (await read(collection, "resnapshot")) ?? false,
    purgeMissing: (await read(collection, "purgeMissing")) ?? false,
    highWater: await read(collection, "clockHighWater"),
    replicaId: await read("", "replicaId"),
    incarnation: await read("", "incarnation"),
  });
}

export async function newIncarnation(handle) {
  const db = await connection(handle);
  const tx = db.transaction("meta", "readwrite");
  tx.objectStore("meta").put({ collection: "", key: "incarnation", value: crypto.randomUUID().replaceAll("-", "") });
  await transactionDone(tx);
}

export async function deleteDatabase(name) {
  await request(indexedDB.deleteDatabase(name));
}

export async function requestPersistence() {
  return !!(navigator.storage && navigator.storage.persist && (await navigator.storage.persist()));
}

export async function estimate() {
  if (!navigator.storage || !navigator.storage.estimate) return JSON.stringify({ usage: null, quota: null });
  const { usage, quota } = await navigator.storage.estimate();
  return JSON.stringify({ usage: usage ?? null, quota: quota ?? null });
}

// ---- Lifecycle: ask .NET to sync when the network returns or the tab becomes visible again (timers may have
// been throttled or frozen while it was hidden). navigator.onLine is never treated as proof the server is up.
const watches = new Map();
let nextWatch = 1;

export function watchLifecycle(dotnetRef) {
  const wake = () => dotnetRef.invokeMethodAsync("Wake").catch(() => { });
  const onVisibility = () => { if (document.visibilityState === "visible") wake(); };
  window.addEventListener("online", wake);
  document.addEventListener("visibilitychange", onVisibility);
  const id = nextWatch++;
  watches.set(id, () => {
    window.removeEventListener("online", wake);
    document.removeEventListener("visibilitychange", onVisibility);
  });
  return id;
}

export function unwatchLifecycle(id) {
  const stop = watches.get(id);
  if (stop) {
    watches.delete(id);
    stop();
  }
}

// ---- Tab ownership (Web Locks). The lock is released when the holder releases it, or when its tab closes
// or crashes, so a stale owner cannot keep it.
const leases = new Map();
let nextLease = 1;

export async function tryAcquireLease(name) {
  if (!navigator.locks) throw fail("unavailable", "Web Locks are not available in this browser.");
  let release;
  const released = new Promise((resolve) => { release = resolve; });
  const acquired = await new Promise((resolve) => {
    navigator.locks.request(name, { ifAvailable: true }, (lock) => {
      if (!lock) {
        resolve(false);
        return undefined;
      }
      resolve(true);
      return released;
    });
  });
  if (!acquired) return 0;
  const id = nextLease++;
  leases.set(id, release);
  return id;
}

export function releaseLease(id) {
  const release = leases.get(id);
  if (release) {
    leases.delete(id);
    release();
  }
}
