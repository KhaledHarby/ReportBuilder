# Cache refresh strategy

Compare two proposals with the existing dataset cache path:

1. Run cache load/refresh as a **background job**, not an HTTP request.
2. Read the source in **chunks with keyset pagination** instead of loading everything at once.

**Source:** `DatasetCacheService`, `DatasetCacheRefreshQueue`, `DatasetCacheHostedService`, and the Mongo / SQL Server / Redis / Postgres / ClickHouse stores.

## Opinion

Adopt both ideas, but do not treat them as a greenfield rewrite.

HTTP already returns immediately. The real gaps are a **durable job** (IIS recycle-safe) with **live progress**, and **chunked source reads** only where a streaming `SELECT *` is unsafe.

Keep storing the **full dataset** in cache. Keyset pagination does not replace SQL Server `SELECT * INTO`, and it does not fix 50M report runs that load the whole Mongo snapshot into RAM.

## Apply vs ignore

| Item | Verdict | Why |
| --- | --- | --- |
| HTTP enqueue + catalog 3s poll | **Already done — keep** | Refresh already returns immediately. Do not rebuild this. |
| Durable job row + startup resume | **Apply** | In-memory `Channel` dies on IIS recycle and leaves `Refreshing` stuck. Persist work on `DatasetCacheState` and pick it up on startup. |
| Progress + heartbeat (`rowsCopied`, `estimatedTotal`, `LastHeartbeatAt`) | **Apply** | Catalog already polls. Show percent. Clear stuck after N minutes without heartbeat, not 24 hours. |
| Stream Redis like Mongo (no full `List` in RAM) | **Applying** | Refresh streams SQL → Redis. Reports stream the list and keep only the current page. |
| Query the cache in place (filter + skip/take in Mongo/SQL) | **Applying** | Mongo queries the collection. Redis streams and pages in the API (no query engine). SQL Server cache already queried in place. |
| Keyset source reads where a unique indexed key exists | **Apply** | ClickHouse / Postgres / Mongo: re-open the source per chunk so a timeout or recycle can resume. |
| Keep `BackgroundService` (hosted worker) | **Apply** | Worker already exists. Persist the queue; do not swap the host. |
| Keep storing the **full dataset** in cache | **Apply** | Reports still need every row until the runner can query the cache in place. |
| Keep SQL Server `SELECT * INTO`; raise or remove the 600s timeout | **Apply** | Engine-side bulk copy. No app RAM. Fastest SQL snapshot path. |
| Hangfire (or Quartz) for a job id | **Ignore** | A SQL-backed queue plus `DatasetId` + `cacheVersion` is enough. Hangfire only if you later run a worker beside IIS. |
| Keyset / chunk the SQL Server `SELECT * INTO` path | **Ignore** | Slower and more fragile than engine bulk copy. |
| `OFFSET` / `FETCH` paging of the source | **Ignore** | Cost grows with every page; will look hung around page 500+ at 15M rows. |
| `COUNT(*)` on every catalog poll | **Ignore** | Estimate once at start (`sys.partitions` / `COUNT_BIG` with `NOLOCK`). |
| Stop caching the full dataset | **Ignore** | Only valid if reports can query the live source with acceptable latency. |
| Expect refresh chunks to fix report 502s | **Ignore** | `GetSnapshotAsync` still loads the whole collection into RAM on run. |
| Greenfield rewrite of the cache path | **Ignore** | Adopt durable jobs + selective keyset on the current stores. |

| Area | Status |
| --- | --- |
| HTTP enqueue + catalog poll | Done |
| Durable job + progress | Gap |
| Resumable source chunks | Gap |
| Query the cache in place (report runs) | Mongo + Redis reports done |

## 1. Background job, not an HTTP request

The catalog Refresh button should enqueue work, return at once, and let the UI poll status. That is already the contract of `RequestRefreshAsync` plus `CatalogPage` `refetchInterval` of 3 seconds.

| Piece | Proposed | Existing | Verdict |
| --- | --- | --- | --- |
| HTTP click | Enqueue, return job id | `POST /cache/refresh` sets Refreshing and `TryEnqueue`, then returns the dataset | Already done |
| Worker | Hangfire, Quartz, or `BackgroundService` + queue | `DatasetCacheHostedService` drains an in-memory `Channel`, then `RefreshDue` every ~30s | Keep `BackgroundService`; persist the queue |
| Job identity | Return a job id | No job id. Status lives on `DatasetCacheState` | `DatasetId` + `cacheVersion` is enough unless you need history |
| Progress | Rows copied, percent, status, last error | Refreshing / Ready / Failed. `RowCount` and `DurationMs` only after success. `LastError` on fail. Catalog polls status only | Add `rowsCopied` + `estimatedTotal`; heartbeat while running |
| Survival | Outlives gateway / IIS timeouts | Survives HTTP timeout. Dies on app-pool recycle: `Channel` is in-process and unbounded | This is the actual hole |

