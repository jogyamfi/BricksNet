<#
.SYNOPSIS
    Runs the BricksNet live tests against a real Databricks workspace.

.DESCRIPTION
    Reads credentials from scripts/live-tests.env (see live-tests.env.example), checks the token and
    the SQL warehouse, then runs the selected steps. Credentials are passed to the test processes as
    environment variables and are never printed.

    Steps:
      Check    - validate the token and show the warehouse state (always runs first)
      Setup    - run scripts/databricks/01_setup.sql and 02_verify.sql through the Statement Execution API
      Core     - fluent API + ADO.NET live tests, REST and Thrift  (Category=E2E)
      EfCore   - EF Core functional suite, over REST and then Thrift
      Samples  - AdoNetDapper, Di and EfCore sample demos
      Teardown - run scripts/databricks/99_teardown.sql

.EXAMPLE
    ./scripts/Run-LiveTests.ps1 -Steps Setup
    ./scripts/Run-LiveTests.ps1                      # Core, EfCore and Samples
    ./scripts/Run-LiveTests.ps1 -Steps Core -Filter "FullyQualifiedName~LargeResult"
#>
[CmdletBinding()]
param(
    [ValidateSet('Setup', 'Core', 'EfCore', 'Samples', 'Teardown')]
    [string[]] $Steps = @('Core', 'EfCore', 'Samples'),

    # Extra dotnet test filter, combined with the step's own filter.
    [string] $Filter,

    [string] $EnvFile = (Join-Path $PSScriptRoot 'live-tests.env')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

function Read-EnvFile([string] $path) {
    if (-not (Test-Path $path)) {
        throw "Missing $path. Copy scripts/live-tests.env.example to scripts/live-tests.env and fill it in."
    }
    $values = @{}
    foreach ($line in Get-Content $path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        $values[$trimmed.Substring(0, $eq).Trim()] = $trimmed.Substring($eq + 1).Trim()
    }
    foreach ($required in 'DATABRICKS_HOST', 'DATABRICKS_TOKEN', 'DATABRICKS_WAREHOUSE_ID') {
        if (-not $values[$required]) { throw "$required is not set in $path." }
    }
    if (-not $values['DATABRICKS_CATALOG']) { $values['DATABRICKS_CATALOG'] = 'workspace' }
    $values['DATABRICKS_HOST'] = $values['DATABRICKS_HOST'] -replace '^https?://', '' -replace '/+$', ''
    return $values
}

$config = Read-EnvFile $EnvFile
$apiBase = "https://$($config.DATABRICKS_HOST)/api/2.0"
$headers = @{ Authorization = "Bearer $($config.DATABRICKS_TOKEN)" }
$httpPath = "/sql/1.0/warehouses/$($config.DATABRICKS_WAREHOUSE_ID)"

function Invoke-Api([string] $method, [string] $path, $body) {
    $params = @{ Method = $method; Uri = "$apiBase$path"; Headers = $headers; ContentType = 'application/json; charset=utf-8' }
    # Send bytes: Windows PowerShell 5.1 would otherwise encode a string body as ISO-8859-1.
    if ($null -ne $body) { $params.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5)) }
    return Invoke-RestMethod @params
}

# Runs one statement and waits for it to finish. Returns the response, or throws on failure.
function Invoke-Statement([string] $sql, [string] $catalog, [string] $schema) {
    $request = @{ warehouse_id = $config.DATABRICKS_WAREHOUSE_ID; statement = $sql; wait_timeout = '30s'; on_wait_timeout = 'CONTINUE' }
    if ($catalog) { $request.catalog = $catalog }
    if ($schema) { $request.schema = $schema }
    $response = Invoke-Api 'POST' '/sql/statements' $request
    while ($response.status.state -in 'PENDING', 'RUNNING') {
        Start-Sleep -Seconds 3
        $response = Invoke-Api 'GET' "/sql/statements/$($response.statement_id)" $null
    }
    if ($response.status.state -ne 'SUCCEEDED') {
        throw "Statement $($response.status.state): $($response.status.error.message)`n$sql"
    }
    return $response
}

