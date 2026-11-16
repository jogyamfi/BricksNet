<#
.SYNOPSIS
    Packs BricksNet and verifies the packages before they are published (see docs/RELEASING.md).

.DESCRIPTION
    Steps, in order (any failure stops the script with a non-zero exit code):

      Pack      dotnet pack the solution with ContinuousIntegrationBuild=true, so symbols hold
                deterministic /_/ paths exactly as in CI. Skipped with -NoPack.
      Inspect   Opens each .nupkg and checks: the expected packages and a .snupkg for each, one
                shared version, only allow-listed files, no build/test-only dependencies, the
                EF package's dependencies, and licence/icon/readme/repository metadata.
      Validate  Runs Meziantou.Framework.NuGetPackageValidation.Tool (from dotnet-tools.json),
                which checks Source Link (every source file must resolve on GitHub), determinism,
                symbols, XML docs and metadata. While the GitHub repo is private, the "URL is
                accessible" rule is skipped; source files are still checked using a GitHub token
                ($env:GITHUB_TOKEN, else `gh auth token`).
      Smoke     Creates a throwaway console app outside the repo, restores both packages from the
                local folder only (an isolated NuGet.config and package cache, so a stale cached
                copy of the same version can never be used), builds it and runs it. Offline, it
                loads both assemblies and builds an EF model. With -Live it also runs a query
                through ADO.NET and through EF Core against the workspace in scripts/live-tests.env
                (or DATABRICKS_* environment variables).

.EXAMPLE
    ./scripts/Test-Packages.ps1                 # pack + all offline checks
    ./scripts/Test-Packages.ps1 -Live           # ... plus a real query against Databricks
    ./scripts/Test-Packages.ps1 -NoPack -PackageDirectory ./nupkgs   # CI: verify already-packed output
#>
[CmdletBinding()]
param(
    # Where packages are written (or read from, with -NoPack).
    [string] $PackageDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'nupkgs'),

    # Verify packages already in -PackageDirectory instead of packing.
    [switch] $NoPack,

    # Skip the consumer smoke test.
    [switch] $SkipSmoke,

    # Also run a query against a real workspace in the smoke test.
    [switch] $Live,

    [string] $EnvFile = (Join-Path $PSScriptRoot 'live-tests.env')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path $PSScriptRoot -Parent
$expectedIds = @('BricksNet', 'BricksNet.EntityFrameworkCore')
$repositoryUrl = 'https://github.com/jogyamfi/BricksNet'

# Packages that must never appear as dependencies of a shipped package.
$forbiddenDependencies = @(
    'MinVer', 'Microsoft.SourceLink.*', 'Microsoft.NET.Test.Sdk', 'xunit*', 'coverlet.*', 'Moq',
    'RichardSzalay.MockHttp', 'Microsoft.AspNetCore.Mvc.Testing', 'Dapper', 'Microsoft.Extensions.Hosting',
    'Microsoft.Extensions.Logging.Console', 'Microsoft.CodeAnalysis.PublicApiAnalyzers')

