# Phase 1 tasks

## Task 1: Scaffold solution and projects — done

**Acceptance criteria:** .NET 10 solution contains Api, Application, Domain, Infrastructure, UnitTests and IntegrationTests with correct reference direction.
**Verification:** `dotnet build LinkForge.slnx`.
**Dependencies:** None.
**Files likely touched:** solution and project files.
**Estimated scope:** Medium.

## Task 2: Implement Base62 and URL validation — done

**Acceptance criteria:** Codec round-trips boundary values and emits seven characters; validator rejects non-HTTP(S), credentials, controls and overlong URLs.
**Verification:** `dotnet test tests/UrlShortener.UnitTests/UrlShortener.UnitTests.csproj`.
**Dependencies:** Task 1.
**Files likely touched:** Domain/Application and unit test files.
**Estimated scope:** Medium.

## Checkpoint: Foundation

- [x] Solution builds.
- [x] Unit tests pass.

## Task 3: Add PostgreSQL schema and migration — done

**Acceptance criteria:** Migration creates `users`, `urls`, unique short code, foreign key and ID sequence; migration applies to a clean database.
**Verification:** Apply migration to temporary PostgreSQL.
**Dependencies:** Task 1.
**Files likely touched:** Infrastructure database files and migration.
**Estimated scope:** Medium.

## Task 4: Add create API — done

**Acceptance criteria:** Valid POST returns 201, URL/code/token; codes are unique and persisted; invalid input returns validation error.
**Verification:** Integration tests against PostgreSQL.
**Dependencies:** Tasks 2, 3.
**Files likely touched:** API, Application, Infrastructure and integration tests.
**Estimated scope:** Medium.

## Checkpoint: Create path

- [x] Migration applies.
- [x] Two creates produce distinct codes.

## Task 5: Add redirect and metadata API — done

**Acceptance criteria:** Existing code redirects with 302, absent code returns 404, public metadata contains no management token.
**Verification:** Integration tests against PostgreSQL.
**Dependencies:** Task 4.
**Files likely touched:** API, Application and integration tests.
**Estimated scope:** Medium.

## Task 6: Add protected delete and exception handling — done

**Acceptance criteria:** Valid token deletes; absent/wrong token cannot delete; deleted code returns 404; unexpected exceptions do not disclose internals.
**Verification:** Integration tests against PostgreSQL.
**Dependencies:** Task 5.
**Files likely touched:** API, Application, Infrastructure and integration tests.
**Estimated scope:** Medium.

## Task 7: Document and review Phase 1 — done

**Acceptance criteria:** README gives setup, migration and API examples; requirements reflect selected decisions; build and tests pass.
**Verification:** Run documented commands and `git diff --check`.
**Dependencies:** Tasks 1–6.
**Files likely touched:** README, docs, task tracking.
**Estimated scope:** Small.

## Checkpoint: Complete

- [x] Full test suite passes.
- [x] API create → redirect → delete works end to end.
- [x] Phase 1 documentation is current.

## Phase 2 tasks

## Task 8: Add health checks and opt-in startup migration — done

**Acceptance criteria:** The API exposes liveness without dependencies, readiness with a PostgreSQL check, and can apply pending migrations only when configured.
**Verification:** Integration tests cover both health endpoints; solution build and tests pass.
**Dependencies:** Task 7.
**Files likely touched:** API, Infrastructure and integration tests.
**Estimated scope:** Medium.

## Task 9: Containerize the API — done

**Acceptance criteria:** A multi-stage Dockerfile produces a non-development runtime image with port 8080 and an HTTP health check; build context excludes local artifacts and secrets.
**Verification:** `docker build` succeeds and the image health check can reach `/health/ready` when run with a database.
**Dependencies:** Task 8.
**Files likely touched:** `Dockerfile`, `.dockerignore`.
**Estimated scope:** Small.

## Task 10: Add Compose deployment and persistence — done

**Acceptance criteria:** Compose starts PostgreSQL and the API with environment-driven configuration, waits for database health, and mounts a named PostgreSQL volume.
**Verification:** `docker compose up --build -d`, health checks, create/redirect, PostgreSQL restart, and persistence check.
**Dependencies:** Tasks 8–9.
**Files likely touched:** `docker-compose.yml`, `.env.example`, README and `.gitignore`.
**Estimated scope:** Medium.

## Checkpoint: Phase 2 complete

- [x] Full .NET test suite passes.
- [x] Docker image builds.
- [x] Compose services become healthy.
- [x] A URL survives PostgreSQL container restart with the named volume.
- [x] Phase 2 documentation and PLAN checkboxes are current.
## Phase 3: Redis redirect cache

### Task 11: Cache contract and redirect flow
- [x] Add Application cache abstraction and cache-aside redirect behavior.
- [x] Unit-test hit, miss, Redis failure, and delete race policy.

### Task 12: Redis integration
- [x] Implement Redis adapter, connection configuration, TTL, and tombstones.
- [x] Integration-test real Redis cache hit, miss, delete, and failure fallback.

### Task 13: Compose and runtime
- [x] Add bounded Redis service to Compose.
- [x] Document cache configuration and invalidation limits.
- [x] Verify Docker rebuild, Redis restart, and PostgreSQL-backed redirect.

## Client MVP

### Task 14: Serve the client
- [x] Publish `/app/` with the API without changing existing routes.
- [x] Verify `/app/` returns HTML and its assets load.

### Task 15: Create and copy
- [x] Build responsive, accessible create form and result states.
- [x] Wire create API, validation/error handling, and copy actions.
- [x] Verify the flow in a browser at desktop and mobile widths.

### Task 16: Handoff
- [x] Update README with the client URL and usage.
- [x] Run the solution tests, build, and Compose runtime check.

## Authentication MVP

### Task 17: Account schema and local API
- [x] Migrate user password and Google identity fields while retaining existing data.
- [x] Implement signup, login, logout, current-user, CSRF, and cookie session tests.

### Task 18: Google OAuth
- [x] Configure Google challenge, callback, account creation and collision behavior.
- [x] Verify disabled/unconfigured and configured challenge paths without committing secrets.

### Task 19: Browser pages and runtime
- [x] Build responsive signup/login pages and show account state in `/app/`.
- [x] Verify browser form flows, authenticated ownership, tests, build, and Compose.
