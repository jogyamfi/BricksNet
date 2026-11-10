---
applyTo: "**/src/**/*.csproj, **/Directory.Build.props, **/Directory.Packages.props"
---

# NuGet Packaging (BricksNet)

These rules apply to project files and shared MSBuild configuration.

## Property Placement

- Put repo-wide MSBuild properties (TFM, nullable, analyzers) in `Directory.Build.props` at the solution root.
- Put packaging properties shared by every shipped package in `src/Directory.Build.props`. It imports the root file and applies only to `src/`, so tests and samples are unaffected.
- Put only package-specific properties (`PackageId`, `Description`, `PackageTags`) in the individual `.csproj`.

  ```xml
  <!-- Directory.Build.props -->
  <Project>
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
      <LangVersion>latest</LangVersion>
      <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
      <AnalysisLevel>latest</AnalysisLevel>
      <Deterministic>true</Deterministic>
    </PropertyGroup>
  </Project>
  ```

## Central Package Management

- Use `Directory.Packages.props` with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`. Declare versions there; project `.csproj` files reference packages **without** a `Version` attribute.

  ```xml
  <!-- Directory.Packages.props -->
  <Project>
    <PropertyGroup>
      <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    </PropertyGroup>
    <ItemGroup>
      <PackageVersion Include="Apache.Arrow" Version="18.0.0" />
    </ItemGroup>
  </Project>
  ```

  ```xml
  <!-- BricksNet.csproj -->
  <PackageReference Include="Apache.Arrow" />
  ```

## Required Packaging Properties

- `src/Directory.Build.props` sets: `Authors`, `Company`, `Copyright`, `PackageLicenseExpression` (SPDX), `PackageProjectUrl`, `RepositoryUrl`, `RepositoryType=git`, `PackageIcon` (`assets/icon.png`), `PackageReadmeFile`, `PackageReleaseNotes`, `EnablePackageValidation`.
- Each packable `.csproj` under `src/` sets `PackageId`, `Description` and `PackageTags`, and has its own `README.md` next to it. Package readmes use absolute URLs only, because nuget.org cannot resolve relative links or images.

## Source Link

- `Microsoft.SourceLink.GitHub` is referenced once, in `src/Directory.Build.props` (with `PrivateAssets="all"`), for debugger source-stepping. It requires `EmbedUntrackedSources=true` and `Deterministic=true`.

  ```xml
  <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
  <PropertyGroup>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
  </PropertyGroup>
  ```

## Symbol Packages

- Set `IncludeSymbols=true` and `SymbolPackageFormat=snupkg`. Publish the `.snupkg` alongside the `.nupkg`.

## XML Documentation

- Set `GenerateDocumentationFile=true` in `BricksNet.csproj`. Suppress `CS1591` (missing XML comment) only in test projects, never in the library.

## Deterministic / CI Builds

- Set `Deterministic=true` unconditionally and `ContinuousIntegrationBuild=true` when the `CI` environment variable is set, for reproducible package hashes.

  ```xml
  <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  ```

## Excluding Content

- Do not embed appsettings, connection strings, or test fixtures in the package. Exclude with:

  ```xml
  <None Include="fixtures/**" Pack="false" />
  ```

## Test Projects

- Test projects must not be packable:

  ```xml
  <IsPackable>false</IsPackable>
  ```

## Versioning

- The version comes from git tags via MinVer (referenced in `src/Directory.Build.props`, `MinVerTagPrefix=v`). **Never set `<Version>` in a project file.** Tag `v0.1.0-preview.1` produces `0.1.0-preview.1` for every package. Untagged commits produce `0.1.0-alpha.0.<height>`.
- All packages ship in lockstep with the same version. `BricksNet.EntityFrameworkCore` depends on the exact `BricksNet` version built alongside it.
- CI checkouts that build or pack need full history (`fetch-depth: 0`), or MinVer cannot find the tags.
