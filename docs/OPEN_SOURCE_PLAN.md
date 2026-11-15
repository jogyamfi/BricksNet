# BricksNet: remaining work before the repository goes public

This is a work plan for a coding agent (Claude Opus 5.5) working in this repository with the
maintainer. It covers what is left after a first open-source readiness pass. Delete this file when
the work is done; it is not meant to ship in the public repository.

## Context

BricksNet is a .NET 10 Databricks SQL client: a fluent async API, an ADO.NET provider
(`src/BricksNet`) and an EF Core 10 provider (`src/BricksNet.EntityFrameworkCore`). The GitHub
repository `jogyamfi/BricksNet` is private today. The maintainer wants to make it public under
Apache-2.0 and publish preview packages to nuget.org (the release process is in
`docs/RELEASING.md`).

The first public release freezes two things that are cheap to change now and expensive later: the
git history and the public API. Most of this plan exists because of that.

### Already done (uncommitted in the working tree on `feature/release-phase-2-3`)

Do not redo these. Review them if a later task touches the same file.

- `LICENSE` (Apache-2.0 text) and `NOTICE` (attribution to `databricks-sql-nodejs`,
  `databricks-jdbc` and Apache Hive; both Databricks drivers were confirmed as Apache-2.0).
- `SECURITY.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `.github/PULL_REQUEST_TEMPLATE.md`,
  `.github/ISSUE_TEMPLATE/*`, `.github/dependabot.yml`.
- `coverage-out/` removed from git and added to `.gitignore`.
- Docs no longer claim OAuth client credentials (M2M) or a `BricksNetTransaction` type, neither of
  which exists. Changed: `README.md`, `src/BricksNet/README.md`, `CHANGELOG.md`,
  `samples/BricksNet.Samples/Demos/FluentOAuthDemo.cs`.
- `README.md`: build and licence badges, preview status note, "not affiliated with Databricks"
  disclaimer. The NuGet badge is still commented out (see task 8).
- `.github/workflows/build.yml` and `security.yml`: actions pinned to commit SHAs, and
  `permissions: contents: read` added. `release.yml` was already pinned.

### Ground rules

- Baseline: `dotnet build BricksNet.sln -c Release` gives 0 warnings (warnings are errors) and
  `dotnet test BricksNet.sln -c Release --filter "Category!=E2E"` passes 736 tests offline. Both
  must still hold after every task. Run `./scripts/Test-Packages.ps1` after any packaging change.
- Live tests (`docs/LIVE_TESTING.md`) need the maintainer's workspace credentials. Do not try to
  run them yourself. Say in your report which tasks would benefit from a live run.
- Add a line under **Unreleased** in `CHANGELOG.md` for every user-visible change.
- Do not commit, push, rewrite history, delete branches or change GitHub settings unless the
  maintainer asks in that conversation. Tasks 1 and 8 are entirely in that category.
- One task per commit or pull request, so each can be reviewed alone.

## Decisions the maintainer must make first

Ask these at the start. Tasks 1, 2 and 7 are blocked on the answers; tasks 3 to 6 are not, so
start on those while waiting.

| # | Decision | Recommendation |
|---|---|---|
| D1 | Some commits carry a work-domain author email (run `git log --all --format='%an <%ae>' \| sort \| uniq -c`). Does the maintainer's employer have any claim on this code, and has open-sourcing been cleared? | Only the maintainer can answer. Do not proceed with task 1 until they confirm it is cleared. Rewriting the email does not settle ownership. |
| D2 | Once cleared: keep history as is, rewrite the author email, or publish a single squashed commit? | Rewrite the email with a mailmap and publish to a fresh repository (task 1). |
| D3 | Internal planning documents: delete or keep under `docs/design/`? | Delete the agent prompts; keep the EF Core design document (task 2). |
| D4 | `AllowSelfSignedCerts`: rename it to match what it does, or change what it does to match the name? | Rename (task 3). |
| D5 | Implement OAuth client credentials (M2M) before the first release? | Not a blocker. Do it as a follow-up (task 7). |

## Tasks

### 1. Clean the author identity in git history (blocked on D1, D2)

**Why.** Making the repository public exposes every commit's author email. Rewriting history after
publication is not effective, because clones and forks keep the old commits.

**What.**

1. With the maintainer, write a mailmap that maps the work-domain identity to their personal
   identity. Rewrite all branches with `git filter-repo --mailmap <file>`. Work on a fresh clone,
   not the maintainer's working copy.
2. Check the result: `git log --all --format='%ae%n%ce' | sort -u` shows only the intended
   addresses. Check both author and committer.
3. Publish the rewritten history to a **new** GitHub repository instead of force-pushing and
   flipping the existing one to public. GitHub keeps the old commits reachable through
   `refs/pull/*` for merged pull requests (#2, #3, #4), and a force-push does not remove them. The
   usual route is to rename the existing private repository (for example `BricksNet-archive`),
   create a new `BricksNet`, and push `main` only.
4. Push only `main`. The local branches (`feat/efcore-phase*`, `backup/*`, `fix/*`, `docs/*`) are
   merged history and do not need to exist in the public repository.
5. The repository URL appears in `src/Directory.Build.props`, `README.md`, `SECURITY.md`,
   `.github/ISSUE_TEMPLATE/config.yml` and `docs/RELEASING.md`. If the name stays `BricksNet` no
   change is needed. NuGet trusted publishing (see `docs/RELEASING.md`) is tied to the repository
   and must be configured against the new one.

**Done when.** The maintainer has confirmed the address list from step 2, and the public
repository contains no commit, tag or pull-request ref with the work-domain address.

This task is destructive and outward-facing. Confirm each of steps 1, 3 and 4 with the maintainer
before running it.

### 2. Remove internal planning material (blocked on D3)

**Why.** These files are prompts and plans written for coding agents while the library was built.
They describe intentions, not the shipped library, and some are wrong today (the original prompt
targets .NET 8).

**What.**

- Delete `PLAN.md`, `docs/opus-prompt.md` and `docs/NUGET_RELEASE_PLAN.md`.
- `docs/USER_AGENT.md` documents the Databricks JDBC driver's user agent, not BricksNet's.
  `docs/LIVE_TESTING.md` (near line 109) links to it. Replace it with a short section describing
  the user agent BricksNet actually sends (read the code that builds the header; start from the
  `UserAgent` option on `BricksNetClientOptions`), or delete it and fix the link.
- `docs/EFCORE_PLAN.md` is linked from `README.md` as "design and roadmap". Keep it, but read it
  against the code and `docs/EFCORE.md`: remove phase and agent-execution language, drop the link
  to `PLAN.md`, and make sure the roadmap lists only work that is really outstanding.
- Remove "Phase N" comments that refer to the deleted plan: `Directory.Packages.props`,
  `src/BricksNet/BricksNet.csproj`, `src/BricksNet/Backends/Thrift/Generated/README.md` and a few
  source files. Find them with `grep -rn "Phase [0-9]" src tests samples Directory.Packages.props`.
  Keep the comment when it explains something; drop only the phase reference.
- `.github/instructions/*.instructions.md` are GitHub Copilot instruction files. They can stay.
  Read each one and fix or remove statements that no longer match the code (for example, one
  mentions `AllowSelfSignedCerts`, which task 3 renames).

**Done when.** `grep -rn "PLAN.md\|opus-prompt\|NUGET_RELEASE_PLAN" .` finds nothing outside this
file, and no Markdown link in the repository points at a deleted file.

### 3. Fix the misleading TLS option (blocked on D4)

**Why.** `SslOptions.AllowSelfSignedCerts` does not allow self-signed certificates. In
`src/BricksNet/Connection/HttpClientBuilderExtensions.cs` (`ConfigureTls`), it tolerates
`RemoteCertificateNameMismatch` and still rejects chain errors, so a self-signed certificate fails.
A security option whose name says something different from its behaviour should not ship in a
public API.

**What (recommended: rename).** Trusting a private or self-signed certificate is already covered
by `SslOptions.CustomTrustStore`, which is the safer way to do it. So keep the behaviour and rename
the option to say what it does, for example `SkipHostnameVerification`:

- `src/BricksNet/Contracts/Options/SslOptions.cs`, the `AllowSelfSignedCerts` connection-string
  keyword and property in `src/BricksNet/AdoNet/BricksNetConnectionStringBuilder.cs`, the log
  messages and parameter names in `HttpClientBuilderExtensions.cs`.
- Tests: `tests/BricksNet.Tests/Connection/HttpClientBuilderExtensionsTests.cs` and
  `tests/BricksNet.Tests/AdoNet/BricksNetConnectionStringBuilderTests.cs`.
- Nothing has been released, so do not keep the old name as an alias.
- Document in `README.md` how to connect through a TLS-inspecting proxy or to a private endpoint:
  use `CustomTrustStore`. `CustomTrustStore` is not reachable from a connection string today;
  decide with the maintainer whether to add a keyword that takes a PEM file path.

While there, review `ValidateAgainstCustomTrustStore` in the same file. It masks
`RemoteCertificateChainErrors` and rebuilds the chain against the custom store with
`RevocationMode.NoCheck`. Confirm with a test that a certificate chaining to a root that is *not*
in the custom store is rejected, and that an expired certificate is rejected.

**Done when.** No public member or connection-string keyword is named `AllowSelfSignedCerts`, the
tests above pass, and the new tests for the custom trust store exist.

### 4. Review and lock the public API

**Why.** After the first NuGet release every public type is a compatibility commitment. The core
library has about 60 public types, and several look like implementation details.

**What.**

1. List the public surface of both assemblies. Propose to the maintainer which types become
   `internal`. Candidates to look at first: everything under `Backends/`, `Connection/Handlers/`
   and `Results/` (result handlers, `Lz4Decompressor`, `ResultSlicer`, `ArrowTypeConverter`),
   `AuthProviderFactory`, `CachedTokenProvider`, and the `IBackend` / `ISessionBackend` /
   `IOperationBackend` / `IResultsProvider` contracts. Keep public whatever a user needs to
   configure, call or mock: the client, session and operation types and their interfaces, the
   options records, the ADO.NET types, `IAuthProvider`, `ITokenCache`, `IOAuthPersistence`.
   Get the maintainer's agreement on the list before changing anything. The test projects already
   have `InternalsVisibleTo`.
2. Add `Microsoft.CodeAnalysis.PublicApiAnalyzers` to both library projects (version in
   `Directory.Packages.props`, `PrivateAssets="all"`), with `PublicAPI.Shipped.txt` (empty until
   the first release) and `PublicAPI.Unshipped.txt` (generated from the agreed surface).
3. Remove `CS1591` from `NoWarn` in both library projects and write the missing XML documentation.
   The hand-ported Thrift types under `Backends/Thrift/Generated` have their own `.editorconfig`;
   keep them exempt.
4. `EnablePackageValidation` is already on. After the first stable release, set
   `PackageValidationBaselineVersion` (there is a comment in `src/Directory.Build.props`); note
   this in `docs/RELEASING.md`.

**Done when.** The build passes with `CS1591` enforced, the PublicAPI files match the agreed
surface, and no type the maintainer agreed to hide is public.

### 5. Finish the third-party attribution

**Why.** `NOTICE` now exists at the repository root, but Apache-2.0 expects attribution to travel
with the distributed work, and the derived source files do not say where they came from.

**What.**

- Pack `LICENSE` and `NOTICE` into both NuGet packages (add them next to the icon and README items
  in `src/Directory.Build.props`), and extend `scripts/Test-Packages.ps1` to assert they are in
  each `.nupkg`.
- Add a header to each file in `src/BricksNet/Backends/Thrift/Generated/` stating that it is
  derived from the TCLIService definitions in `databricks-sql-nodejs` and `databricks-jdbc`
  (Apache-2.0), and that it has been modified. Keep the existing `<auto-generated>` marker.
- Rewrite `src/BricksNet/Backends/Thrift/Generated/README.md` for an outside reader: say what the
  files are, where they came from and how to regenerate them. Remove the remark that "the build
  environment has no Thrift compiler".
- Look for other code that was ported, not just modelled, from the two drivers (the fake-service
  record/replay harness in `tests/BricksNet.IntegrationTests/FakeService` says it mirrors the Java
  driver). Where a file is a close port, give it the same header. Report what you find; do not
  guess.

**Done when.** `./scripts/Test-Packages.ps1` passes with the new assertions and every derived file
names its source.

### 6. CI follow-ups

- Add `persist-credentials: false` to the checkout steps in `build.yml` and `security.yml`, as
  `release.yml` already does.
- `build.yml` and `release.yml` say the `GITHUB_TOKEN` passed to `Test-Packages.ps1` is needed
  "while the repo is private". Once the repository is public, check whether the Source Link check
  still needs it and remove it if not.
- `codecov/codecov-action` is pinned at v4.6.0. Newer major versions exist. Read their migration
  notes before upgrading, and check whether the public repository needs a `CODECOV_TOKEN` secret.
- `security.yml` pins `dependency-check/Dependency-Check_Action` to a commit on `main`, because
  the project's tags are years old. Dependabot cannot update a branch pin, so note in the workflow
  that it is refreshed by hand.
- Add a Windows leg to the build matrix (`windows-latest`). The OAuth PKCE flow starts a local
  listener and launches a browser, and that code is platform-sensitive.
- Consider a CodeQL workflow for C#. It is free for public repositories.

**Done when.** The workflows pass on a pull request, on both operating systems.

### 7. OAuth client credentials (M2M) (optional, blocked on D5)

**Why.** `AuthType` has only `Pat` and `OAuthPkce`. PKCE needs a browser, so a service or a CI job
can only authenticate with a personal access token today. Databricks recommends service principals
with OAuth client credentials for unattended workloads, and users of an ADO.NET or EF Core
provider will ask for it early.

**What.** Add `AuthType.OAuthClientCredentials`: an `IAuthProvider` that posts
`grant_type=client_credentials` with scope `all-apis` to the workspace's `/oidc/v1/token` endpoint,
wrapped in the existing `CachedTokenProvider`. Add `ClientId` and `ClientSecret` connection-string
keywords, and treat `ClientSecret` as a secret everywhere `Token` is (logging, `ToString`,
`PersistSecurityInfo`-style behaviour). Check the endpoint and scope against the current Databricks
documentation before writing code. Add unit tests in the style of
`tests/BricksNet.Tests/Auth/OAuthPkceAuthProviderTests.cs`, then document it in both READMEs and
`docs/EFCORE.md`. This needs a live test against a real service principal, which the maintainer
must run.

### 8. Publication checklist (maintainer, with the agent's help on request)

Run in this order, after tasks 1 to 6.

1. Scan the full history for secrets with a dedicated tool (`gitleaks detect --log-opts="--all"`).
   A pattern search for Databricks tokens and real workspace hostnames has already come back
   clean, but that is not a substitute.
2. Re-run the build, the offline tests, `./scripts/Test-Packages.ps1`, and the live tests.
3. Publish as described in task 1.
4. In the repository settings: enable **private vulnerability reporting** (`SECURITY.md` and the
   issue-template link depend on it), secret scanning with push protection, and Dependabot alerts.
   Protect `main` (require a pull request and the build check). Create the `nuget` environment
   with required reviewers, as `docs/RELEASING.md` describes.
5. Set the repository description and topics (`databricks`, `dotnet`, `ado-net`, `efcore`,
   `dapper`).
6. Tag the first preview following `docs/RELEASING.md`. After the packages are on nuget.org,
   uncomment the NuGet badge in `README.md` and replace the "not on nuget.org yet" note in its
   Installation section.
7. Delete this file.

## Reporting back

At the end of each session, tell the maintainer: which tasks are finished, what the build and
test commands printed, which tasks still need a live test run, and any decision you need from
them. If you found something this plan got wrong, say so and say what you did instead.
