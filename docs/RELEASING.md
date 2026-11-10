# Releasing BricksNet

`BricksNet` and `BricksNet.EntityFrameworkCore` are released together, with the same version, by
[`.github/workflows/release.yml`](../.github/workflows/release.yml) when a `v*` tag is pushed. The
version comes from the tag (MinVer): `v0.1.0-preview.1` → `0.1.0-preview.1`.

Versions on nuget.org are **immutable and cannot be deleted**, only unlisted. Check packages locally
before tagging.

## Check packages locally

```powershell
./scripts/Test-Packages.ps1          # pack + inspect + validate + offline smoke test
./scripts/Test-Packages.ps1 -Live    # ... plus one query via ADO.NET and one via EF Core
```

The script:

1. Packs the solution with `ContinuousIntegrationBuild=true`, so the symbols match a CI build.
2. Inspects each package: the expected files only, one shared version, no build- or test-only
   dependencies, the EF package's dependency on the matching BricksNet version and on EF Core
   `< 11.0.0`, and the licence, icon, readme, URLs and commit metadata.
3. Runs `Meziantou.Framework.NuGetPackageValidation.Tool` (pinned in `dotnet-tools.json`). It checks
   that every source file resolves on GitHub through Source Link, and checks determinism, symbols and
   XML docs. While the repo is private it skips only the "project URL is reachable" rule. It reads
   source files using `GITHUB_TOKEN` or your `gh auth token`.
4. Builds and runs a throwaway consumer app that restores both packages from `nupkgs/` only. It uses
   an isolated package cache, so a stale cached copy of the same version can't be picked up.
   `-Live` reads `scripts/live-tests.env`; the warehouse must be able to run statements.

CI runs the same script (without `-Live`) on every PR and push, and before every release.

**Source Link by hand (optional).** Reference the local package from any app, set a breakpoint on a
BricksNet call, and step into it with *Enable Source Link support* on and *Just My Code* off. Visual
Studio needs to be signed in to GitHub while the repo is private.

## One-time setup

### nuget.org

1. Sign in to nuget.org, and turn on two-factor authentication.
2. **Trusted publishing**: go to *your account → Trusted Publishing → Create*:

   | Field | Value |
   |---|---|
   | Repository owner | `jogyamfi` |
   | Repository | `BricksNet` |
   | Workflow file | `release.yml` |
   | Environment | `nuget` |

   The workflow exchanges a GitHub OIDC token for a short-lived API key, so no long-lived key is stored.
3. *(Optional)* Reserve the `BricksNet` ID prefix by emailing `account@nuget.org`.

### GitHub repository settings

1. **Variable**: *Settings → Secrets and variables → Actions → Variables → New repository
   variable*: `NUGET_USER` = your nuget.org **username** (the profile name, not your email).
2. **Environment**: *Settings → Environments → New environment* `nuget`:
   - *Required reviewers*: add yourself. Every publish then waits for your approval.
   - *Deployment branches and tags*: *Selected branches and tags*, add the tag rule `v*`.
3. **Tag ruleset** (recommended): *Settings → Rules → Rulesets → New tag ruleset*, targeting `v*`.
   Restrict creation, update and deletion to yourself, since pushing a tag starts a release.

### When the repo goes public

- Build provenance attestations start being created automatically. The step is skipped while the
  repo is private.
- `Test-Packages.ps1` starts enforcing the "project URL is reachable" rule automatically.

## Cut a release

1. Make sure `main` is green, and move the `CHANGELOG.md` `[Unreleased]` entries under the new
   version heading.
2. Tag a commit **on `main`**, then push the tag:

   ```powershell
   git checkout main; git pull
   git tag v0.1.0-preview.1
   git push origin v0.1.0-preview.1
   ```

3. The **Release** workflow:
   - Checks that the tag is a version tag and that the commit is on `main`.
   - Restores, builds, runs the non-E2E tests, packs, checks that the package version matches the
     tag, and runs `Test-Packages.ps1`.
   - Waits for approval on the `nuget` environment. Approve it in the Actions run.
   - Attests provenance (public repo only), pushes the `.nupkg` and `.snupkg` files to nuget.org,
     and creates a GitHub Release with the packages and generated notes. A tag with a `-suffix` is
     marked as a prerelease.
4. nuget.org validates and indexes packages before they appear in search. This can take from a few
   minutes to about an hour.

**Re-running a failed release** is safe: the push uses `--skip-duplicate`. If the GitHub Release
already exists, delete it before re-running the publish job.

**A bad release** can't be deleted. Unlist it on nuget.org, mark it deprecated pointing to the fixed
version, and release that version.
