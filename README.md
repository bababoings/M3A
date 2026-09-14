# M3A — REST API Contract


## Overview
- **Service**: M3A Ticketing REST API
- **Language/Framework**: C# .NET 10 with Minimal APIs
- **Base URL**: `http://localhost:8080` (from `src/M3A.Api/Properties/launchSettings.json`)
- **Content-Type**: `application/json`
- **ID Format**: Regex `[A-Za-z0-9\-]+` — defined once in `Domain.ValueObjects.ResourceId.Pattern`
- **Persistence**: EF Core 10 + Npgsql (PostgreSQL)
- **Resources**: `Venues`, `Events`, `Tickets`, plus `Items` (the reference resource that
  demonstrates the pattern; it carries no business meaning)

Domain in one line: a **Venue** has a capacity; an **Event** is scheduled at a venue and cannot
allocate more capacity than the venue holds; a **Ticket** is issued for an event and moves
`Issued → Purchased → Redeemed`.

---

## Architecture

### Layered Architecture
```
┌─────────────────────────────────────────────────────────────┐
│                     API Layer (Minimal APIs)                │
│  M3A.Api                                                    │
│  • Routes/      — one route module per resource             │
│  • Dtos/        — wire contracts (records)                  │
│  • Validators/  — FluentValidation per request DTO          │
│  • Extensions/  — DI composition, pipeline, entity→DTO map  │
│  • Middleware/  — GlobalExceptionHandler → ProblemDetails   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                       Delegate Layer                        │
│  M3A.Delegates                                              │
│  • I{Resource}Delegate — one per domain                     │
│  • Business rules: capacity limits, date checks,            │
│    ticket status transitions                                │
│  • Raises EntityNotFound / BusinessRuleViolation            │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      Repository Layer                       │
│  M3A.Repositories                                           │
│  • IRepository<TEntity> + one interface per aggregate root  │
│  • EF Core data access, eager loading where needed          │
│  • Persistence/ — M3ADbContext, configurations, migrations  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                        Domain Models                        │
│  M3A.Domain                                                 │
│  • Entities/     — Venue, Event, Ticket, Item, IEntity      │
│  • Enums/        — TicketStatus                             │
│  • ValueObjects/ — ResourceId                               │
│  • Exceptions/   — DomainException hierarchy                │
└─────────────────────────────────────────────────────────────┘
```

Project references point one way only: `Api → Delegates → Repositories → Domain`.

### Key Patterns
| Pattern | Implementation |
|---------|----------------|
| **Minimal APIs** | One route module per resource (`Routes/{Resource}Routes.cs`), each a `MapGroup` with `WithTags`, mounted through `RouteRegistration.MapM3ARoutes`; `Program.cs` stays a handful of lines |
| **Delegate** | `I{Resource}Delegate` per domain — orchestrates repositories, owns the business rules, returns domain entities |
| **Repository** | `IRepository<TEntity>` shared contract (`GetAll`/`GetById`/`Exists`/`Add`/`Update`/`Delete`), one interface per aggregate root; swappable, which is what makes delegates unit-testable |
| **Dependency Injection** | Built-in container, everything scoped; composed in `AddM3AApi` → `AddApiServices` + `AddDelegates` + `AddRepositories` |
| **Validation** | FluentValidation DTO validators run by the `ValidationFilter<TRequest>` endpoint filter via `.WithValidation<T>()`; rules that must hold for every caller are re-asserted in the delegate |
| **Serialization** | `System.Text.Json`, camelCase properties and dictionary keys, enums as strings (`AddApiServices`) |
| **Error handling** | `GlobalExceptionHandler` is the single place an exception type becomes a status code |
| **Configuration** | `appsettings.json` holds the *shape* of the connection string; `${VAR}` placeholders are expanded from the environment by `ConnectionStringTemplate`, sourced from a gitignored `.env` via `DotEnvFile` |
| **Package versions** | Central, in `Directory.Packages.props`; csproj files name packages only |
| **Compiler** | `net10.0`, nullable enabled, `TreatWarningsAsErrors`, XML docs generated |

---

## Endpoints

