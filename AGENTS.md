# Agent Guidelines for Perinma

## Build & Test Commands

### Building
```bash
dotnet build
```

### Running
```bash
dotnet run --project src/perinma.csproj
```

### Testing
```bash
# Run all tests
dotnet test

# Run specific test file
dotnet test --filter "FullyQualifiedName~ColorUtilsTests"

# Run single test method
dotnet test --filter "FullyQualifiedName~ColorUtilsTests.NormalizeHexColor_WithAlpha_StripsAlphaChannel"
```

### Environment Setup
```bash
# Generate build secrets (required for compilation)
mise prepare-secrets

# Set environment variables
mise set -E local GOOGLE_CLIENT_ID=your-client-id
mise set -E local GOOGLE_CLIENT_SECRET=your-client-secret
```

## Code Style Guidelines

### Imports & Using Statements
- Place `using` statements at the top, organized by:
  1. System namespaces
  2. Third-party packages (Google.Apis, CommunityToolkit, Dapper, etc.)
  3. Local `perinma.*` namespaces
- Use `using` declarations for disposable resources where appropriate
- Tests have `ImplicitUsings` enabled; main project uses explicit usings

### Formatting & Structure
- Target: .NET 10.0 (net10.0)
- C# language version: latest (C# 14)
- Nullable reference types enabled: `<Nullable>enable</Nullable>`
- Use `record` or `class` as appropriate (prefer `class` unless immutability is required)
- Use primary constructor syntax where parameters are simple: `public class SqliteStorage(DatabaseService databaseService)`
- Long method signatures: put each parameter on its own line with consistent indentation

### Naming Conventions
- **Classes**: PascalCase (`CalendarService`, `ViewModelBase`)
- **Interfaces**: PascalCase with `I` prefix (`ICalendarSource`, `IGoogleCalendarService`)
- **Methods**: PascalCase (`GetCalendarsAsync`, `CreateOrUpdateEventAsync`)
- **Async Methods**: Append `Async` suffix to all async methods
- **Properties**: PascalCase (`Account`, `Enabled`, `WeekStart`)
- **Private Fields**: camelCase with underscore prefix (`_calendarSource`, `_weekStart`)
- **Constants/Readonly**: PascalCase (`TextColorOnDark`, `TextFormatting`)
- **Parameters**: camelCase (`accountId`, `cancellationToken`)
- **Local Variables**: camelCase (`allCalendars`, `rowsAffected`)

### Types & Patterns
- Use `required` keyword for required properties in models
- Use nullable reference types (`string?`, `DateTime?`, `int?`) where appropriate
- Use `Guid` for database IDs, `string` for external IDs
- Use `DateTime` and `DateTimeOffset` for timestamps
  - Preserve timezone information
- Use expression-bodied members for simple properties and methods
- Use collection expressions: `var items = [];` instead of `var items = new List<T>();`
- Use `using var` for disposable resources with automatic disposal
- When get Ical occurrences, provide a sensible starttime and then iterate the returned Enumeration
  for as many occurrences as you need. Do not fetch them all.

### MVVM Patterns
- ViewModels inherit from `ViewModelBase` (which inherits `ObservableObject`)
- ViewModels live right next to their View
- View and ViewModel are bundled in packages that resemble the business domain they belong to
- Use CommunityToolkit.Mvvm source generators:
  - `[ObservableProperty]` for auto-generated properties
  - `[RelayCommand]` for auto-generated commands
  - `[NotifyPropertyChangedFor(nameof(PropertyName))]` to trigger other property changes
- Private backing fields for observable properties use underscore prefix
- Use `partial class` for ViewModels with source generators

### Database & Storage
- Use Dapper ORM with SQLite
- Always use `using var connection = databaseService.GetConnection();` for connections
- Set `commandTimeout: 30` on all Dapper queries
- Use parameterized queries with anonymous types or parameter objects
- Use async methods: `QueryAsync`, `ExecuteAsync`, `QuerySingleOrDefaultAsync`
- JSON columns use SQLite jsonb functions with `$.key` path syntax

### Error Handling
- Use `InvalidOperationException` for invalid state/arguments
- Use specific exception types when possible
- Log errors to `Console.WriteLine` with context: status codes, response bodies, etc.
  - When part of a UI flow, show and await a MessageBox
- Wrap HTTP errors with descriptive messages including status code
- Try-catch with specific exception types, avoid catching bare `Exception` unless needed
- Handle nullable values before using (null coalescing `??`, null-conditional `?.`)

### Async/Await
- All async methods accept `CancellationToken cancellationToken = default`
- Pass cancellationToken through to async operations
- Use `async Task` or `async Task<T>` return types
- Configure `await` with `.ConfigureAwait(false)` in library code (not needed in UI code)
- Use `ValueTask` for high-throughput scenarios (rarely needed)

### Testing
- Framework: NUnit 4.x with `Avalonia.Headless.NUnit`
- Use `[TestFixture]` attribute on test classes
- Use `[Test]` attribute on test methods
- Test naming: `MethodName_Scenario_ExpectedResult` (PascalCase)
- Use `Assert.That(actual, Is.EqualTo(expected))` syntax
  - Combine multiple assertions using `Assert.Multple`, aside from Count or NotNull
    assertions that are prerequisites to the assertions to be combined
- Arrange-Act-Assert pattern in test bodies
- Tests use explicit using statements for tested namespaces
- Mock external services (see tests/Fakes/ for examples)
- Test both positive and negative cases

### XML Documentation
- Use `///` for public API documentation
- Include `<summary>`, `<param>`, `<returns>` where helpful
- Keep descriptions concise but informative
- Document non-obvious behavior and edge cases

### String Interpolation & Formatting
- Prefer `$` string interpolation over `String.Format`
- Use `ToUpperInvariant()` for case-independent comparisons
- Use string interpolation for SQL parameters (not string concatenation)
- Use raw string literals (`"""..."""`) for multi-line SQL queries

### LINQ & Collections
- Use LINQ method syntax, not query syntax
- Null-check collections before LINQ: `allCalendars?.AddRange(response.Items)`
- Use `ToList()` to materialize when needed
- Use collection expressions for initialization: `var items = [];`

### Constants & Magic Numbers
- Avoid magic numbers; use named constants or readonly fields
- Example: `TimeSpan.FromMinutes(2)` instead of `120` seconds
- Define constants at class level for reuse
