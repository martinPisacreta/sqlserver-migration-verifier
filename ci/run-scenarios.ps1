# Runs the four README scenarios against a throwaway LocalDB instance and checks the
# verifier's exit code for each one: 0 (equal), 1 (missing row), 1 (changed row), 1 (extra row).
# Used by .github/workflows/ci.yml on windows-latest; it also runs on a Windows PC with LocalDB.
# It creates its own LocalDB instance and deletes it at the end. It never touches another instance.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$instance = 'MigVerifierCI' + (Get-Random -Maximum 99999)
$server = "(localdb)\$instance"
$temp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$out = Join-Path $temp "migration-verifier-$instance"
New-Item -ItemType Directory -Path $out | Out-Null

$expected = @{ 1 = 0; 2 = 1; 3 = 1; 4 = 1 }
$labels = @{ 1 = 'all equal'; 2 = 'one missing row'; 3 = 'NULL changed to empty text'; 4 = 'one extra row' }
$rows = @()
$failed = $false

sqllocaldb create $instance -s | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Error "sqllocaldb create exited with $LASTEXITCODE" }

try {
    sqlcmd -S $server -E -b -i (Join-Path $repo 'setup.sql') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "setup.sql exited with $LASTEXITCODE" }

    $connection = 'Server={0};Database={1};Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
    $env:MIG_SOURCE_CONNECTION = $connection -f $server, 'MigrationVerifierSource'
    $env:MIG_TARGET_CONNECTION = $connection -f $server, 'MigrationVerifierTarget'

    $template = Get-Content (Join-Path $repo 'scenarios.sql') -Raw
    $selector = 'DECLARE @Scenario int = 1;'
    if (-not $template.Contains($selector)) { throw 'scenarios.sql no longer contains the scenario selector line.' }

    foreach ($n in 1..4) {
        $file = Join-Path $out "scenario-$n.sql"
        Set-Content -Path $file -Value $template.Replace($selector, "DECLARE @Scenario int = $n;") -Encoding UTF8
        sqlcmd -S $server -E -b -i $file | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "scenario $n script exited with $LASTEXITCODE" }

        dotnet run -c Release --no-build -- dbo.Customers dbo.CustomerContacts | Out-Host
        $actual = $LASTEXITCODE
        $ok = $actual -eq $expected[$n]
        if (-not $ok) { $failed = $true }
        $rows += '| {0}: {1} | {2} | {3} | {4} |' -f $n, $labels[$n], $expected[$n], $actual, $(if ($ok) { 'PASS' } else { 'FAIL' })

        $report = Join-Path $repo 'migration-report.md'
        if (Test-Path $report) { Move-Item $report (Join-Path $out "scenario-$n-report.md") -Force }
    }
}
finally {
    # Dropping the two sample databases removes their files; they exist only in this instance.
    foreach ($name in 'MigrationVerifierSource', 'MigrationVerifierTarget') {
        sqlcmd -S $server -E -Q "IF DB_ID(N'$name') IS NOT NULL BEGIN ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name]; END" | Out-Null
    }
    sqllocaldb stop $instance -k | Out-Null
    sqllocaldb delete $instance | Out-Null
}

$summary = @(
    '### Four scenarios on a throwaway LocalDB instance',
    '',
    '| Scenario | Expected exit code | Actual exit code | Result |',
    '|---|---|---|---|'
) + $rows
$summary | Out-Host
if ($env:GITHUB_STEP_SUMMARY) {
    $summary | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
    foreach ($n in 1..4) {
        $saved = Join-Path $out "scenario-$n-report.md"
        if (Test-Path $saved) {
            @('', "<details><summary>Report written for scenario $n</summary>", '') | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
            Get-Content $saved | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
            @('', '</details>') | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding UTF8
        }
    }
}

if ($failed) { Write-Host 'At least one scenario returned an unexpected exit code.'; exit 1 }
Write-Host 'All four scenarios returned the expected exit codes.'
exit 0