### Venues

| Method | Path | Request Body | Response | Description |
|--------|------|--------------|----------|-------------|
| `GET` | `/venues` | - | `200` `VenueDto[]` | List all venues |
| `GET` | `/venues/{venueId}` | - | `200` `VenueDto` \| `400` \| `404` | Get venue by ID |
| `POST` | `/venues` | `CreateVenueDto` | `201` + `Location` \| `400` \| `422` | Create venue |
| `PUT` | `/venues/{venueId}` | `UpdateVenueDto` | `200` `VenueDto` \| `400` \| `404` \| `422` | Full update |
| `DELETE` | `/venues/{venueId}` | - | `204` \| `400` \| `404` | Delete venue |

```json
// VenueDto
{ "id": "string", "location": "string", "capacity": 0 }

// CreateVenueDto / UpdateVenueDto
{ "location": "string", "capacity": 0 }
```

Rules: `location` required, ≤256 chars; `capacity` > 0 (enforced in the validator *and* in
`VenueDelegate`, so the rule holds for any caller of the delegate layer).

### Events

| Method | Path | Request Body | Response | Description |
|--------|------|--------------|----------|-------------|
| `GET` | `/events` | - | `200` `EventDto[]` | List all events (venue eager-loaded) |
| `GET` | `/events/{eventId}` | - | `200` `EventDto` \| `400` \| `404` | Get event by ID |
| `POST` | `/events` | `CreateEventDto` | `201` + `Location` \| `400` \| `404` \| `422` | Create event |
| `PUT` | `/events/{eventId}` | `UpdateEventDto` | `200` `EventDto` \| `400` \| `404` \| `422` | Full update |
| `DELETE` | `/events/{eventId}` | - | `204` \| `400` \| `404` \| `422` | Delete event |

```json
// EventDto
{
  "id": "string",
  "name": "string",
  "dateTime": "2026-12-31T20:00:00+00:00",
  "eventCapacity": 0,
  "venueId": "string",
  "venue": { "id": "string", "location": "string", "capacity": 0 }
}

// CreateEventDto / UpdateEventDto
{ "name": "string", "dateTime": "2026-12-31T20:00:00+00:00", "eventCapacity": 0, "venueId": "string" }
```

