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
