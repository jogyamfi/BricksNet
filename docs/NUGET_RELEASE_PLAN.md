# NuGet Release Plan

Plan for publishing `BricksNet` and `BricksNet.EntityFrameworkCore` to nuget.org, and for the CI
needed to keep their dependencies and security posture current afterwards.

## Current state (audited 2026-11-09)

Already in place:

- Package metadata in both `.csproj` files (ID, authors, description, SPDX license, readme).
- Source Link (`Microsoft.SourceLink.GitHub`), `.snupkg` symbols, deterministic + CI builds.
- Central Package Management (`Directory.Packages.props`), SDK pinned by `global.json`.
- `build.yml` packs both projects on `main` and uploads them as a workflow artifact (not published).
- `security.yml`: weekly `dotnet list package --vulnerable` + OWASP Dependency-Check.
- Test, integration and sample projects are `IsPackable=false`.
- Both package IDs are **unclaimed** on nuget.org.

Gaps and blockers:

| # | Gap | Impact |
|---|-----|--------|
| B1 | GitHub repo is **private** | Source Link, project URL, and README links are dead for consumers |
| B2 | No `LICENSE` file, though packages declare `Apache-2.0` | Licence claim with no licence text in the repo |
| B3 | `PackageProjectUrl` / `RepositoryUrl` = `github.com/your-org/bricksnet` | Broken links on the nuget.org page |
| G1 | `<Version>` hard-coded in each `.csproj` | Two places to bump; easy to ship mismatched versions |
| G2 | Core package has no `PackageTags` / icon | Poor discoverability |
| G3 | Repo `README.md` packed as the package readme | Relative links/images break on nuget.org |
| G4 | No release/publish workflow | Publishing would be manual, with a long-lived API key |
| G5 | No Dependabot, no lock files | Dependency updates are manual; transitive drift undetected |
| G6 | Actions pinned by tag; OWASP action pinned to `@main` | Supply-chain risk in CI |
| G7 | No workflow-level `permissions:` | `GITHUB_TOKEN` gets default (broad) scopes |
| G8 | Test packages behind (`Microsoft.NET.Test.Sdk` 17.12, `xunit.runner.visualstudio` 2.8.2) | Will be picked up by Dependabot in Phase 5 |

> **Note:** nuget.org versions are **immutable and cannot be deleted**, only unlisted. Use prerelease
> versions (`0.1.0-preview.N`) until the public API is something you're willing to support.

---

## Sequencing: the repo stays private until just before the first publish

The repo is tidied up before it goes public, so the phases run in this order:

1. Phase 0 (except going public), then Phases 1–3. All of these work on a private repo, including
   creating the nuget.org trusted-publishing policy and the `nuget` environment.
2. The private-friendly parts of Phase 5: 5.1 Dependabot, 5.2 NuGet Audit, 5.3 lock files,
   5.4 workflow hardening, 5.8 scheduled build, 5.9 SBOM.
3. **Tidy-up and go public** (see Phase 0, step 1).
4. The public-only parts of Phase 5: 5.5 dependency review, 5.6 CodeQL, 5.7 secret scanning and push
   protection, 5.10 Scorecard. These are free on public repos only; a private personal repo needs
   GitHub Advanced Security.
5. Phase 4, the first publish.

While the repo is private, Source Link stepping in the Phase 2 smoke test only works if the debugger
is signed in to GitHub. `dotnet validate` and package inspection are unaffected.

## Phase 0 — Prerequisites and decisions

Mostly account and repo settings, outside the codebase.

1. **Make the GitHub repo public** (B1) just before the first publish, after the tidy-up:
   - Making the repo public publishes the **full history**, not just the current files. Scan it with
     `gitleaks detect` (or `git log -p`). Check `scripts/live-tests.env`, `coverage-out/` and any old
     fixtures.
   - If anything must go, rewrite the history with `git filter-repo` **before the first `v*` tag**.
     A rewrite changes every commit SHA, which breaks tags and the commit SHAs recorded in packages.
     If a real credential was ever committed, revoke it too; rewriting history isn't enough.
   - Then flip the visibility, enable the public-only features (see Sequencing), and publish.
