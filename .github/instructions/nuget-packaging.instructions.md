---
applyTo: "**/src/BricksNet/*.csproj, **/Directory.Build.props, **/Directory.Packages.props"
---

# NuGet Packaging (BricksNet)

These rules apply to project files and shared MSBuild configuration.

## Property Placement

- Put all shared MSBuild properties in `Directory.Build.props` at the solution root. Put project-specific properties in the individual `.csproj`.

  ```xml
  <!-- Directory.Build.props -->
  <Project>
    <PropertyGroup>
      <TargetFramework>net8.0</TargetFramework>
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

- `BricksNet.csproj` must set: `PackageId`, `Version`, `Authors`, `Company`, `Description`, `PackageLicenseExpression` (SPDX), `PackageReadmeFile`, `PackageProjectUrl`, `RepositoryUrl`, `RepositoryType=git`.

## Source Link

- Add `Microsoft.SourceLink.GitHub` (with `PrivateAssets="all"`) for debugger source-stepping. It requires `EmbedUntrackedSources=true` and `Deterministic=true`.

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

- Keep `<Version>` in `Directory.Build.props` as the single source of truth; override pre-release with `VersionSuffix` (e.g., `-alpha`, `-beta`). Use MinVer or GitVersion for automated bumping in CI.
