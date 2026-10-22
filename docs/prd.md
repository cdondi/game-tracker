# PRD: Game Tracker API

## Problem

I own games across several platforms and have no single place to track which ones I haven't started, which I'm playing, and which I've finished, or what I thought of them. Spreadsheets and notes drift and allow messy, duplicate data.

## Users

- **One user (the owner).** No accounts and no authentication in v1. Story 10 adds optional protection.
- This is a demo app. It favors clarity and correctness over breadth.

## Goals

- A CRUD Web API in ASP.NET Core for logging owned games, with platform, status and an optional rating.
- Strict, predictable validation. Errors are returned as ProblemDetails (RFC 7807).
- Persistence in EF Core + SQLite. Sample data is seeded on first run so the demo has something to show.
- Swagger UI in Development for manual exploration.
- Every acceptance criterion below is covered by automated tests, and `dotnet test` passes.

## Non-goals

- Multiple users, accounts, or authentication in v1 (Story 10 is optional and comes last).
- Pagination, a client-chosen sort order, and PATCH.
- Notes, play dates, hours played, genre, cover art.
- Integration with external game databases (IGDB, Steam, etc.).
- Any frontend.

## Data model

**Game**

| Field       | Type              | Rules |
|-------------|-------------------|-------|
| `id`        | int               | Assigned by the server. Read-only. |
| `title`     | string            | Required. Trimmed. 1–200 characters after trimming. |
| `platform`  | `Platform` enum   | Required. |
| `status`    | `GameStatus` enum | Required. |
| `rating`    | decimal?          | Optional (null = unrated). 1.0–5.0 in 0.5 steps. Allowed only when the status is `Playing` or `Completed`. |
| `createdAt` | DateTimeOffset    | Set by the server on create. Read-only. |
| `updatedAt` | DateTimeOffset    | Set by the server on create and on update. Read-only. |

**`Platform`:** `PC`, `PS5`, `PS4`, `XboxSeries`, `XboxOne`, `Switch`, `Switch2`, `Mobile`, `Other`

**`GameStatus`:** `Backlog`, `Playing`, `Completed`

**Serialization:** Enums are sent and received as strings. Input matching ignores case (`"ps5"` is accepted). Output always uses the canonical form (`"PS5"`).

**Uniqueness:** `(title, platform)` is unique, and the title comparison ignores case. This is enforced by a **case-insensitive unique index in the database**: `Title` uses `COLLATE NOCASE` with a unique index on `(Title, Platform)`. The API also returns a friendly 409 for it. The index is the source of truth, so a duplicate that slips past an application-level check is still rejected and mapped to a 409.

**Known limitation (accepted):** SQLite's `NOCASE` collation folds case only for ASCII letters. Titles that differ only in the case of non-ASCII letters (such as `"Pokémon"` and `"POKÉMON"`) are treated as different games. The same applies to the case-insensitive title sort in Story 3. This is acceptable for a demo.

## User stories

Each story is a vertical slice, built and tested end to end (endpoint → validation → persistence → tests).

### Story 1: Add a game
As the owner, I want to add a game I own, so that it's in my collection.

**Acceptance criteria**
- `POST /games` with a valid `title`, `platform` and `status` returns **201** with the created game and a `Location: /games/{id}` header.
- The response includes `id`, `createdAt` and `updatedAt`, and `rating` is `null` when it was omitted.
- `title` is stored trimmed. A missing, empty, whitespace-only, or over-200-character title returns **400**.
- A missing `platform` or `status` returns **400**.
- An unknown enum value (such as `"platform": "Dreamcast"`) returns **400**, and the error lists the valid values.
- Enum input ignores case. Output uses the canonical casing.
- Client-supplied `id`, `createdAt` and `updatedAt` are ignored.
- All 400 responses are ProblemDetails with errors listed per field.

### Story 2: View a single game
As the owner, I want to fetch one game by id, so that I can see its details.

**Acceptance criteria**
- `GET /games/{id}` for an existing id returns **200** with the game.
- An unknown id returns **404** as ProblemDetails.

### Story 3: List my games
As the owner, I want to list all my games, so that I can see my whole collection.

**Acceptance criteria**
- `GET /games` returns **200** with a JSON array of all games.
- The results are sorted by `title` A–Z, ignoring case.
- The response is a bare JSON array, with no wrapper object.
- There is no pagination. Every game is returned.