# Runs a .sql file statement by statement. USE CATALOG / USE SCHEMA do not persist between
# Statement Execution API calls, so they are tracked here and sent with each later statement.
function Invoke-SqlFile([string] $path) {
    Write-Host "Running $(Split-Path $path -Leaf)" -ForegroundColor Cyan
    $text = (Get-Content $path -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*--' }) -join "`n"
    $catalog = $null
    $schema = $null
    foreach ($statement in $text -split ';') {
        $sql = $statement.Trim()
        if ($sql -eq '') { continue }
        if ($sql -match '^USE\s+CATALOG\s+`?([^`\s]+)`?$') { $catalog = $Matches[1]; continue }
        if ($sql -match '^USE\s+SCHEMA\s+`?([^`\s]+)`?$') { $schema = $Matches[1]; continue }

        $firstLine = ($sql -split "`n")[0]
        Write-Host "  $firstLine"
        $response = Invoke-Statement $sql $catalog $schema
        if ($response.result.data_array -and $response.manifest.schema.columns) {
            $names = $response.manifest.schema.columns | ForEach-Object { $_.name }
            foreach ($row in $response.result.data_array) {
                $cells = for ($i = 0; $i -lt $names.Count; $i++) { "$($names[$i])=$($row[$i])" }
                Write-Host "    $($cells -join '  ')"
            }
        }
    }
}

function Invoke-DotNetTest([string] $project, [string] $stepFilter) {
    $filters = @($stepFilter, $Filter) | Where-Object { $_ }
    $arguments = @('test', (Join-Path $repoRoot $project), '--logger', 'console;verbosity=normal')
    if ($filters) { $arguments += @('--filter', ($filters -join '&')) }
    # Windows PowerShell 5.1 turns native stderr lines into errors; under 'Stop' the first failing
    # test would abort the script before dotnet prints its details. Rely on the exit code instead.
    $ErrorActionPreference = 'Continue'
    & dotnet @arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { $script:failures += $project }
}

# --- Check ----------------------------------------------------------------------------------
Write-Host "Workspace: $($config.DATABRICKS_HOST)   Warehouse: $($config.DATABRICKS_WAREHOUSE_ID)" -ForegroundColor Cyan
try {
    $me = Invoke-Api 'GET' '/preview/scim/v2/Me' $null
    Write-Host "Token OK, authenticated as $($me.userName)"
    $warehouse = Invoke-Api 'GET' "/sql/warehouses/$($config.DATABRICKS_WAREHOUSE_ID)" $null
    Write-Host "Warehouse '$($warehouse.name)' is $($warehouse.state)"
    if ($warehouse.state -ne 'RUNNING') {
        Write-Host 'Starting the warehouse (the first statement would also start it)...'
        Invoke-Api 'POST' "/sql/warehouses/$($config.DATABRICKS_WAREHOUSE_ID)/start" @{} | Out-Null
    }
}
catch {
    throw "Workspace check failed: $($_.Exception.Message). Check DATABRICKS_HOST, DATABRICKS_TOKEN and DATABRICKS_WAREHOUSE_ID."
}

# Environment for the test projects and samples (child processes inherit it).
$env:BRICKSNET_E2E_HOST = $config.DATABRICKS_HOST
$env:BRICKSNET_E2E_TOKEN = $config.DATABRICKS_TOKEN
$env:BRICKSNET_E2E_HTTP_PATH = $httpPath
$env:BRICKSNET_E2E_THRIFT_PATH = $httpPath
$env:BRICKSNET_E2E_CATALOG = $config.DATABRICKS_CATALOG
$env:BRICKSNET_EF_HOST = $config.DATABRICKS_HOST
$env:BRICKSNET_EF_TOKEN = $config.DATABRICKS_TOKEN
$env:BRICKSNET_EF_HTTPPATH = $httpPath
$env:BRICKSNET_EF_CATALOG = $config.DATABRICKS_CATALOG
$env:Databricks__Host = $config.DATABRICKS_HOST
$env:Databricks__Token = $config.DATABRICKS_TOKEN
$env:Databricks__HttpPath = $httpPath
$env:Databricks__Catalog = $config.DATABRICKS_CATALOG

$failures = @()
try {
    foreach ($step in $Steps) {
        Write-Host "`n=== $step ===" -ForegroundColor Yellow
        switch ($step) {
            'Setup' {
                Invoke-SqlFile (Join-Path $PSScriptRoot 'databricks/01_setup.sql')
                Invoke-SqlFile (Join-Path $PSScriptRoot 'databricks/02_verify.sql')
            }
            'Core' {
                Invoke-DotNetTest 'tests/BricksNet.IntegrationTests' 'Category=E2E'
            }
            'EfCore' {
                $env:BRICKSNET_EF_USETHRIFT = 'false'
                Invoke-DotNetTest 'tests/BricksNet.EntityFrameworkCore.FunctionalTests' $null
                $env:BRICKSNET_EF_USETHRIFT = 'true'
                Invoke-DotNetTest 'tests/BricksNet.EntityFrameworkCore.FunctionalTests' $null
            }
            'Samples' {
                foreach ($demo in 'AdoNetDapper', 'Di', 'EfCore') {
                    Write-Host "--- sample: $demo" -ForegroundColor Cyan
                    $ErrorActionPreference = 'Continue'
                    & dotnet run --project (Join-Path $repoRoot 'samples/BricksNet.Samples') -- $demo 2>&1 | ForEach-Object { "$_" }
                    $ErrorActionPreference = 'Stop'
                    if ($LASTEXITCODE -ne 0) { $failures += "sample $demo" }
                }
            }
            'Teardown' {
                Invoke-SqlFile (Join-Path $PSScriptRoot 'databricks/99_teardown.sql')
            }
        }
    }
}
finally {
    # Do not leave the token in this PowerShell session's environment.
    Get-ChildItem env: | Where-Object { $_.Name -like 'BRICKSNET_E*' -or $_.Name -like 'Databricks__*' } |
        ForEach-Object { Remove-Item "env:$($_.Name)" }
}

if ($failures) {
    Write-Host "`nFailed: $($failures -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "`nAll selected steps passed." -ForegroundColor Green
