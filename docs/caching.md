# Redirect cache (Phase 3)

PostgreSQL is the URL source of truth. Redis stores only `url:{shortCode} -> originalUrl` for one hour. Only `GET /{shortCode}` uses the cache; metadata and delete authorization use PostgreSQL.

On a hit, redirect returns HTTP 302 without querying PostgreSQL. On a miss, the API reads PostgreSQL and writes the mapping to Redis with a one-hour TTL. Unknown codes are not cached. Redis runs with 128 MB max memory and `allkeys-lru` eviction in local Compose; it has no persistence.

After a successful database delete, the API writes a one-hour `deleted` marker for the code. A concurrent cache miss fills with SET NX, so an older read cannot overwrite the marker. Redirect checks for the marker again after filling. Generated codes are not reused. When Redis is unavailable, requests fall back to PostgreSQL and the readiness endpoint remains based on PostgreSQL alone.

There is a distributed failure limit: if PostgreSQL delete succeeds while Redis cannot be reached, a previously cached redirect may remain valid until its TTL expires (at most one hour, unless evicted earlier). Redis restart clears the cache and next redirect repopulates it from PostgreSQL. A stronger deletion guarantee would need durable invalidation or a database check on each redirect, which would defeat the current cache objective.

Phase 5 will measure active short codes, cache hit/miss ratio, Redis memory per entry, and latency before tuning TTL or memory.
