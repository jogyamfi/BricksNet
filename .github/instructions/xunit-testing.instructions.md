---
applyTo: "**/tests/**/*.cs"
---

# xUnit Testing (BricksNet)

These rules apply to all test code under `tests/`.

## Naming & Structure

- Name a test class `{ClassUnderTest}Tests` in the same relative namespace as the class under test, but in the test project.
- Structure every test as Arrange / Act / Assert with a blank line between sections; each method tests exactly one behaviour.

  ```csharp
  [Fact]
  public async Task ExecuteStatementAsync_ReturnsOperation_WhenSqlValid()
  {
      // Arrange
      var session = CreateSession();

      // Act
      var op = await session.ExecuteStatementAsync("SELECT 1");

      // Assert
      Assert.NotNull(op);
  }
  ```

## Data-Driven Tests

- Use `[Theory]` + `[InlineData]` for simple inputs; use `[MemberData]` for inputs that cannot be inlined.

  ```csharp
  [Theory]
  [InlineData("Int64", typeof(long))]
  [InlineData("Utf8", typeof(string))]
  public void Convert_MapsArrowTypeToClrType(string arrowType, Type expected) { /* ... */ }
  ```

## Async

- Async test methods return `Task`, never `void`. Use `await` throughout; never call `.GetAwaiter().GetResult()` inside a test.

## Mocking

- Use `Moq` 4.20+. Prefer `Setup(...).ReturnsAsync(...)` over callback-style setups. Verify critical interactions with `Verify(...)` in the Assert phase.

  ```csharp
  mockBackend.Setup(b => b.GetStatusAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(QueryStatus.Succeeded);
  // ...
  mockBackend.Verify(b => b.GetStatusAsync(It.IsAny<CancellationToken>()), Times.Once);
  ```

## HTTP Mocking

- Use `RichardSzalay.MockHttp` (`MockHttpMessageHandler`) to mock `HttpClient` without a real server.

  ```csharp
  var mock = new MockHttpMessageHandler();
  mock.When("https://host/api/2.0/sql/statements").Respond("application/json", "{ \"status\": ... }");
  var client = new HttpClient(mock);
  ```

## Fixtures

- Use `IClassFixture<T>` for expensive shared setup (e.g., `FakeServiceFixture`) and inject it via the constructor.
- Implement `IAsyncLifetime` on fixtures needing async setup/teardown (`InitializeAsync` / `DisposeAsync`).
- The `FakeServiceFixture` starts a `TestServer` (via `WebApplicationFactory`/`TestHost`). The replayer looks up `{TestClass}/{TestMethod}/step-{n}.json`. Reset the per-test step counter in `InitializeAsync`.

## Categorisation

- Decorate tests that require live Databricks credentials with `[Trait("Category", "E2E")]`; exclude them from default runs:

  ```text
  dotnet test --filter "Category!=E2E"
  ```

## Coverage

- Target 85% line coverage on `src/BricksNet/`.
- Exclude generated Thrift code from coverage by marking generated types `[ExcludeFromCodeCoverage]` (files under `src/BricksNet/Backends/Thrift/Generated/`).
