# LinkForge

LinkForge is a URL shortener built in stages from [PLAN.md](PLAN.md). Phase 1 is an ASP.NET Core 10 API backed by PostgreSQL. It creates seven-character Base62 links, redirects with HTTP 302, exposes public link metadata, and lets the creator delete a link with a management token.

## Requirements

- .NET SDK 10
- Docker Desktop, for local PostgreSQL and integration tests

## Run locally

These PowerShell commands run PostgreSQL without adding the Phase 2 Docker Compose deployment. Pick your own password for local use. The container is named `linkforge-postgres`, so remove or rename an existing container with that name before repeating the setup.

```powershell
docker run --name linkforge-postgres -e POSTGRES_DB=linkforge -e POSTGRES_USER=linkforge -e POSTGRES_PASSWORD=localpass -p 5432:5432 -d postgres:18-alpine
$env:ConnectionStrings__Default = 'Host=localhost;Port=5432;Database=linkforge;Username=linkforge;Password=localpass'
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/UrlShortener.Infrastructure --startup-project src/UrlShortener.Api
dotnet run --project src/UrlShortener.Api --no-launch-profile -- --urls http://localhost:5000
```

The API starts at `http://localhost:5000`. In Development, Swagger UI is at `http://localhost:5000/swagger`. Override `ShortUrls__BaseUrl` when the public short-link origin differs from `http://localhost:5000`. Run database migrations before starting the API; startup does not mutate the schema.

## API

### Create

```http
POST /api/v1/urls
Content-Type: application/json

{"url":"https://example.com/articles/123"}
```

`201 Created` returns `shortCode`, `shortUrl`, `managementToken`, and `createdAt`. Save the token securely: it is shown only once and cannot be recovered. Each POST creates a new short code, even for the same long URL. A retry after a timeout may create another link; Phase 1 does not accept an idempotency key.

### Redirect and inspect

```http
GET /{shortCode}              -> 302 Location: <original URL>
GET /api/v1/urls/{shortCode}  -> 200 public metadata
```

Unknown codes return `404`. Metadata contains the original URL and creation time, but never the management token. Generated codes are case-sensitive.

### Delete

```http
DELETE /api/v1/urls/{shortCode}
X-Management-Token: <token returned at creation>
```

The correct token returns `204`; a missing or wrong token returns `403`. A deleted code returns `404`. The database stores only the token's SHA-256 hash. Anonymous link creation is supported in this learning phase; accounts and API keys are not yet active.

### Validation

The create endpoint accepts absolute HTTP(S) URLs up to 2,048 characters with a DNS host. It rejects local hosts, raw IP addresses, credentials, whitespace at either end, and control characters. The service does not fetch the destination URL. Invalid input returns `400` with a validation problem response.

## Build and test

```powershell
dotnet build LinkForge.slnx
dotnet test tests/UrlShortener.UnitTests/UrlShortener.UnitTests.csproj
dotnet test tests/UrlShortener.IntegrationTests/UrlShortener.IntegrationTests.csproj
```

Integration tests start a temporary PostgreSQL 18 container, apply the EF Core migration, exercise HTTP endpoints, and remove the container. Docker Desktop must be running.

## Structure

| Path | Responsibility |
|---|---|
| `src/UrlShortener.Api` | HTTP routes, validation responses, Swagger, logging |
| `src/UrlShortener.Application` | URL use cases and repository contract |
| `src/UrlShortener.Domain` | URL data and Base62 codec |
| `src/UrlShortener.Infrastructure` | EF Core model, migration, PostgreSQL repository |
| `tests/` | Unit and PostgreSQL integration tests |
| `docs/` | Requirements, capacity model, architecture |

PostgreSQL is the source of truth. Redis, NGINX, Kafka and custom aliases belong to later phases in [PLAN.md](PLAN.md).
