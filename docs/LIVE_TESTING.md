# Live testing against Databricks

The default test run is offline. The live suites run only when you give them a workspace, and they
are run by hand, never in CI. This guide sets them up on a free Databricks workspace.

| Suite | What it covers | Where |
|---|---|---|
| Core (`Category=E2E`) | Fluent API and ADO.NET over REST and Thrift: types, NULLs, parameters, large results / CloudFetch, metadata, errors, rows affected, kept sessions | `tests/BricksNet.IntegrationTests/Live`, `ThriftBackendIntegrationTests` |
| EF Core functional | LINQ translation, `SaveChanges`, bulk operations, type round trips. Creates and drops its own `efct_*` schema | `tests/BricksNet.EntityFrameworkCore.FunctionalTests` |
| Samples | `AdoNetDapper`, `Di`, `EfCore` demos | `samples/BricksNet.Samples` |

## 1. Check which edition you have

BricksNet talks to a **SQL warehouse** with a **personal access token**. The legacy *Community
Edition* (workspace URL `community.cloud.databricks.com`) has neither, so it cannot run these tests.
*Free Edition* (URL like `dbc-1234abcd-5678.cloud.databricks.com`, sign-up at
<https://www.databricks.com/learn/free-edition>) has both. If you signed up for "Community" recently
you most likely have Free Edition. Check the address bar after you log in.

Free Edition limits that matter here:

- One small serverless SQL warehouse. It stops when idle, so the first query after a pause waits for
  it to start (up to a minute or two).
- No classic all-purpose clusters. The Thrift tests use the warehouse's HTTP path, which accepts
  Thrift as well as REST.
- Unity Catalog with a `workspace` catalog you can create schemas in.

## 2. Things to create in the workspace

1. **SQL warehouse.** Open **SQL Warehouses**. Free Edition already has one (*Serverless Starter
   Warehouse*). Open it, then **Connection details**, and note:
   - **Server hostname**, e.g. `dbc-1234abcd-5678.cloud.databricks.com`
   - **HTTP path**, `/sql/1.0/warehouses/<warehouse-id>`. You need the id at the end.
2. **Personal access token.** Click your avatar, then **Settings > Developer > Access tokens >
   Generate new token**. Name it `bricksnet-live-tests` and give it a short lifetime (such as 30
   days). Copy it now, because it is shown only once.
3. **Catalog.** Open **Catalog** and confirm `workspace` exists. If you use another catalog, edit the
   catalog name in `scripts/databricks/*.sql` and set `DATABRICKS_CATALOG` to match.

## 3. Configure the runner

```powershell
Copy-Item scripts/live-tests.env.example scripts/live-tests.env
notepad scripts/live-tests.env      # fill in host, token, warehouse id
```

`scripts/live-tests.env` is git-ignored. Don't put the token anywhere else in the repo.

## 4. Create the fixtures

Use one of these two options.

**Option A, from the terminal** (runs the SQL through the Statement Execution API):

```powershell
./scripts/Run-LiveTests.ps1 -Steps Setup
```

**Option B, in the browser:** open **SQL Editor**, select your warehouse, paste
`scripts/databricks/01_setup.sql` and click **Run all**. Then run `02_verify.sql`. Every row should
show `ok = true`.

This creates `workspace.bricksnet_live` with:

| Object | Purpose |
|---|---|
| `people` (5 rows) + view `active_people` | Basic reads, NULLs, parameters, metadata calls |
| `all_types` (3 rows) | Every SQL type: typical values, edge values (min/max, unicode, empty binary, year 1 / 9999 dates), and an all-NULL row |
| `big_table` (2,000,000 rows) | Multi-chunk results, CloudFetch over REST |
| `dml_target` | INSERT / UPDATE / DELETE / MERGE row counts (emptied by each run) |

## 5. Run the tests

```powershell
./scripts/Run-LiveTests.ps1                                  # Core, EfCore, Samples
./scripts/Run-LiveTests.ps1 -Steps Core                      # one step
./scripts/Run-LiveTests.ps1 -Steps Core -Filter "FullyQualifiedName~LargeResult"
```

Each run first checks the token and the warehouse and starts the warehouse if it is stopped. The
runner removes the credential environment variables from your shell when it finishes.

To run from Visual Studio or `dotnet test` directly, set these variables yourself:

| Variable | Value |
|---|---|
| `BRICKSNET_E2E_HOST`, `BRICKSNET_EF_HOST` | Server hostname |
| `BRICKSNET_E2E_TOKEN`, `BRICKSNET_EF_TOKEN` | Personal access token |
| `BRICKSNET_E2E_HTTP_PATH`, `BRICKSNET_E2E_THRIFT_PATH`, `BRICKSNET_EF_HTTPPATH` | `/sql/1.0/warehouses/<id>` |
| `BRICKSNET_E2E_CATALOG`, `BRICKSNET_EF_CATALOG` | `workspace` |
| `BRICKSNET_E2E_SCHEMA` | Optional, defaults to `bricksnet_live` |
| `BRICKSNET_EF_USETHRIFT` | Optional, `true` runs the EF suite over Thrift |

Without them, the live tests show as skipped.

## 6. Manual checks not covered by the runner

- **OAuth PKCE (`FluentOAuth` sample).** In **Settings > Identity and access > App connections**,
  add a public OAuth app with redirect URL `http://localhost:8020` and scope `sql`. If App
  connections is not available on your edition, skip this check. Then set `OAuthClientId` and the
  `https://<host>/oidc/v1/authorize` and `/oidc/v1/token` endpoints in
  `samples/BricksNet.Samples/appsettings.User.json` and run
  `dotnet run --project samples/BricksNet.Samples -- FluentOAuth`.
- **Cold start.** Stop the warehouse in the UI, then run `-Steps Core -Filter
  "FullyQualifiedName~SelectPeople"`. It should wait for the warehouse to start rather than fail.
- **Bad token.** Put a wrong token in `live-tests.env`. The check step should fail with a clear
  message.
- **Query history.** In **Query History**, filter by your user. The statements should appear with
  the BricksNet user agent (see [USER_AGENT.md](USER_AGENT.md)).

`FluentPat` runs a query against tables that exist only in a private workspace, so it is not part of
the runner.

## 7. Clean up

```powershell
./scripts/Run-LiveTests.ps1 -Steps Teardown
```

This drops `bricksnet_live` and lists any `efct_*` schemas left by an interrupted EF Core run. When
you finish testing, revoke the token under **Settings > Developer > Access tokens**.
