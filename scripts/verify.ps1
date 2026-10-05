<#
.SYNOPSIS
    Builds the solution and runs the chosen test projects, printing only a compact summary.

.DESCRIPTION
    Meant for agents and humans alike: the full MSBuild and test-host output is kept out of the
    console (and out of an agent's context window). On failure it prints the distinct compiler
    errors, or the names of the failing tests with the first lines of each failure message.

    Integration and E2E tests start the Aspire AppHost, which needs a container runtime. When
    Docker is not on PATH but Podman is, the script selects Podman and starts its machine.

.EXAMPLE
    ./scripts/verify.ps1                         # build + unit + web (bUnit) tests
    ./scripts/verify.ps1 -Tests unit -Class '*CommentTextValidatorTests'
    ./scripts/verify.ps1 -Tests integration -Class '*CommentsContractTests'
    ./scripts/verify.ps1 -Tests all
    ./scripts/verify.ps1 -BuildOnly
#>
param(
    # unit, web, integration, e2e or all.
    [ValidateSet('unit', 'web', 'integration', 'e2e', 'all')]
    [string[]] $Tests = @('unit', 'web'),

    # xUnit v3 class filter (wildcards allowed), passed as --filter-class.
    [string] $Class,

    # Skip the build (only when nothing changed since the last one).
    [switch] $NoBuild,

    # Build only, run no tests.
    [switch] $BuildOnly,

    # Maximum number of failing tests printed in detail.
    [int] $MaxFailures = 15
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# English, stable output that the patterns below can match.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$projects = [ordered]@{
    unit        = 'tests/Taskify.UnitTests'
    web         = 'tests/Taskify.Web.Tests'
    integration = 'tests/Taskify.IntegrationTests'
    e2e         = 'tests/Taskify.E2ETests'
}
if ($Tests -contains 'all') { $Tests = @('unit', 'web', 'integration') }

$failed = $false

if (-not $NoBuild) {
    $buildOut = & dotnet build Taskify.slnx -warnaserror -v q --nologo '-clp:ErrorsOnly;NoSummary' 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Output 'BUILD: FAILED'
        $buildOut | ForEach-Object { "$_" } |
            Where-Object { $_ -match '(error|warning) [A-Z]+\d+' } |
            ForEach-Object { ($_ -replace '\s+\[[^\]]+\.csproj\]$', '').Trim() } |
            Select-Object -Unique -First 40
        exit 1
    }
    Write-Output 'BUILD: OK'
}
if ($BuildOnly) { exit 0 }

if (($Tests -contains 'integration') -or ($Tests -contains 'e2e')) {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    $podman = Get-Command podman -ErrorAction SilentlyContinue
    if (-not $docker -and $podman) {
        $env:ASPIRE_CONTAINER_RUNTIME = 'podman'
        $running = & podman machine list --format '{{.Running}}' 2>$null
        if (-not ($running -contains 'true')) {
            Write-Output 'CONTAINERS: starting the Podman machine...'
            & podman machine start *> $null
        }
    }
    elseif (-not $docker) {
        Write-Output 'CONTAINERS: neither Docker nor Podman found; integration/E2E tests cannot run.'
        exit 1
    }
}

foreach ($name in $Tests) {
    $path = $projects[$name]
    $testArgs = @('test', '--project', $path, '--no-build', '--no-progress')
    if ($Class) { $testArgs += @('--filter-class', $Class) }

    $out = & dotnet @testArgs 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE

    $summary = $out | Where-Object { $_ -match '^\s*(total|failed|succeeded|skipped):\s*\d+' } |
        ForEach-Object { $_.Trim() }
    $status = if ($code -eq 0) { 'OK' } else { 'FAILED' }
    Write-Output ("TESTS {0}: {1}  [{2}]" -f $name, $status, ($summary -join ', '))

    if ($code -ne 0) {
        $failed = $true
        $shown = 0
        for ($i = 0; $i -lt $out.Count -and $shown -lt $MaxFailures; $i++) {
            if ($out[$i] -match '^\s*failed (.+?) \(\d') {
                $shown++
                Write-Output ("  x {0}" -f $Matches[1])
                # The failure message follows the "from <dll>" line; keep its first lines only.
                $detail = $out[($i + 1)..([Math]::Min($i + 6, $out.Count - 1))] |
                    Where-Object { $_ -notmatch '^\s*from ' -and $_ -notmatch '^\s*at ' -and $_.Trim() } |
                    Select-Object -First 3
                $detail | ForEach-Object { '      ' + $_.Trim() }
            }
        }
        if (-not $summary) {
            # No summary at all usually means the host or a fixture crashed before running tests.
            $out | Where-Object { $_ -match 'error|exception' } | Select-Object -Unique -First 10 |
                ForEach-Object { '  ' + $_.Trim() }
        }
    }
}

if ($failed) { exit 1 }
exit 0
