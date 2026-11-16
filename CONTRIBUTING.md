# Contributing to BricksNet

Thanks for your interest in contributing. Bug reports, fixes, documentation and new SQL
translations are all welcome.

## Before you start

- **Bugs and small fixes:** open an issue or send a pull request directly.
- **New features or public API changes:** open an issue first so the design can be agreed before
  you write the code.
- **Security problems:** do not open an issue. Follow [SECURITY.md](SECURITY.md).

## Build and test

You need the .NET SDK version pinned in [global.json](global.json).

```shell
dotnet build BricksNet.sln
dotnet test BricksNet.sln --filter "Category!=E2E"
```

This test run is offline and needs no Databricks workspace. The build treats warnings as errors
and enforces the code style in [.editorconfig](.editorconfig), so a clean build means the style
checks passed.

### Live tests

Some suites run against a real Databricks workspace (the free edition is enough). They are run by
hand and never in CI. See [docs/LIVE_TESTING.md](docs/LIVE_TESTING.md). If your change touches a
backend, result handling, type mapping or SQL generation, please run them and say so in the pull
request. If you cannot, say that instead and a maintainer will run them.

Never commit credentials. `scripts/live-tests.env` and `appSettings.User.json` are git-ignored for
this reason.

## Pull requests

- Branch from `main` and keep each pull request to one change.
- Add or update tests for the behaviour you change.
- Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md) for anything a user would notice.
- Update the docs ([README.md](README.md), [docs/EFCORE.md](docs/EFCORE.md), the package READMEs
  under `src/`) when behaviour or public API changes.
- CI must pass: build, offline tests, package verification and the security scan.

## Project layout

| Path | Contents |
|---|---|
| `src/BricksNet` | Core library: fluent API, ADO.NET provider, REST and Thrift backends |
| `src/BricksNet.EntityFrameworkCore` | EF Core provider |
| `tests/BricksNet.Tests`, `tests/BricksNet.EntityFrameworkCore.Tests` | Offline unit tests |
| `tests/BricksNet.IntegrationTests` | Fake-service integration tests and live (`E2E`) tests |
| `tests/BricksNet.EntityFrameworkCore.FunctionalTests` | Live EF Core tests |
| `samples/BricksNet.Samples` | Runnable demos |

Releases are described in [docs/RELEASING.md](docs/RELEASING.md).

## Licensing

BricksNet is licensed under [Apache 2.0](LICENSE). By submitting a contribution you agree that it
is licensed under the same terms, and you confirm that you have the right to submit it.

## Conduct

This project follows a [code of conduct](CODE_OF_CONDUCT.md).
