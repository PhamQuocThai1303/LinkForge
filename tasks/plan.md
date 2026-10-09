# Implementation Plan: Phase 1 and Phase 2 URL shortener

## Overview

Build the first working LinkForge API on .NET 10 and PostgreSQL. Create distinct seven-character Base62 codes, redirect with HTTP 302, expose URL metadata, and allow deletion only with a one-time management token returned at creation.

## Architecture decisions

- API → Application → Domain; Infrastructure implements Application interfaces.
- PostgreSQL sequence allocates IDs atomically. Start at 100,000,000,000 to match the seven-character example in PLAN.md. Keep `short_code` unique in the database.
- Anonymous create is allowed for the learning MVP. Each create request produces a new code. Delete requires a high-entropy token in `X-Management-Token`; store only its SHA-256 hash. Public metadata omits the token.
- Accept absolute HTTP(S) URLs up to 2,048 characters with a nonempty DNS host. Reject credentials and control characters. No network fetch is performed.
- Create is unsafe to retry after an unknown outcome; there is no idempotency-key contract in Phase 1.
- Keep the users table for the planned account model, with nullable `user_id` until accounts are implemented.

## Dependencies and order

1. Solution, projects, references, test harness.
2. Base62 and URL validation with unit tests.
3. Database schema and migration.
4. Create endpoint from HTTP through PostgreSQL, tested end to end.
5. Redirect and metadata endpoints, tested end to end.
6. Token-protected delete and error handling, tested end to end.
7. Documentation, local run instructions, final review.

## Checkpoints

- After 2: solution builds and unit tests pass.
- After 4: migration works and create returns a unique persisted code.
- After 6: all endpoint and authorization tests pass against PostgreSQL.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Token leaks in logs or metadata | Store hash only; omit token from read response and logs. |
| Concurrent ID/code collision | PostgreSQL sequence and unique index. |
| Sequence and generated code drift | Convert the allocated ID using one canonical Base62 codec. |
| PostgreSQL unavailable during local verification | Run test database in a temporary Docker container; document external dependency. |

## Open questions

None blocking Phase 1. Account ownership and custom aliases remain later-phase decisions.

## Phase 2: Dockerize application

### Objective

Run the API and PostgreSQL together with Docker Compose, while keeping the API configuration externalized and the database durable across container restarts.

### Architecture decisions

- Use a multi-stage Alpine Dockerfile so the runtime image contains only the published API.
- Keep PostgreSQL as the source of truth and attach a named volume at `/var/lib/postgresql`, matching the PostgreSQL 18 image layout.
- Compose waits for PostgreSQL's `pg_isready` health check before starting the API.
- The API exposes `/health/live` without dependencies and `/health/ready` with a PostgreSQL URL-schema check.
- Automatic EF migration is opt-in through `Database:MigrateOnStartup`; Compose enables it so a clean `docker compose up` is usable, while normal local runs preserve the manual migration workflow.
- Use environment variables for the connection string, environment name, public short-link base URL, and host port. Local Compose defaults are intentionally development-only.

### Implementation order

1. Add health-check contracts and opt-in startup migration, with integration coverage.
2. Add the multi-stage Dockerfile and ignore rules; build the image.
3. Add Compose services, health dependencies, environment configuration, and the PostgreSQL volume.
4. Update runbook and phase tracking; verify create, redirect, persistence, and health behavior through Compose.

### Risks and mitigations

| Risk | Mitigation |
|---|---|
| API starts before PostgreSQL accepts connections | Compose `service_healthy` dependency and PostgreSQL `pg_isready` health check. |
| Startup migration is accidentally enabled outside Compose | Configuration defaults to false; only Compose sets `Database__MigrateOnStartup=true`. |
| Database data disappears when the container is replaced | Named Docker volume and an explicit persistence verification. |
| Runtime image lacks a probe client | Use the Alpine runtime's `wget` in the container health check and verify the image at runtime. |

### Phase 2 acceptance criteria

- `docker compose up --build -d` starts a healthy API and PostgreSQL.
- `GET /health/live` returns `200` even when readiness dependencies are excluded.
- `GET /health/ready` returns `200` only when PostgreSQL is reachable and the schema is available.
- The API is reachable from the host and can create and redirect a URL.
- Replacing/restarting the PostgreSQL container with the Compose volume attached preserves the URL.
- `docker compose down` leaves the named volume available; `docker compose down -v` is the explicit destructive reset.

## Phase 3: Redis redirect cache

### Objective

Reduce PostgreSQL lookups for repeated redirects while preserving PostgreSQL as the source of truth.

### Contract and decisions

- `IUrlCache` in Application stores `url:{shortCode}` to original URL for one hour. Infrastructure implements it with StackExchange.Redis. With no Redis configuration, use a no-op cache for local PostgreSQL-only runs.
- Redirect is cache-aside: hit returns HTTP 302 without repository lookup; miss reads PostgreSQL and fills Redis. Metadata continues to read PostgreSQL.
- Redis errors are logged and treated as cache misses or skipped writes. PostgreSQL readiness does not depend on Redis.
- After a successful database delete, write a one-hour tombstone to Redis. Miss fills use SET NX, so a concurrent stale database read cannot overwrite a tombstone. Generated codes are never reused.
- Redis is ephemeral with no persistence. Memory policy is configurable; start with a bounded memory allocation and LRU eviction. A transient network partition exactly during delete may leave a stale cached value until TTL expiry; stronger cross-system delete guarantees require a durable invalidation mechanism.

### Implementation and verification

1. Add cache abstraction and redirect path with focused unit tests for hit, miss, failure, and delete race.
2. Add Redis adapter and configuration; exercise real Redis in integration tests for hit, miss, delete, and unavailable behavior.
3. Add Compose Redis service, documentation, and run-time checks including Redis restart and PostgreSQL-backed fallback.

## Client MVP

### Objective and contract

Use the existing create API from a same-origin browser page at `/app/`. Display the generated short URL and one-time management token with copy actions. Preserve the current API and root service-status response. See `docs/client-mvp.md` for acceptance criteria.

### Implementation order

1. Serve static files at `/app/` and verify the HTTP route.
2. Build the responsive form, result state, error state, and copy actions.
3. Verify a real create/copy flow in a browser, run the .NET test suite, rebuild Compose, and document the client URL.

### Risks

| Risk | Mitigation |
|---|---|
| Public API origin differs from the page origin | Use relative API requests; Compose already sets the public short-link base URL. |
| Management token is lost or exposed | Show it once, keep it in page memory, and avoid storage or logs. |

## Authentication MVP

### Build order

1. Extend the user schema and migrate existing PostgreSQL data.
2. Add local signup/login, cookie sessions, CSRF handling, and integration tests.
3. Add Google OAuth with optional environment configuration and callback tests.
4. Add login/signup UI and signed-in state to the existing client.
5. Rebuild Compose and verify desktop/mobile and API flows.

### Decisions and risks

- Use ASP.NET Core cookies for same-origin browser sessions; keep anonymous URL creation available.
- A signed-in create records user ID without changing the management token contract.
- Reject Google/password email collisions rather than linking identities implicitly.
- Persist Data Protection keys in Compose so replacing the app container does not log users out.
- Google consent requires the app owner's OAuth credentials; all local flows and the challenge route can be verified without them.