2. **Add `LICENSE`** (Apache-2.0 full text) at the repo root (B2). Optionally add a `NOTICE` file.
3. **Fix URLs** (B3) to `https://github.com/jogyamfi/BricksNet`.
4. **nuget.org account**: sign in with a Microsoft account, enable 2FA.
5. **(Optional) Reserve the `BricksNet` ID prefix**: email `account@nuget.org`. This gives a
   verified badge and blocks look-alike `BricksNet.*` packages.
6. **Decide the versioning scheme**: lockstep (both packages share one version). Recommended, since the
   EF package depends on the exact core version. First release: `0.1.0-preview.1`.
7. **Add `SECURITY.md`** and enable *Private vulnerability reporting* in repo settings.

**Exit criteria:** repo public with licence, URLs fixed, nuget.org account with 2FA.

---

## Phase 1 — Package metadata hardening

1. **Centralise shared metadata** in a new `src/Directory.Build.props`. It applies only to the shipped
   libraries, not tests or samples:

   ```xml
   <Project>
     <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
     <PropertyGroup>
       <Authors>James Gyamfi</Authors>
       <Company>JamesGyamfi</Company>
       <Copyright>Copyright (c) James Gyamfi</Copyright>
       <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
       <PackageProjectUrl>https://github.com/jogyamfi/BricksNet</PackageProjectUrl>
       <RepositoryUrl>https://github.com/jogyamfi/BricksNet</RepositoryUrl>
       <RepositoryType>git</RepositoryType>
       <PackageIcon>icon.png</PackageIcon>
       <PackageReleaseNotes>https://github.com/jogyamfi/BricksNet/blob/main/CHANGELOG.md</PackageReleaseNotes>
       <IncludeSymbols>true</IncludeSymbols>
       <SymbolPackageFormat>snupkg</SymbolPackageFormat>
       <EmbedUntrackedSources>true</EmbedUntrackedSources>
       <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
       <!-- Fails pack on package-shape mistakes; add a baseline after the first stable release. -->
       <EnablePackageValidation>true</EnablePackageValidation>
     </PropertyGroup>
     <ItemGroup>
       <None Include="$(MSBuildThisFileDirectory)../assets/icon.png" Pack="true" PackagePath="\" />
       <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
     </ItemGroup>
   </Project>
   ```

   Then remove the duplicated properties from both `.csproj` files.
2. **Version from git tags (G1)**: add `MinVer` (`PrivateAssets="all"`) to `src/Directory.Build.props`
   and delete `<Version>` from both projects. Tag `v0.1.0-preview.1` → package `0.1.0-preview.1`.
   Untagged builds get `0.1.0-alpha.0.N` automatically. Set `<MinVerTagPrefix>v</MinVerTagPrefix>`.
3. **Discoverability (G2)**: add `PackageTags` to the core package
   (`databricks;sql;ado.net;spark;delta;arrow;thrift`) and a 128×128 `assets/icon.png`.
4. **Package-specific readmes (G3)**: add `src/BricksNet/README.md` and
   `src/BricksNet.EntityFrameworkCore/README.md`. Keep them short: install, a minimal example, a link
   to the repo docs. Use absolute URLs only. Point `PackageReadmeFile` at them.
5. **Update `.github/instructions/nuget-packaging.instructions.md`** to match. Version now comes from
   MinVer, not a `Version` property.
6. *(Optional, before 1.0)*: add `Microsoft.CodeAnalysis.PublicApiAnalyzers` to track public API
   changes, and decide when to lift the `CS1591` suppression (the instructions file says the library
   should not suppress it).

**Exit criteria:** `dotnet pack` produces both packages with one version, an icon, a readme and
correct URLs.

---

## Phase 2 — Local pack and verification

> **Implemented** as [`scripts/Test-Packages.ps1`](../scripts/Test-Packages.ps1), which automates the
> steps below. It uses `Meziantou.Framework.NuGetPackageValidation.Tool` (pinned in
> `dotnet-tools.json`) instead of `dotnet-validate`, which has had only one release. CI runs it on
> every PR. See [RELEASING.md](RELEASING.md).

1. Pack:
   ```powershell
   dotnet pack BricksNet.sln -c Release -o ./nupkgs
   ```
2. **Inspect** each `.nupkg` with NuGet Package Explorer, or unzip it. Check:
   - `lib/net10.0/` contains the `.dll` and `.xml` docs, and nothing else unexpected.
   - The dependency list has no test packages, `SourceLink`, or `MinVer`.
   - `BricksNet.EntityFrameworkCore` depends on `BricksNet` at the same version, and on
     `Microsoft.EntityFrameworkCore.Relational [10.0.12, 11.0.0)`.
   - Licence, icon, readme, and repository commit metadata are present.