### Story 4: Filter my games
As the owner, I want to filter by status and/or platform, so that I can see, for example, my Switch backlog.

**Acceptance criteria**
- `GET /games?status=Backlog` returns only backlog games. `?platform=PS5` returns only PS5 games.
- When both filters are given, they are combined with AND.
- Filter values ignore case.
- An invalid filter value (such as `?status=Abandoned`) returns **400**, not an empty list.
- Filtered results keep the title A–Z sort.

### Story 5: Update a game
As the owner, I want to edit a game, so that I can change its status as I play it or fix mistakes.

**Acceptance criteria**
- `PUT /games/{id}` with a full valid body returns **200** with the updated game.
- PUT replaces the whole resource. An omitted optional field (`rating`) becomes `null`.
- `updatedAt` changes and `createdAt` does not.
- Title, platform, status and enum validation are the same as in Story 1.
- An unknown id returns **404**. PUT never creates a game.

### Story 6: Delete a game
As the owner, I want to remove a game, so that my list reflects what I actually own.

**Acceptance criteria**
- `DELETE /games/{id}` returns **204**, and a later `GET /games/{id}` returns **404**.
- An unknown id returns **404**.

### Story 7: Rate a game
As the owner, I want to rate games I've played, so that I remember what I thought of them.

**Acceptance criteria**
- `rating` is accepted on POST and PUT when the status is `Playing` or `Completed`.
- Valid values are exactly 1.0, 1.5, 2.0, … 5.0.
- These values return **400**: 0.5, 0, negative numbers, values above 5.0, and values that aren't a 0.5 step (such as 3.2).
- A non-null `rating` with the status `Backlog` returns **400** on both POST and PUT.
- Changing a rated game to `Backlog` succeeds only if the same PUT sets `rating` to `null`. Otherwise it returns **400**. The API never clears a rating without being told to.
- `rating: null` is always valid.

### Story 8: Prevent duplicate entries
As the owner, I want the API to reject a game I've already logged on the same platform, so that my collection stays clean.

**Acceptance criteria**
- POST with a title + platform that already exists returns **409** as ProblemDetails. The title comparison ignores case and trims whitespace (`" elden ring "` matches `"Elden Ring"`).
- A PUT that changes a game's title or platform so that it collides with *another* game returns **409**.
- A PUT that keeps a game's own title and platform unchanged, or only changes their casing, does **not** return 409.
- The same title on a different platform is allowed.
- The database has a case-insensitive unique index on `(Title, Platform)`. A test confirms that an insert which bypasses the application check (such as a direct `DbContext` insert) fails. A unique-constraint violation from the database is mapped to **409**, never to 500.

### Story 9: Seed sample data
As the owner demoing the app, I want sample games to exist on first run, so that the API has data to show right away.

**Acceptance criteria**
- On startup, if the `Games` table is empty, exactly 5 games are inserted. Any valid set of 5 is acceptable. They cover several platforms, all three statuses, and both rated and unrated games.
- The seed data obeys every validation rule, including the rating rules and uniqueness.
- Seeding doesn't run again if the table already has rows, so restarts don't create duplicates.
- **Tests must not assume an empty table.** The tests:
  - create the data they need, using unique titles (for example, with a GUID suffix);
  - assert that their own records are present or absent, not on total counts or exact list contents;
  - check sort order and filters by the relative order and membership of records they created, not by an exact match on the full list.

### Story 10 (optional, last): Protect the endpoints with authentication
As the owner, I want the API to require credentials, so that nobody else on the network can change my collection.

**Acceptance criteria**
- Authentication uses a single API key sent in the `X-Api-Key` header.
- When auth is enabled, every `/games` endpoint returns **401** if the header is missing or the key is wrong.
- With a valid key, every earlier acceptance criterion still holds.
- The key is stored in .NET user secrets for local development (read through configuration). It is never hard-coded or committed.
- Swagger UI can send the `X-Api-Key` header.
- Auth can be turned off by configuration, so that the v1 no-auth behavior and its tests still work.

## Cross-cutting acceptance criteria

- Swagger/OpenAPI UI is available in the Development environment.
- Integration tests use xUnit and `WebApplicationFactory` against in-memory SQLite, with the same unique index and seeding as production.
- `dotnet test` passes with no failures.