Rules:
- `name` required, ≤256 chars; `eventCapacity` > 0; `venueId` must match the resource-ID format.
- `dateTime` must be in the future — **on create only** (see [known gaps](#status--known-gaps)).
- The referenced venue must exist → otherwise `404`.
- `eventCapacity` may not exceed the venue's `capacity` → otherwise `422`.
- An event with tickets issued for it cannot be deleted → `422`. The `tickets → events`
  foreign key is `Restrict`, so this is refused in the delegate before the database rejects it.

### Tickets

| Method | Path | Request Body | Response | Description |
|--------|------|--------------|----------|-------------|
| `GET` | `/tickets` | - | `200` `TicketDto[]` | List all tickets |
| `GET` | `/tickets/{ticketId}` | - | `200` `TicketDto` \| `400` \| `404` | Get ticket by ID |
| `POST` | `/tickets` | `CreateTicketDto` | `201` + `Location` \| `400` \| `404` | Issue a ticket |
| `DELETE` | `/tickets/{ticketId}` | - | `204` \| `400` \| `404` | Delete ticket |
| `POST` | `/tickets/{ticketId}/purchase` | - | `200` `TicketDto` \| `400` \| `404` \| `422` | `Issued → Purchased` |
| `POST` | `/tickets/{ticketId}/redeem` | - | `200` `TicketDto` \| `400` \| `404` \| `422` | `Purchased → Redeemed` |

```json
// TicketDto — status is serialized as a string
{ "id": "string", "eventId": "string", "status": "Issued" }

// CreateTicketDto
{ "eventId": "string" }
```

Rules: the referenced event must exist → otherwise `404`, enforced in `TicketDelegate` and
backed by a foreign key in the database. A ticket is created `Issued`. `purchase` and `redeem`
are the only state changes and each rejects a ticket not in the expected source state with `422`.
There is no `PUT /tickets/{id}` — status is changed through the two action endpoints, not by
replacing the resource.

### Items (reference resource)

| Method | Path | Request Body | Response | Description |
|--------|------|--------------|----------|-------------|
| `GET` | `/items` | - | `200` `ItemDto[]` | List all items |
| `GET` | `/items/{itemId}` | - | `200` `ItemDto` \| `400` \| `404` | Get item by ID |
| `POST` | `/items` | `CreateItemDto` | `201` + `Location` \| `400` | Create item |
| `PUT` | `/items/{itemId}` | `UpdateItemDto` | `200` `ItemDto` \| `400` \| `404` | Full update |
| `DELETE` | `/items/{itemId}` | - | `204` \| `400` \| `404` | Delete item |

```json
// ItemDto                          // CreateItemDto / UpdateItemDto
{ "id": "string", "name": "string" }  { "name": "string" }
```

### Conventions shared by every resource
- Identifiers are **server-assigned** (`ResourceId.New()`), never supplied by the client.
- `POST` returns the created DTO *and* a `Location: /{resource}/{id}` header.
- Path IDs are format-checked before any lookup, so a malformed ID is `400`, not `404`.
- `DELETE` is not idempotent by design: deleting twice gives `204` then `404`.
- Every route carries `WithName`, `WithSummary` and `Produces*` metadata; OpenAPI is served at
  `/openapi/v1.json` in the Development environment.

---

## Error Responses

| Code | Scenario | Produced by |
|------|----------|-------------|
| `400` | Invalid ID format, malformed JSON, FluentValidation failure | `ResultExtensions.InvalidIdProblem`, `ValidationFilter<T>`, `GlobalExceptionHandler` |
| `404` | Resource not found, including a referenced venue or event that does not exist | `ResultExtensions.NotFoundProblem`, `EntityNotFoundException` |
| `422` | Business rule violation: capacity ≤ 0, event capacity over venue capacity, past event date, illegal ticket transition, deleting an event that has tickets | `BusinessRuleViolationException` |
| `500` | Unhandled exception — logged in full, no detail leaked to the client | `GlobalExceptionHandler` fallback |

All four are RFC 7807 ProblemDetails. Titles are constants in `Extensions/ProblemTitles.cs`, and
`instance` is set to the request path by `AddProblemDetails`.

**Error Response Format**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Failed",
  "status": 400,
  "instance": "/events",
  "errors": {
    "EventCapacity": ["'Event Capacity' must be greater than '0'."]
  }
}
```

Note that a rule checked by both a validator and a delegate surfaces as `400` when the request
reaches the validator, and as `422` when the delegate is called some other way.

---

## Configuration

`src/M3A.Api/appsettings.json` is committed and contains **no credentials** — only the shape of
the connection:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USERNAME};Password=${DB_PASSWORD}"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

How the placeholders are filled, in order:

1. `Program.cs` calls `DotEnvFile.Load(...)` before the host is built. It walks up from the
   working directory (and from `AppContext.BaseDirectory`) looking for a `.env`, parses
   `KEY=VALUE` lines — honouring `#` comments, an `export ` prefix and surrounding quotes — and
   sets each variable **only if it is not already set**. Real environment variables therefore
   always win, which is what lets production keep its own secret store with this code untouched.
2. `ConnectionStringTemplate.Expand` substitutes every `${VAR}`. If any resolve to nothing it
   throws at startup naming all the missing variables, rather than failing later as an opaque
   connection error.

`.env` is gitignored. For local development, create one at the repository root:

```bash
DB_HOST=localhost
DB_PORT=5432
DB_NAME=m3a_db
DB_USERNAME=m3a_svc
DB_PASSWORD=<your password>
```

`appsettings.Development.json` only raises log levels. `appsettings.Local.json` is gitignored for
further machine-specific overrides.

---

## Testing

**Current state: 117 tests, all passing** (12 domain, 39 delegate, 16 repository, 50 API),
plus a manual end-to-end run against real PostgreSQL (see [Verified end to end](#verified-end-to-end)).

### Test Strategy Overview
| Layer | Test Type | Framework | Coverage today |
|-------|-----------|-----------|----------------|
| API Routes | Integration | xUnit + `WebApplicationFactory` + EF Core InMemory | Every `/events`, `/venues`, `/items` endpoint; `/tickets` create and transitions |
| Delegates | Unit | xUnit + Moq | `EventDelegate`, `VenueDelegate`, `ItemDelegate`, `TicketDelegate` |
| Repositories | Unit | xUnit + EF Core InMemory | `EventRepository`, `VenueRepository`, `ItemRepository`, `ConnectionStringTemplate` |
| Domain | Unit | xUnit | `ResourceId`, entity builders |


#### Route Tests
**Events** (`Routes/EventRoutesTests`)
| Test | Assertion |
|------|-----------|
| `GetAll_Returns200` | Listing succeeds and deserializes as `EventDto[]` |
| `GetById_Returns400_WhenIdFormatIsInvalid` | Malformed ID rejected before any lookup |
| `GetById_Returns404_WhenNotFound` | Well-formed ID with no row |
| `Post_Returns201WithLocation_AndTheEventIsRetrievableWithVenue` | `Location` resolves, and the nested `venue` is populated |
| `Post_Returns400_WhenNameIsEmpty` | Validator rejects empty name |
| `Post_Returns400_WhenCapacityIsNotPositive` | Validator rejects capacity ≤ 0 |
| `Post_Returns400_WhenDateTimeIsInThePast` | Validator rejects a past date |
| `Post_Returns404_WhenVenueDoesNotExist` | Referenced venue must exist |
| `Post_Returns422_WhenEventCapacityExceedsVenueCapacity` | The cross-entity rule |
| `Put_Returns200_WhenEventExists` / `Put_Returns404_WhenEventDoesNotExist` | Replace semantics |
| `Delete_Returns204_ThenSubsequentDeleteReturns404` | Delete is deliberately not idempotent |

**Venues** (`Routes/VenueRoutesTests`) — the same nine shapes, with
`Post_Returns400_WhenLocationIsEmpty` and `Post_Returns400_WhenCapacityIsNotPositive`.
**Items** (`Routes/ItemRoutesTests`) — the same eight shapes for the reference resource.

#### Cross-Cutting Tests (`Middleware/ValidationMiddlewareTests`)
| Test | Assertion |
|------|-----------|
| `ValidationFailure_ReturnsProblemDetailsWithErrors` | FluentValidation errors shaped as ProblemDetails `errors` |
| `MalformedJson_Returns400` | A bad body never reaches a handler |
| `SuccessfulResponse_IsJson` | `application/json` on the success path |
| `InvalidIdFormat_Returns400` | Theory over `has%20space`, `under_score` |

Auth tests (`401`/`403`) are listed in the PRD as future work — no auth middleware exists yet.

#### Delegate Tests (Unit, repositories mocked)
**`EventDelegateTests`** — `GetAll`/`GetById` pass-through and not-found; `CreateAsync` assigns an
ID and persists; `CreateAsync` throws on non-positive capacity (theory: `0`, `-10`), on a past
date, on an unknown venue, and on capacity over the venue limit; `UpdateAsync` throws for an
unknown event, an unknown venue and an over-limit capacity, and replaces fields when valid;
`DeleteAsync` throws when not found and succeeds when found.

**`VenueDelegateTests`** — `GetById` not-found; `CreateAsync` assigns an ID and persists, and
throws on non-positive capacity (theory: `0`, `-1`); `UpdateAsync` not-found, field replacement
and capacity rule; `DeleteAsync` not-found.

**`ItemDelegateTests`** — the minimal template: `GetById` not-found, `CreateAsync` persists,
`UpdateAsync` found/not-found, `DeleteAsync` not-found.

**`TicketDelegateTests`** — `CreateAsync` throws `EntityNotFoundException` for an unknown event
and never reaches the ticket repository; `CreateAsync` assigns a valid ID and persists the ticket
as `Issued` when the event exists.

The status transitions are covered as a matrix. `purchase` moves `Issued → Purchased` and `redeem`
moves `Purchased → Redeemed`, each persisting exactly once; every other source status is refused
with `BusinessRuleViolationException` — theories over `Purchased`/`Redeemed` for purchase and
`Issued`/`Redeemed` for redeem — and a refused transition must neither persist nor mutate the
entity in memory. Both actions throw `EntityNotFoundException` for an unknown ticket.

**`TicketRoutesTests`** — the same paths over HTTP: `201` + `Location` and `404` on create;
`200` for each legal transition, with the new status confirmed by a follow-up `GET`; `422` for
purchasing an already-purchased ticket, redeeming before purchase, and redeeming twice; and
theories over both actions for `404` (unknown ticket) and `400` (malformed id).

#### Repository & Configuration Tests
`EventRepositoryTests` covers the empty listing, an add/get round-trip that also asserts the venue
is eager-loaded, `ExistsAsync`, and both delete paths; `VenueRepositoryTests` and
`ItemRepositoryTests` mirror it. `ConnectionStringTemplateTests` covers substitution, strings with
no placeholders, an empty variable being treated as missing, and the error naming *every* missing
variable. `DotEnvFileTests` (in the API suite) covers no-file, comments and blank lines, quote
stripping and the `export` prefix, not overriding an already-set variable, and finding the file by
walking up from a nested directory.

### Test Data Builders
`tests/Shared/Builders/` holds fluent builders (`EventBuilder`, `VenueBuilder`, `TicketBuilder`,
`ItemBuilder`)
linked into every test project via a `<Compile Include="..\Shared\Builders\*.cs" />` item, so a
test states only the fields it cares about:

```csharp
var venue = new VenueBuilder().WithCapacity(100).Build();
var ticket = new TicketBuilder().WithStatus(TicketStatus.Purchased).Build();
```

### Test Execution Requirements
| Requirement | How it is met |
|-------------|---------------|
| **Isolation** | `RepositoryTestBase` gives each test its own in-memory database; `M3AApiFactory` its own per-factory database, both GUID-named |
| **Parallelization** | No shared state and no fixed database names |
| **Determinism** | Builders use fixed IDs; no wall-clock assertions |
| **Coverage** | `dotnet test --collect:"XPlat Code Coverage"` (coverlet referenced by every test project) |
| **CI Pipeline** | Not configured|


---

## Project Structure
```
M3A/
├── src/
│   ├── M3A.Api/
│   │   ├── Program.cs                        # .env load → AddM3AApi → UseM3APipeline
│   │   ├── Routes/                           # EventRoutes, VenueRoutes, TicketRoutes,
│   │   │                                     #   ItemRoutes, RouteRegistration
│   │   ├── Dtos/                             # EventDtos, VenueDtos, TicketDtos, ItemDtos
│   │   ├── Validators/                       # Create/Update validator per write DTO
│   │   ├── Middleware/GlobalExceptionHandler.cs
│   │   ├── Extensions/
│   │   │   ├── ServiceCollectionExtensions.cs  # composition root
│   │   │   ├── WebApplicationExtensions.cs     # HTTP pipeline
│   │   │   ├── ValidationFilter.cs
│   │   │   ├── ResultExtensions.cs             # shared 400/404 shapes
│   │   │   ├── ProblemTitles.cs
│   │   │   ├── DotEnvFile.cs                   # .env loader
│   │   │   └── {Event,Venue,Ticket,Item}Mappings.cs
│   │   └── appsettings.json
│   ├── M3A.Delegates/
│   │   ├── I{Event,Venue,Ticket,Item}Delegate.cs + implementations
│   │   └── Extensions/ServiceCollectionExtensions.cs
│   ├── M3A.Repositories/
│   │   ├── IRepository.cs                    # shared CRUD contract
│   │   ├── I{Event,Venue,Ticket,Item}Repository.cs + implementations
│   │   ├── Persistence/
│   │   │   ├── M3ADbContext.cs
│   │   │   ├── ConnectionStringTemplate.cs    # ${VAR} expansion
│   │   │   └── Configurations/                # one IEntityTypeConfiguration per entity
│   │   ├── Migrations/
│   │   └── Extensions/ServiceCollectionExtensions.cs
│   └── M3A.Domain/
│       ├── Entities/ (IEntity, Venue, Event, Ticket, Item)
│       ├── Enums/TicketStatus.cs
│       ├── ValueObjects/ResourceId.cs
│       └── Exceptions/ (DomainException, EntityNotFound, BusinessRuleViolation)
├── tests/
│   ├── M3A.Api.Tests/
│   │   ├── Routes/ (Event, Venue, Item, Ticket)
│   │   ├── Middleware/ValidationMiddlewareTests.cs
│   │   ├── Extensions/DotEnvFileTests.cs
│   │   ├── M3AApiFactory.cs                  # swaps Npgsql → InMemory
│   │   └── M3AApiTests.cs                    # base class: client + JSON options
│   ├── M3A.Delegates.Tests/                  # Event, Venue, Item, Ticket
│   ├── M3A.Repositories.Tests/               # Event, Venue, Item, ConnectionStringTemplate,
│   │                                         #   RepositoryTestBase
│   ├── M3A.Domain.Tests/                     # Entities/, ValueObjects/
│   └── Shared/Builders/                      # linked into each test project
├── docs/Projecto_Final_7C.md                 # the PRD this README tracks
├── Directory.Build.props                     # shared compiler settings
├── Directory.Packages.props                  # central package versions
├── global.json                               # SDK 10.0.101
└── M3A.sln
```

---

## Commands

```bash
dotnet build                                   # whole solution, warnings are errors
dotnet test                                    # all four suites (117 tests)
dotnet test --collect:"XPlat Code Coverage"    # with coverage
dotnet run --project src/M3A.Api               # http://localhost:8080
```

`dotnet run` needs a PostgreSQL instance and a populated `.env`; the test suite needs neither
(`M3AApiFactory` swaps in EF Core InMemory).

Migrations:

```bash
dotnet ef migrations add <Name> --project src/M3A.Repositories --startup-project src/M3A.Api
dotnet ef database update      --project src/M3A.Repositories --startup-project src/M3A.Api
```

---

## Adding a resource

1. `src/M3A.Domain/Entities/{Resource}.cs` — implement `IEntity`.
2. `src/M3A.Repositories/` — `I{Resource}Repository.cs` (extend `IRepository<T>`),
   `{Resource}Repository.cs`, `Persistence/Configurations/{Resource}Configuration.cs`, a `DbSet`
   on `M3ADbContext`, and a line in `AddRepositoryImplementations`. Add a migration.
3. `src/M3A.Delegates/` — `I{Resource}Delegate.cs`, `{Resource}Delegate.cs`, and a line in
   `AddDelegates`.
4. `src/M3A.Api/` — `Dtos/{Resource}Dtos.cs`, `Validators/`, `Extensions/{Resource}Mappings.cs`,
   `Routes/{Resource}Routes.cs`, then one line in `RouteRegistration.MapM3ARoutes`.
5. Mirror the four test suites and add a builder under `tests/Shared/Builders/`.

---

## Verified end to end

Run against real PostgreSQL (not the in-memory test provider), `dotnet run --project src/M3A.Api`
with both migrations applied:

| Check | Result |
|---|---|
| `POST /venues`, `POST /events` | `201`, event returns its nested `venue` |
| `POST /tickets` for an existing event | `201` + `Location`, status `Issued` |
| `POST /tickets` for an unknown event | `404` `Resource Not Found` |
| `purchase` → `redeem` | `200`, `Issued` → `Purchased` → `Redeemed` |
| `purchase`/`redeem` out of order | `422` naming the actual and expected status |
| `DELETE /events/{id}` with no tickets | `204` |
| `DELETE /events/{id}` with tickets | `422` (the FK is enforced by PostgreSQL as `23503`) |
| Event capacity over venue capacity | `422` |
| Event on an unknown venue | `404` |
| Event with a past date | `400` |
| Malformed resource id | `400` |
| `GET /openapi/v1.json` | `200`, all 10 paths documented |

No unhandled exceptions were logged during the run.