3. **Validate Source Link and determinism:**
   ```powershell
   dotnet tool install -g dotnet-validate
   dotnet validate package local ./nupkgs/BricksNet.<ver>.nupkg
   ```
4. **Consumer smoke test** from a local feed, in a throwaway console app outside the repo:
   ```powershell
   dotnet new console -o $env:TEMP\bricksnet-smoke; cd $env:TEMP\bricksnet-smoke
   dotnet nuget add source C:\DevelopmentRep\BricksNet\nupkgs -n local-bricksnet
   dotnet add package BricksNet --prerelease
   dotnet add package BricksNet.EntityFrameworkCore --prerelease
   ```
   Run one query against a live workspace, then step into BricksNet code in the debugger to confirm
   Source Link works.
5. Add `nupkgs/` to `.gitignore` if it isn't already there.

**Exit criteria:** both packages validate cleanly and work from a local feed.

---

## Phase 3 — Release workflow (CI publishing)

> **Implemented** in [`.github/workflows/release.yml`](../.github/workflows/release.yml). The
> nuget.org and GitHub settings below are manual; see [RELEASING.md](RELEASING.md#one-time-setup).
> Differences from the sketch below: the nuget.org username comes from the `NUGET_USER` repo
> variable; the tag must be a version tag on a commit in `main`; the package version is checked
> against the tag; and the attestation step is skipped while the repo is private. `--locked-mode`
> is left out until lock files are added (Phase 5.3).

Publish on a **git tag push**, using **NuGet Trusted Publishing** (GitHub OIDC). This means no
long-lived API key is stored anywhere.

1. **On nuget.org**: *Account → Trusted Publishing → Add policy*. Set owner `jogyamfi`, repo
   `BricksNet`, workflow `release.yml`, environment `nuget`.
   - *Fallback if not using trusted publishing:* create an API key scoped to **Push only**, glob
     `BricksNet*`, with a 365-day expiry. Store it as the `NUGET_API_KEY` secret on the `nuget`
     environment, and add a calendar reminder to rotate it.
2. **In GitHub**: *Settings → Environments → `nuget`*. Add yourself as a **required reviewer** so
   every publish needs a manual approval. Restrict the environment to tags `v*`.
3. **Add `.github/workflows/release.yml`:**

   ```yaml
   name: Release
   on:
     push:
       tags: [ "v*" ]

   permissions:
     contents: read

   jobs:
     build:
       runs-on: ubuntu-latest
       steps:
         - uses: actions/checkout@<sha> # v4
           with: { fetch-depth: 0 }      # MinVer + Source Link need full history
         - uses: actions/setup-dotnet@<sha> # v4
           with: { global-json-file: global.json }
         - run: dotnet restore BricksNet.sln --locked-mode
         - run: dotnet build BricksNet.sln -c Release --no-restore
         - run: dotnet test BricksNet.sln -c Release --no-build --filter "Category!=E2E"
         - run: dotnet pack BricksNet.sln -c Release --no-build -o nupkgs
         - uses: actions/upload-artifact@<sha> # v4
           with: { name: nupkgs, path: nupkgs/ }

     publish:
       needs: build
       runs-on: ubuntu-latest
       environment: nuget            # manual approval gate
       permissions:
         id-token: write             # OIDC for NuGet trusted publishing + attestations
         contents: write             # create the GitHub Release
         attestations: write
       steps:
         - uses: actions/download-artifact@<sha> # v4
           with: { name: nupkgs, path: nupkgs }
         - uses: actions/attest-build-provenance@<sha> # v2
           with: { subject-path: "nupkgs/*.nupkg" }
         - uses: NuGet/login@<sha> # v1
           id: nuget
           with: { user: <your-nuget.org-username> }
         - run: >
             dotnet nuget push "nupkgs/*.nupkg"
             --api-key ${{ steps.nuget.outputs.NUGET_API_KEY }}
             --source https://api.nuget.org/v3/index.json
             --skip-duplicate
           # .snupkg files next to the .nupkg are pushed to the symbol server automatically.
         - run: gh release create "$GITHUB_REF_NAME" nupkgs/* --generate-notes $PRERELEASE_FLAG
           env:
             GH_TOKEN: ${{ github.token }}
             PRERELEASE_FLAG: ${{ contains(github.ref_name, '-') && '--prerelease' || '' }}
   ```

4. **Change `build.yml`**: keep the pack step as a PR/main sanity check, but switch it to
   `dotnet pack BricksNet.sln` so new packable projects are picked up automatically.

**Exit criteria:** pushing a tag builds, tests and packs, waits for approval, then publishes.

---

## Phase 4 — First publish

1. Update `CHANGELOG.md`: move `[Unreleased]` to `[0.1.0-preview.1] - <date>`.
2. Tag and push:
   ```powershell
   git tag v0.1.0-preview.1; git push origin v0.1.0-preview.1
   ```
3. Approve the `nuget` environment deployment in the Actions tab.
4. Verify on nuget.org. Validation and indexing can take from a few minutes to about an hour.
   - Both packages are listed, the readme renders, and the icon, licence and repo link show.
   - The *Source Link* and *Deterministic* health indicators are green, and symbols are available.
5. Install from nuget.org into the smoke-test app (Phase 2, step 4, with the local source removed).
6. Add NuGet version badges to the repo `README.md`.

**Exit criteria:** packages are installable from nuget.org with working symbols and Source Link.

---

## Phase 5 — CI for dependencies and security updates

Ordered by value. Items 1–6 are recommended before or alongside the first release. The rest can
follow.

### 5.1 Dependabot version and security updates

Add `.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"                     # reads Directory.Packages.props
    schedule: { interval: "weekly", day: "monday" }
    open-pull-requests-limit: 10
    groups:
      dotnet-platform:                 # ship the 10.0.x patch train together
        patterns: [ "Microsoft.Extensions.*", "Microsoft.EntityFrameworkCore*",
                    "System.Diagnostics.DiagnosticSource", "Microsoft.AspNetCore.*" ]
      test-tooling:
        patterns: [ "xunit*", "Microsoft.NET.Test.Sdk", "coverlet.*", "Moq", "RichardSzalay.*" ]
      build-tooling:
        patterns: [ "Microsoft.SourceLink.*", "MinVer" ]
    ignore:
      # The EF provider uses EF internal APIs; majors (11.x) are a deliberate porting task.
      - dependency-name: "Microsoft.EntityFrameworkCore*"
        update-types: [ "version-update:semver-major" ]

  - package-ecosystem: "github-actions"
    directory: "/"
    schedule: { interval: "weekly" }
    groups:
      actions: { patterns: [ "*" ] }

  - package-ecosystem: "dotnet-sdk"    # bumps global.json
    directory: "/"
    schedule: { interval: "weekly" }
```

In repo settings, enable **Dependabot alerts** and **Dependabot security updates**. Security PRs are
then raised immediately, outside the weekly schedule.

### 5.2 NuGet Audit at restore time

Make it explicit in the root `Directory.Build.props`:

```xml
<NuGetAudit>true</NuGetAudit>
<NuGetAuditMode>all</NuGetAuditMode>      <!-- include transitive packages -->
<NuGetAuditLevel>low</NuGetAuditLevel>
```

Because `TreatWarningsAsErrors=true`, any `NU1901`–`NU1904` advisory **fails the build**. Every
build is then a vulnerability gate. The `grep` step in `security.yml` becomes a backup and its
report remains useful. If a low-severity advisory blocks you, suppress it per package with
`<NuGetAuditSuppress Include="<advisory url>" />` and a comment, the same way
`dependency-check-suppressions.xml` handles OWASP.

### 5.3 Lock files and locked restore

Add `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` to the root props. Commit the
generated `packages.lock.json` files, and use `dotnet restore --locked-mode` in every workflow. This
gives reproducible restores and catches unexpected transitive changes. Dependabot updates the lock
files in its PRs.

### 5.4 Harden the workflows themselves

- Add a top-level `permissions: contents: read` to `build.yml` and `security.yml` (G7). Grant more
  per job only where needed.
- **Pin every action to a full commit SHA**, with the version in a comment (G6). Most urgent:
  `dependency-check/Dependency-Check_Action@main` currently runs whatever is on that branch.
  Dependabot (5.1) keeps SHA pins up to date.
- Add `step-security/harden-runner` (audit mode first) to log outbound network calls from CI.

### 5.5 Dependency review on PRs

Add `actions/dependency-review-action` to PRs with `fail-on-severity: moderate` and an allow-list of
licences (`Apache-2.0, MIT, BSD-2-Clause, BSD-3-Clause`). This blocks PRs that introduce vulnerable
or incompatibly licensed packages. Free on public repos; private repos need GitHub Advanced Security.

### 5.6 CodeQL static analysis

Add `github/codeql-action` (`languages: csharp`, `build-mode: manual` using the existing build
command). Run it on PRs, on `main`, and weekly. Free on public repos.

### 5.7 Secret scanning

Enable **secret scanning** and **push protection** in repo settings. This matters given the
live-test env files and the PAT/OAuth support in the library.

### 5.8 Scheduled build

Add a `schedule:` trigger (weekly) to `build.yml`. Combined with 5.2, this surfaces newly published
advisories against unchanged code, and catches upstream breakage from SDK roll-forward
(`rollForward: latestMinor`).

### 5.9 SBOM and provenance on releases

Phase 3 already attests build provenance. Also generate a CycloneDX SBOM in `release.yml`
(`dotnet tool run dotnet-CycloneDX BricksNet.sln -o sbom`) and attach it to the GitHub Release.

### 5.10 OpenSSF Scorecard

Add `ossf/scorecard-action` (weekly, results to code scanning). It checks for regressions in the
items above (pinned deps, token permissions, branch protection) and gives a public badge.

### 5.11 Branch protection on `main`

Require PRs, and require the Build & Test, Security Scan, CodeQL and dependency-review checks to pass.
Block force pushes. Restrict who can push `v*` tags (tag ruleset), since a tag triggers a release.

### 5.12 *(Optional)* Dependabot auto-merge for patches

A small workflow using `dependabot/fetch-metadata` can enable auto-merge for `semver-patch` updates
in the `test-tooling` and `actions` groups once required checks pass. Keep runtime-dependency
updates manual.

### 5.13 *(Optional)* Upstream canary

A monthly job that builds against the newest EF Core / .NET preview (overriding the `<11.0.0` bound
in a scratch branch). This gives early warning of the EF 11 internal-API breakage before it blocks
you.

### 5.14 *(After 1.0)* API compatibility gate

Set `<PackageValidationBaselineVersion>1.0.0</PackageValidationBaselineVersion>` so `dotnet pack`
fails on accidental breaking changes within a major version.

---

## Phase 6 — Release runbook (ongoing)

**Normal release**

1. Merge changes to `main`, with `CHANGELOG.md` updated under `[Unreleased]`.
2. Move `[Unreleased]` to the new version heading.
3. `git tag vX.Y.Z[-preview.N]` and push it.
4. Approve the `nuget` environment, then verify on nuget.org.

**Security patch for a dependency**

1. Merge the Dependabot security PR. The build passing confirms that NuGet Audit is clean.
2. Release a patch version immediately (`vX.Y.Z+1`).
3. If the vulnerability was in BricksNet itself, publish a GitHub Security Advisory (GHSA). Then mark
   the affected versions as deprecated on nuget.org, pointing to the fixed version.

**Bad release**

Versions can't be deleted. *Unlist* the version on nuget.org, mark it *deprecated* (reason:
critical bugs) pointing to the replacement, and ship a fixed version.

**Housekeeping**

- If using an API key instead of trusted publishing, rotate it before expiry.
- Review `dependency-check-suppressions.xml` and any `NuGetAuditSuppress` entries whenever the
  affected package is upgraded.

---

## Checklist

- [ ] **P0** Repo public · LICENSE · URLs fixed · nuget.org account + 2FA · SECURITY.md
- [x] **P1** `src/Directory.Build.props` · MinVer · icon · tags · per-package readmes · package validation
- [x] **P2** Local pack inspected · package validation clean · local-feed smoke test (`scripts/Test-Packages.ps1`)
- [ ] **P3** ~~`release.yml`~~ (done) · trusted publishing policy · `nuget` environment · `NUGET_USER` variable (manual, see RELEASING.md)
- [ ] **P4** `v0.1.0-preview.1` published and verified
- [ ] **P5** Dependabot · NuGet Audit · lock files · SHA-pinned actions + permissions · dependency review · CodeQL · secret scanning · scheduled build · SBOM · Scorecard · branch/tag protection
- [ ] **P6** Runbook adopted
