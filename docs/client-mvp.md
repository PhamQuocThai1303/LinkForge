# Client MVP

## Objective

Provide a small browser client for the existing URL shortener. A user pastes an HTTP(S) URL, creates a short URL, and copies it. The one-time management token is shown with a separate copy action so the user can save it.

## Contract and scope

- Serve the client at `/app/` from the ASP.NET Core API on the same origin. Keep the existing `/` service-status response and API routes.
- Submit `{ "url": "..." }` to `POST /api/v1/urls` and consume `shortUrl`, `shortCode`, and `managementToken` from a `201` response.
- Validate the URL in the browser for helpful feedback; the API remains the authority. Show validation and connection errors without clearing the entered URL.
- Keep the result and token in page memory only. They disappear on reload. Do not send the token to logging or analytics.
- Show loading state, a copy confirmation, and a link to open the short URL. Use text APIs for untrusted values, never HTML injection.
- No separate frontend server, package manager, CORS configuration, account flow, or custom alias in this MVP.

## Design system

Single responsive screen in Vietnamese. Warm neutral page, dark ink text, green action color, generous spacing, and one focused form with a result panel. Use system fonts, native CSS shapes, visible keyboard focus, semantic labels, and a live status region. Desktop uses two columns; narrow screens stack them.

## Code style

Keep browser code as small, direct functions. Use native DOM APIs and explicit state text. For example:

```js
errorMessage.textContent = message;
errorMessage.hidden = !message;
```

## Commands and structure

- Build: `dotnet build LinkForge.slnx`
- Test: `dotnet test LinkForge.slnx`
- Run: `docker compose up --build -d`
- Browser files: `src/UrlShortener.Api/wwwroot/app/`
- API wiring: `src/UrlShortener.Api/Program.cs`
- HTTP integration coverage: `tests/UrlShortener.IntegrationTests/UrlApiTests.cs`

## Testing strategy

The integration test checks the HTML and asset routes. Browser QA checks empty/invalid input, server validation, create, redirect, copy actions, reload behavior, desktop and mobile layout, and console errors.

## Boundaries

- Always: call the API on the same origin, validate for user feedback, and render untrusted values as text.
- Ask first: changes to the backend create contract or database schema.
- Never: store or log the management token in the client.

## Acceptance criteria

1. `GET /app/` serves the client in local .NET and Compose runs; `/` and existing API routes still work.
2. Creating a valid URL displays the returned short URL and management token; both can be copied.
3. Invalid input and API failures show actionable feedback; the form can be retried.
4. The layout remains usable on desktop and mobile, including keyboard navigation and screen-reader status text.

## Open questions

None blocking. The existing API determines URL validation and link generation.
