# {Service} - REST API Contract (PRD Template)

> Generic PRD template for any REST microservice. Replace `{Service}` with the service name and `{Resource}`/`items` with your domain resource(s). Only one example resource (`Items`) is included; replicate its pattern for additional resources.

## Overview
- **Service**: {Service} Management REST API
- **Language/Framework**: C# .NET 10+ with Minimal APIs
- **Base URL**: `http://localhost:8080` (configurable via `appsettings.json`)
- **Content-Type**: `application/json`
- **ID Format**: Regex `[A-Za-z0-9\-]+`

---

## Architecture

### Layered Architecture
```
┌─────────────────────────────────────────────────────────────┐
│                        API Layer (Minimal APIs)             │
│  • Endpoint definitions (MapGet, MapPost, MapPut, etc.)     │
│  • Request/Response mapping                                 │
│  • Validation (FluentValidation)                            │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      Delegate Layer                         │
│  • I{Resource}Delegate (one per domain)                     │
│  • Business logic orchestration                             │
│  • Transaction management                                   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Repository Layer                         │
│  • I{Resource}Repository (one per aggregate root)           │
│  • Data access (EF Core / Dapper)                           │
│  • Query building                                           │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      Domain Models                          │
│  • Entities                                                 │
│  • Value Objects                                            │
│  • Enums                                                    │
└─────────────────────────────────────────────────────────────┘
```

### Key Patterns
| Pattern | Implementation |
|---------|----------------|
| **Minimal APIs** | `app.MapGet()`, `app.MapPost()`, etc. in `Program.cs` or route modules |
| **Delegate** | One per domain (`I{Resource}Delegate`) - orchestrates repositories, contains business rules |
| **Repository** | One per aggregate root - data access abstraction, testable via in-memory implementations |
| **Dependency Injection** | Built-in DI container, scoped per request |
| **Validation** | FluentValidation for request DTOs |
| **Serialization** | System.Text.Json with camelCase, enum as string |

---

## Endpoints

Example resource: `Items`. Follow this contract shape for every resource.

| Method | Path | Request Body | Response | Description |
|--------|------|--------------|----------|-------------|
| `GET` | `/items` | - | `200` `ItemDto[]` | List all items |
| `GET` | `/items/{itemId}` | - | `200` `ItemDto` \| `400` (invalid ID) \| `404` | Get item by ID |
| `POST` | `/items` | `CreateItemDto` | `201` + `Location` \| `400` | Create new item |
| `PUT` | `/items/{itemId}` | `UpdateItemDto` | `200` `ItemDto` \| `404` | Full update item |
| `DELETE` | `/items/{itemId}` | - | `204` \| `404` | Delete item |

**ItemDto**
```json
{
  "id": "string",
  "name": "string"
}
```

**CreateItemDto**
```json
{
  "name": "string"
}
```

**UpdateItemDto**
```json
{
  "name": "string"
}
```

---

## Error Responses

| Code | Scenario |
|------|----------|
| `400` | Invalid ID format, malformed JSON, validation failure (FluentValidation) |
| `404` | Resource not found |
| `422` | Business logic violation (duplicate entity, invalid state transition, etc.) |
| `500` | Unhandled exception |

