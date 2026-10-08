# Implementation Plan: Phase 1 URL shortener

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