### What still breaks a 15M / 50M refresh

The worker runs inside the API process. An IIS recycle, deploy, or idle timeout cancels the `SqlDataReader` and leaves `Status = Refreshing` until the 24-hour stuck cutoff.

There is one global reader, so one huge Mongo copy blocks every other dataset.

- SQL Server cache uses `SELECT * INTO` with a **600s** command timeout.
- ClickHouse / Postgres / Redis source reads also use **600s**.
- Mongo source `CommandTimeout` is **0** (unlimited) and writes 2,000-row `InsertMany` batches — the closest to a healthy copy today.

### Do not add Hangfire yet

A SQL-backed queue (or reuse `DatasetCacheState` as the job row) plus startup resume of Refreshing datasets gives durability without a new product.

Hangfire helps if you later run a Windows worker service beside IIS, or need retries / a dashboard. Quartz is unnecessary for a single queue of cache copies.

## 2. Read in chunks with keyset pagination

“Do not load all data into the cache” here means **do not pull the source in one giant in-memory or one-shot SQL operation**. The cache still needs every row for the current report runner.

| Provider | How it copies today | Keyset worth it? |
| --- | --- | --- |
| SQL Server cache | `SELECT * INTO` staging, then rename. Engine-side bulk copy. No app RAM. | **No.** Keyset would be slower and more fragile. Keep `SELECT * INTO`; raise or remove the 600s timeout. Maybe `BATCHSIZE` via SSIS/bcp later, not `OFFSET`. |
| Mongo | One `SELECT *` reader, stream rows, `InsertMany` 2,000. Logs every 250k. | **Optional.** Streaming already avoids RAM. Keyset helps resume after recycle if you persist last key on staging. |
| ClickHouse / Postgres | One `SELECT *` reader, insert in batches. Source command timeout 600s. | **Yes** if copies exceed ~10 minutes. Re-open the source per chunk so a timeout cannot kill the whole job. |
| Redis | `SELECT *` into a `List` of dictionaries, then push. Full snapshot in RAM. | **Must stream first.** Redis is a poor home for 15M+ rows anyway. |
| Report run (Mongo/Redis) | `GetSnapshotAsync` loads the entire collection into RAM, then filters. | **Unrelated to refresh chunks.** This is the 50M 502 / OOM. Push filters into Mongo/SQL instead. |

### Keyset caveats

- Keyset needs a unique, stable, indexed order: a primary key, or a composite the catalog already marks as indexed. Many MECS views will not have that.
- `OFFSET` / `FETCH` is not a substitute at 15M rows. Cost grows with every page and will look like a hang around page 500+.
- Fallback: keep one streaming `SELECT *` when no key exists. That is still better than `OFFSET`.
- Do not pause the copy to `COUNT(*)` on every poll. Estimate once at start (`sys.partitions` / `COUNT_BIG` with `NOLOCK`) so percent is honest enough.

## Recommended sequence

1. **Durable job row + startup resume**  
   `RequestRefresh` writes Refreshing; hosted service picks up Refreshing datasets after recycle even if the `Channel` is empty.

2. **Progress columns + heartbeat**  
   `RowsCopied`, `EstimatedTotal`, `LastHeartbeatAt`. Catalog already polls. Show percent. Clear stuck after N minutes without heartbeat, not 24 hours.

3. **Stream Redis like Mongo**  
   Stop building a `List` of all rows. Redis still should not be the 50M provider.

4. **Keyset source reads where a unique key exists**  
   ClickHouse / Postgres / Mongo resume. Leave SQL Server `SELECT * INTO` alone.

5. **Query the cache in place**  
   Mongo/SQL filter + skip/take in the store. Stops loading 50M rows to run a 50-row grid. This is a different project from refresh.

## What not to do

- Do not stop storing the full dataset in cache unless reports can query the live source with acceptable latency.
- Do not introduce Hangfire only to get a job id.
- Do not keyset the SQL Server snapshot path.
- Do not expect chunked writes to fix report 502s while `GetSnapshotAsync` still materializes every document.