**Error Response Format**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Failed",
  "status": 400,
  "errors": {
    "fieldName": ["error message"]
  }
}
```

---

## Configuration (`appsettings.json`)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database={service}_db;Username={service}_svc;Password=password"
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

---

## Testing Requirements

### Test Strategy Overview
| Layer | Test Type | Framework | Coverage Target |
|-------|-----------|-----------|-----------------|
| API Routes | Integration | xUnit + WebApplicationFactory | 100% endpoint coverage |
| Delegates | Unit | xUnit + Moq/NSubstitute | 90%+ branch coverage |
| Repositories | Unit + Integration | xUnit + EF Core InMemory / Testcontainers | 80%+ |
| Domain | Unit | xUnit | 100% business logic |

---

#### Required Test Cases per Endpoint

**Items Routes** (`/items`)
| Test Case | Description |
|-----------|-------------|
| `GET /items` returns 200 with empty array | No items in DB |
| `GET /items` returns 200 with items | Multiple items exist |
| `GET /items/{id}` returns 200 with item | Valid ID exists |
| `GET /items/{id}` returns 404 | Valid ID not found |
| `GET /items/{id}` returns 400 | Invalid ID format (non-alphanumeric) |
| `POST /items` returns 201 + Location | Valid CreateItemDto |
| `POST /items` returns 400 | Missing/invalid fields, duplicate constraint violations |
| `PUT /items/{id}` returns 200 | Valid UpdateItemDto |
| `PUT /items/{id}` returns 404 | Valid ID not found |
| `DELETE /items/{id}` returns 204 | Valid ID exists |
| `DELETE /items/{id}` returns 404 | Valid ID not found |

#### Cross-Cutting Route Tests
| Test Case | Description |
|-----------|-------------|
| All endpoints return 401/403 | When auth middleware enabled (future) |
| Request validation | FluentValidation errors return 400 with ProblemDetails |
| Content-Type | All endpoints require/return `application/json` |
| ID format validation | All `{id}` params reject non-matching regex |
| Concurrency | Parallel requests don't cause data corruption |

---

### Delegate Tests (Unit Tests)

#### Setup Pattern
```csharp
public class ItemDelegateTests
{
    private readonly Mock<IItemRepository> _repoMock;
    private readonly IItemDelegate _delegate;

    public ItemDelegateTests()
    {
        _repoMock = new Mock<IItemRepository>();
        _delegate = new ItemDelegate(_repoMock.Object);
    }
}
```

#### Required Test Cases per Delegate

**{Resource}Delegate**
| Method | Test Cases |
|--------|------------|
| `GetByIdAsync` | Returns DTO when found, null when not found, maps nested entities |
| `GetAllAsync` | Returns empty list, returns multiple with correct mapping |
| `CreateAsync` | Generates ID, saves via repo, returns ID, validates unique constraints |
| `UpdateAsync` | Updates all fields, returns updated DTO, throws if not found |
| `DeleteAsync` | Deletes entity, throws if not found, handles referential integrity |

---

### Test Data Builders
```csharp
// Fluent builders for test data
public class ItemBuilder
{
    private Item _item = new();

    public ItemBuilder WithId(string id) { _item.Id = id; return this; }
    public ItemBuilder WithName(string name) { _item.Name = name; return this; }
    public Item Build() => _item;
}
```

---

### Test Execution Requirements
| Requirement | Detail |
|-------------|--------|
| **CI Pipeline** | All tests run on every PR |
| **Coverage** | `dotnet test --collect:"XPlat Code Coverage"` with thresholds |
| **Parallelization** | Tests must be parallelizable (no shared state) |
| **Determinism** | No flaky tests; use fixed dates/IDs in tests |
| **Isolation** | Each test creates own data; no test ordering dependency |

---

### Test Project Structure
```
tests/
├── {Service}.Api.Tests/
│   ├── Routes/
│   │   └── {Resource}RoutesTests.cs
│   ├── Middleware/
│   │   └── ValidationMiddlewareTests.cs
│   └── {Service}ApiTests.cs (base class)
├── {Service}.Delegates.Tests/
│   └── {Resource}DelegateTests.cs
├── {Service}.Repositories.Tests/
│   └── {Resource}RepositoryTests.cs
└── {Service}.Domain.Tests/
    └── {Resource}Tests.cs
```

---

## Project Structure
```
{Service}/
├── src/
│   ├── {Service}.Api/
│   │   ├── Program.cs                    # Minimal API setup, DI registration
│   │   ├── Routes/
│   │   │   └── {Resource}Routes.cs
│   │   ├── Dtos/
│   │   │   └── {Resource}Dtos.cs
│   │   ├── Validators/
│   │   └── Extensions/
│   ├── {Service}.Delegates/
│   │   ├── I{Resource}Delegate.cs
│   │   └── {Resource}Delegate.cs
│   ├── {Service}.Repositories/
│   │   ├── I{Resource}Repository.cs
│   │   └── {Resource}Repository.cs
│   └── {Service}.Domain/
│       └── {Resource}.cs
├── tests/
│   ├── {Service}.Api.Tests/
│   ├── {Service}.Delegates.Tests/
│   └── {Service}.Repositories.Tests/
└── {Service}.sln
```
