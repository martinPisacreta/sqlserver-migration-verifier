# SQL Server migration verifier

[![Build](https://github.com/martinPisacreta/sqlserver-migration-verifier/actions/workflows/ci.yml/badge.svg)](https://github.com/martinPisacreta/sqlserver-migration-verifier/actions/workflows/ci.yml)

Compare selected tables between two SQL Server databases by complete primary key.
Check row counts, column definitions and server-calculated SHA-256 row hashes.
Receive a Markdown report of missing, extra and changed rows, with up to 20 example keys per category and table.

## Requirements

- .NET 8 SDK; the project references Microsoft.Data.SqlClient 7.1.1. Building restores its package dependencies.
- SQL Server 2016 or later, including compatible LocalDB.
- PowerShell and sqlcmd for the commands below. The example uses the installed LocalDB instance MSSQLLocalDB.
- Permission to create the example databases; the verifier itself needs connection access, SELECT access to all selected data, and visibility of column and primary-key metadata.
- An enabled primary key on every selected table, matching column definitions and primary-key order, and ALLOW_SNAPSHOT_ISOLATION enabled in both databases.

Use an account that sees the intended data: row-level security and dynamic masking can restrict or transform what is compared. Pause writes in both databases before migration acceptance.

## Quick start

Run these commands in PowerShell from the project directory. setup.sql creates MigrationVerifierSource and MigrationVerifierTarget, enables snapshot isolation, and populates two tables in each database. It refuses existing database names and never drops databases. If setup fails partway through, inspect the databases before retrying; provisioning is not atomic.

```powershell
sqlcmd -S '(localdb)\MSSQLLocalDB' -E -b -i .\setup.sql
if ($LASTEXITCODE -ne 0) { throw 'Database setup failed.' }

$env:MIG_SOURCE_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=MigrationVerifierSource;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
$env:MIG_TARGET_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=MigrationVerifierTarget;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'

dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

dotnet run -c Release --no-build -- dbo.Customers dbo.CustomerContacts
$verificationExitCode = $LASTEXITCODE
Get-Content .\migration-report.md
Write-Host "Verifier exit code: $verificationExitCode"
```

The initial expected exit code is 0: both tables contain three matching rows on each side. TrustServerCertificate=true is for this local example; configure trusted certificates and suitable credentials for other environments. Keep connection strings in environment variables.

To apply the missing-row scenario without changing scenarios.sql, replace its selector in a temporary copy and run that copy with sqlcmd:

```powershell
$scenarioSql = (Get-Content .\scenarios.sql -Raw).Replace('DECLARE @Scenario int = 1;', 'DECLARE @Scenario int = 2;')
$scenarioFile = Join-Path $env:TEMP 'migration-verifier-scenario.sql'
Set-Content -Path $scenarioFile -Value $scenarioSql -Encoding ASCII
sqlcmd -S '(localdb)\MSSQLLocalDB' -E -b -i $scenarioFile
if ($LASTEXITCODE -ne 0) { throw 'Scenario execution failed.' }
Remove-Item $scenarioFile

dotnet run -c Release --no-build -- dbo.Customers dbo.CustomerContacts
$verificationExitCode = $LASTEXITCODE
Get-Content .\migration-report.md
Write-Host "Verifier exit code: $verificationExitCode"
```

Expected exit code: 1, with one missing dbo.CustomerContacts row at AccountId=10, ContactId=2. Change the replacement selector to 1, 3 or 4 to try the other cases. Each script execution resets both target example tables from the source before applying one scenario. Run the verifier after each case. The reset deletes target example rows; use these scripts only with the example databases.

docs/EXPECTED-REPORTS.md lists what each of the four scenarios should report, and docs/reports/ holds the reports the verifier actually wrote for them on 2026-11-18 (SQL Server 2025 LocalDB, Windows). VALIDATION.md records both that run and the earlier one of 2026-10-27, which also covered two extra cases.

## Arguments, reports and exit codes

Pass one or more schema.table arguments; comma-separated lists also work. Names must use ASCII letters, digits and underscores, start with a letter or underscore, and have at most 128 characters per component.

- 0: every selected table matches; --help also returns 0.
- 1: row, count or compared schema differences.
- 2: invalid invocation, cancellation or incomplete verification, including SQL, permissions, unsupported types or report-writing errors.

The verifier prints Markdown and replaces migration-report.md in the current directory. Save reports separately to retain multiple scenario runs. Reports include primary-key values, so handle them according to the data they contain.

Missing means present only in source; extra means present only in target. Changed means the same serialized primary key has a different row hash. A changed primary key appears as a missing row and an extra row. Displayed JSON key values are strings. Schema differences report row counts but mark missing, extra and changed as n/a because row hashes were not compared.

Errors produce an incomplete-verification report when it can be saved. If saving fails, an older report may remain. Check the exit code and report timestamp. Connection strings and SQL error details are not emitted.

## Supported types

char, varchar, nchar, nvarchar, bit, tinyint, smallint, int, bigint, decimal, numeric, uniqueidentifier, float, real, money, smallmoney, date, datetime, datetime2, smalldatetime, time, datetimeoffset, binary, varbinary and timestamp/rowversion.

Unsupported types fail explicitly, including xml, text/ntext/image, sql_variant, spatial/CLR types, user-defined alias types and encrypted columns. No column is silently excluded. Supported computed columns are compared by evaluated value; their expressions are not validated. Rowversion bytes are compared literally and commonly differ after migration.

## How comparison works and practical limits

Column metadata and primary-key ordinals are compared first. With matching definitions, SQL Server hashes a framed serialization of every column. NULL and empty values have different frames. Primary keys use complete serialized bytes, including composite-key components. Binary values retain their bytes; other supported values use SQL textual conversions. Case and trailing-space differences remain significant.

This tool is intended for small and medium tables. It scans both sides and retains both key/hash dictionaries in memory, then sorts keys for deterministic examples. Memory grows with row count and key size. There is no batching, streaming merge, checkpoint or resume. Each SQL command has a 180-second timeout; snapshot reads consume resources and can retain row versions.

The two database snapshots are independent, not a coordinated cross-database instant. Pause writes before acceptance. SHA-256 equality is probabilistic evidence. The comparison does not validate omitted tables, foreign keys, indexes, defaults, identity settings, triggers, procedures, permissions or application behavior. Matching the checked metadata is not a complete schema audit.

## Using it as a release check

The exit code makes the verifier usable as a gate: run it after the data load of a migration or release step and stop the pipeline unless it returns 0. ci/run-scenarios.ps1 is a working example. It creates a throwaway LocalDB instance, applies the four scenarios, checks that the verifier exits 0, 1, 1, 1, and deletes the instance.

The included CI workflow builds the project on .NET 8 and runs that script on a Windows runner on every push. The run summary shows the exit-code table and the four reports. That run covers the example databases only; it says nothing about very large tables or databases on different servers.

## Need this done for your migration?

Request verification of your selected SQL Server tables and a report of the observed differences, or the same check wired into your release process as a pass or fail step.
https://www.upwork.com/freelancers/~014a89dccd8bf740d0
