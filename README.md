# M3A — REST API skeleton

Architecture skeleton for the M3A service, built to the contract in
[`docs/Projecto_Final_7C.md`](docs/Projecto_Final_7C.md). **No business domain is
implemented yet** — the `Item` resource is a working reference implementation that
shows the pattern every real resource will follow, end to end.

- **Framework**: .NET 10, ASP.NET Core Minimal APIs
- **Base URL**: `http://localhost:8080` (see `src/M3A.Api/Properties/launchSettings.json`)
- **Persistence**: EF Core 10 + Npgsql
- **Validation**: FluentValidation
- **Tests**: xUnit, Moq, `WebApplicationFactory`, EF Core InMemory

## Layout

```
M3A/
├── src/
│   ├── M3A.Api/                     # Minimal API host — the only layer that knows HTTP
│   │   ├── Program.cs
│   │   ├── Routes/                   # One route module per resource + RouteRegistration
│   │   ├── Dtos/                     # Wire contracts
│   │   ├── Validators/               # FluentValidation rules per request DTO
│   │   ├── Middleware/               # GlobalExceptionHandler → ProblemDetails
│   │   └── Extensions/               # DI composition, pipeline, mapping, validation filter
│   ├── M3A.Delegates/               # Business logic orchestration, one delegate per domain
│   ├── M3A.Repositories/            # Data access, one repository per aggregate root
│   │   └── Persistence/              # DbContext + entity configurations
│   └── M3A.Domain/                  # Entities, value objects, enums, domain exceptions
├── tests/
│   ├── M3A.Api.Tests/               # Integration — Routes/ and Middleware/
│   ├── M3A.Delegates.Tests/         # Unit, repositories mocked
│   ├── M3A.Repositories.Tests/      # Unit against EF Core InMemory
│   ├── M3A.Domain.Tests/            # Unit
│   └── Shared/Builders/              # Fluent test data builders, linked into each project
└── M3A.sln
```

Dependencies point one way only: `Api → Delegates → Repositories → Domain`.

## Adding a resource

1. `src/M3A.Domain/Entities/{Resource}.cs`
2. `src/M3A.Repositories/` — `I{Resource}Repository.cs`, `{Resource}Repository.cs`,
   a `Persistence/Configurations/{Resource}Configuration.cs`, and a `DbSet` on `M3ADbContext`;
   register it in `Extensions/ServiceCollectionExtensions.AddRepositoryImplementations`.
3. `src/M3A.Delegates/` — `I{Resource}Delegate.cs`, `{Resource}Delegate.cs`;
   register it in `Extensions/ServiceCollectionExtensions.AddDelegates`.
4. `src/M3A.Api/Dtos/{Resource}Dtos.cs`, `Validators/`, `Routes/{Resource}Routes.cs`,
   then one line in `Routes/RouteRegistration.MapM3ARoutes`.
5. Mirror the four test suites.

## Conventions baked in

| Concern | Decision |
|---|---|
| ID format | `[A-Za-z0-9\-]+`, enforced by `Domain.ValueObjects.ResourceId` — the single source of truth for routes and validators |
| Serialization | camelCase, enums as strings (`AddApiServices`) |
| Errors | RFC 7807 ProblemDetails everywhere: `400` validation, `404` not found, `422` business rule, `500` unhandled |
| Status mapping | Derived from exception type in `Middleware/GlobalExceptionHandler` — the only place it happens |
| Package versions | Central, in `Directory.Packages.props`; csproj files name packages only |
| Compiler | `TreatWarningsAsErrors`, nullable enabled, no `any`-equivalent escape hatches |

### One deliberate deviation from the PRD

The PRD's delegate test table says delegates return DTOs, while its file layout puts DTOs
in `M3A.Api/Dtos/`. Those two cannot both hold without the delegate layer depending on the
API layer. This skeleton keeps the layout and has **delegates return domain entities**, with
the API layer mapping to DTOs in `Extensions/ItemMappings.cs`. To flip it, move the DTOs into
a shared contracts project.

## Commands

```bash
dotnet build                                   # whole solution, warnings are errors
dotnet test                                    # all four suites
dotnet test --collect:"XPlat Code Coverage"    # with coverage
dotnet run --project src/M3A.Api              # http://localhost:8080
```

OpenAPI is served at `/openapi/v1.json` in the Development environment.

`dotnet run` needs the PostgreSQL instance from `appsettings.json`; the test suite does not
(it swaps in EF Core InMemory via `M3AApiFactory`). No migrations exist yet — add the first
one with `dotnet ef migrations add Initial --project src/M3A.Repositories --startup-project src/M3A.Api`
once real entities land.