$failures = New-Object System.Collections.Generic.List[string]
function Fail([string] $message) { $script:failures.Add($message); Write-Host "  FAIL $message" -ForegroundColor Red }
function Pass([string] $message) { Write-Host "  ok   $message" -ForegroundColor DarkGray }
function Step([string] $name) { Write-Host "`n== $name" -ForegroundColor Cyan }
function Stop-IfFailed {
    if ($script:failures.Count -gt 0) {
        Write-Host "`n$($script:failures.Count) check(s) failed." -ForegroundColor Red
        exit 1
    }
}
function Invoke-Native([string] $what, [scriptblock] $command) {
    & $command | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

# ---------------------------------------------------------------- Pack
if (-not $NoPack) {
    Step 'Pack'
    if (Test-Path $PackageDirectory) { Remove-Item -Recurse -Force $PackageDirectory }
    Invoke-Native 'dotnet pack' {
        dotnet pack (Join-Path $repoRoot 'BricksNet.sln') -c Release -o $PackageDirectory `
            -p:ContinuousIntegrationBuild=true --nologo -v:minimal
    }
}

# ---------------------------------------------------------------- Inspect
Step 'Inspect'
$nupkgs = @(Get-ChildItem $PackageDirectory -Filter '*.nupkg' -ErrorAction SilentlyContinue)
if ($nupkgs.Count -eq 0) { throw "No .nupkg files in $PackageDirectory." }

$packages = @{}
foreach ($file in $nupkgs) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
    $metadata = $nuspec.package.metadata
    $packages[$metadata.id] = [pscustomobject]@{ File = $file; Entries = $entries; Metadata = $metadata }
}

$ids = @($packages.Keys | Sort-Object)
if (($ids -join ',') -ne (($expectedIds | Sort-Object) -join ',')) {
    Fail "expected packages [$($expectedIds -join ', ')], found [$($ids -join ', ')]"
}
$versions = @($packages.Values | ForEach-Object { $_.Metadata.version } | Sort-Object -Unique)
if ($versions.Count -ne 1) { Fail "packages have different versions: $($versions -join ', ')" }
$version = $versions[0]
Pass "version $version"

foreach ($id in $ids) {
    $p = $packages[$id]
    $m = $p.Metadata
    Write-Host "  -- $id"

    $snupkg = Join-Path $PackageDirectory "$id.$version.snupkg"
    if (Test-Path $snupkg) { Pass 'symbols package (.snupkg) present' } else { Fail "$id has no .snupkg" }

    # Files: only the library, its XML docs, the readme, the icon, the licence and NOTICE, and NuGet's
    # own bookkeeping.
    $allowed = @(
        "lib/net10.0/$id.dll", "lib/net10.0/$id.xml", 'README.md', 'icon.png', 'LICENSE', 'NOTICE',
        "$id.nuspec", '[Content_Types].xml', '_rels/.rels')
    $unexpected = @($p.Entries | Where-Object {
            $allowed -notcontains $_ -and $_ -notlike 'package/services/metadata/core-properties/*.psmdcp' })
    if ($unexpected.Count -gt 0) { Fail "$id contains unexpected files: $($unexpected -join ', ')" }
    else { Pass 'contents: dll, xml docs, readme, icon, LICENSE, NOTICE only' }
    foreach ($required in "lib/net10.0/$id.dll", "lib/net10.0/$id.xml", 'README.md', 'icon.png', 'LICENSE', 'NOTICE') {
        if ($p.Entries -notcontains $required) { Fail "$id is missing $required" }
    }

    # Metadata.
    if ($m.license.type -ne 'expression' -or $m.license.'#text' -ne 'Apache-2.0') { Fail "$id licence is not the Apache-2.0 expression" }
    if ($m.icon -ne 'icon.png') { Fail "$id icon is not icon.png" }
    if ($m.readme -ne 'README.md') { Fail "$id readme is not README.md" }
    if ($m.projectUrl -ne $repositoryUrl) { Fail "$id projectUrl is '$($m.projectUrl)'" }
    if ($m.repository.url -ne $repositoryUrl) { Fail "$id repository url is '$($m.repository.url)'" }
    if (-not $m.repository.commit) { Fail "$id has no repository commit" }
    if (-not $m.description) { Fail "$id has no description" }
    if (-not $m.tags) { Fail "$id has no tags" }
    Pass "metadata (licence, icon, readme, urls, commit $($m.repository.commit.Substring(0, 7)))"

    # Dependencies.
    $deps = @()
    if ($m.dependencies -and $m.dependencies.group) {
        $deps = @($m.dependencies.group | ForEach-Object { if ($_.PSObject.Properties['dependency']) { $_.dependency } })
    }
    foreach ($dep in $deps) {
        foreach ($pattern in $forbiddenDependencies) {
            if ($dep.id -like $pattern) { Fail "$id depends on build/test-only package $($dep.id)" }
        }
    }
    Pass "dependencies: $(($deps | ForEach-Object { "$($_.id) $($_.version)" }) -join '; ')"

    if ($id -eq 'BricksNet.EntityFrameworkCore') {
        $core = $deps | Where-Object { $_.id -eq 'BricksNet' }
        if (-not $core -or $core.version -ne $version) { Fail "EF package must depend on BricksNet $version" }
        $ef = $deps | Where-Object { $_.id -eq 'Microsoft.EntityFrameworkCore.Relational' }
        if (-not $ef -or $ef.version -notmatch ',\s*11\.0\.0\)$') {
            Fail 'EF package must depend on Microsoft.EntityFrameworkCore.Relational below 11.0.0'
        }
    }
}
Stop-IfFailed

# ---------------------------------------------------------------- Validate
Step 'Validate (Source Link, determinism, symbols, metadata)'
Push-Location $repoRoot
try {
    Invoke-Native 'dotnet tool restore' { dotnet tool restore | Out-Null }

    # The repo URL answers 404 to anonymous requests while the repo is private.
    $repoIsPublic = $true
    try { Invoke-WebRequest -Uri $repositoryUrl -Method Head -UseBasicParsing | Out-Null }
    catch { $repoIsPublic = $false }

    $token = $env:GITHUB_TOKEN
    if (-not $token -and (Get-Command gh -ErrorAction SilentlyContinue)) { $token = (gh auth token 2>$null) }

    $validatorArgs = @($nupkgs.FullName) + @(
        '--only-report-errors',
        # The IDs are ours once published; this rule would fail every release after the first.
        '--excluded-rules', 'PackageIdAvailableOnNuGetOrg')
    if (-not $repoIsPublic) {
        Write-Host '  note repo is private: skipping the project/repository URL reachability rule (52)' -ForegroundColor Yellow
        $validatorArgs += @('--excluded-rule-ids', '52')
        if (-not $token) { throw 'The repo is private, so Source Link checks need a GitHub token: set GITHUB_TOKEN or sign in with `gh auth login`.' }
    }
    if ($token) { $validatorArgs += @('--github-token', $token) }

    $output = dotnet meziantou.validate-nuget-package @validatorArgs 2>&1 | Out-String
    $exit = $LASTEXITCODE
    $report = $null
    try { $report = $output | ConvertFrom-Json } catch { }
    if ($exit -ne 0) {
        if ($report) {
            foreach ($pkg in $report.Packages.PSObject.Properties) {
                foreach ($group in ($pkg.Value.Errors | Group-Object ErrorCode)) {
                    Fail "$(Split-Path $pkg.Name -Leaf): [$($group.Name)] x$($group.Count) $($group.Group[0].Message)"
                }
            }
        }
        else { Fail "validator failed (exit $exit): $output" }
    }
    else { Pass 'all validation rules passed' }
}
finally { Pop-Location }
Stop-IfFailed

# ---------------------------------------------------------------- Smoke
if ($SkipSmoke) { Write-Host "`nAll package checks passed (smoke test skipped)." -ForegroundColor Green; exit 0 }

Step "Smoke test from a local feed$(if ($Live) { ' (live)' })"
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("bricksnet-smoke-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $feed = (Resolve-Path $PackageDirectory).Path
    # Isolated feed + cache: BricksNet* may only come from the local folder.
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config><add key="globalPackagesFolder" value="packages-cache" /></config>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="BricksNet" /><package pattern="BricksNet.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -Path (Join-Path $work 'NuGet.config') -Encoding UTF8

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BricksNet" Version="[$version]" />
    <PackageReference Include="BricksNet.EntityFrameworkCore" Version="[$version]" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $work 'Smoke.csproj') -Encoding UTF8

    @'
using System.Reflection;
using BricksNet;
using BricksNet.AdoNet;
using Microsoft.EntityFrameworkCore;

var host = Environment.GetEnvironmentVariable("DATABRICKS_HOST");
var connectionString = string.IsNullOrEmpty(host)
    ? "Host=example.invalid;HttpPath=/sql/1.0/warehouses/none;Token=none"
    : $"Host={host};HttpPath=/sql/1.0/warehouses/{Environment.GetEnvironmentVariable("DATABRICKS_WAREHOUSE_ID")};" +
      $"Token={Environment.GetEnvironmentVariable("DATABRICKS_TOKEN")};" +
      $"Catalog={(Environment.GetEnvironmentVariable("DATABRICKS_CATALOG") is { Length: > 0 } c ? c : "workspace")}";

// Offline: both assemblies load and the EF provider can build a model.
Console.WriteLine("BricksNet " + typeof(BricksNetClient).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
var options = new DbContextOptionsBuilder<SmokeContext>().UseBricksNet(connectionString).Options;
using (var context = new SmokeContext(options))
{
    Console.WriteLine($"EF model built with {context.Model.GetEntityTypes().Count()} entity type(s).");
}

if (string.IsNullOrEmpty(host))
{
    Console.WriteLine("SMOKE OK (offline)");
    return 0;
}

// Live: one query through ADO.NET and one through EF Core.
await using (var connection = new BricksNetConnection(connectionString))
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1";
    var value = Convert.ToInt32(await command.ExecuteScalarAsync());
    if (value != 1) throw new InvalidOperationException($"ADO.NET returned {value}, expected 1.");
    Console.WriteLine("ADO.NET query OK");
}

await using (var context = new SmokeContext(options))
{
    var value = await context.Database.SqlQuery<int>($"SELECT 42 AS Value").SingleAsync();
    if (value != 42) throw new InvalidOperationException($"EF Core returned {value}, expected 42.");
    Console.WriteLine("EF Core query OK");
}

Console.WriteLine("SMOKE OK (live)");
return 0;

sealed class SmokeContext(DbContextOptions<SmokeContext> options) : DbContext(options)
{
    public DbSet<SmokeRow> Rows => Set<SmokeRow>();
}

sealed class SmokeRow
{
    public long Id { get; set; }
    public string? Name { get; set; }
}
'@ | Set-Content -Path (Join-Path $work 'Program.cs') -Encoding UTF8

    # Credentials only reach the child process through its environment; they are never printed.
    $liveVariables = @('DATABRICKS_HOST', 'DATABRICKS_TOKEN', 'DATABRICKS_WAREHOUSE_ID', 'DATABRICKS_CATALOG')
    $saved = @{}
    foreach ($name in $liveVariables) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
    try {
        if ($Live) {
            if (Test-Path $EnvFile) {
                foreach ($line in Get-Content $EnvFile) {
                    $trimmed = $line.Trim()
                    if ($trimmed -eq '' -or $trimmed.StartsWith('#') -or $trimmed.IndexOf('=') -lt 1) { continue }
                    $key = $trimmed.Substring(0, $trimmed.IndexOf('=')).Trim()
                    if ($liveVariables -contains $key) {
                        [Environment]::SetEnvironmentVariable($key, $trimmed.Substring($trimmed.IndexOf('=') + 1).Trim())
                    }
                }
            }
            foreach ($name in $liveVariables | Where-Object { $_ -ne 'DATABRICKS_CATALOG' }) {
                if (-not [Environment]::GetEnvironmentVariable($name)) { throw "-Live needs $name (set it, or fill in $EnvFile)." }
            }
            $env:DATABRICKS_HOST = $env:DATABRICKS_HOST -replace '^https?://', '' -replace '/+$', ''
        }
        else {
            foreach ($name in $liveVariables) { [Environment]::SetEnvironmentVariable($name, $null) }
        }

        Push-Location $work
        try {
            Invoke-Native 'smoke restore' { dotnet restore --nologo -v:quiet }
            Invoke-Native 'smoke build' { dotnet build --no-restore --nologo -v:quiet -c Release }
            Invoke-Native 'smoke run' { dotnet run --no-build -c Release }
        }
        finally { Pop-Location }
    }
    finally {
        foreach ($name in $liveVariables) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
    Pass "consumer app restored, built and ran against $version"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Write-Host "`nAll package checks passed for $version." -ForegroundColor Green
