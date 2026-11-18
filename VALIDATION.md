# Validation on Windows

## 2026-10-27 09:00Z

- Build: `dotnet build -c Release`, net8.0 on the .NET 8.0.32 runtime, Microsoft.Data.SqlClient 7.1.1: 0 warnings, 0 errors.
- Database: temporary SQL Server 2025 LocalDB instance created for the run and deleted afterwards; setup.sql created both sample databases.

| Check | Expected | Result |
|---|---|---|
| Scenario 1: all equal | exit 0, both tables MATCH | PASS |
| Scenario 2: one missing contact | exit 1, missing {AccountId 10, ContactId 2} | PASS |
| Scenario 3: NULL changed to empty text | exit 1, changed {CustomerId 2} | PASS |
| Scenario 4: one extra contact | exit 1, extra {AccountId 10, ContactId 99} | PASS |
| Extra: accent removed in Unicode text and 12.50 changed to 12.51 | exit 1, two changed keys | PASS |
| Extra: 20,000-row table with one changed amount | exit 1, changed {OrderId 15000}, in about 1 second | PASS |

Total: 6 of 6. Not covered: very large tables, types outside the README list, and databases on different servers.

## 2026-11-18 09:01Z

- Build: `dotnet build -c Release` with .NET SDK 10.0.402, net8.0 on the .NET 8.0.32 runtime: 0 warnings, 0 errors.
- Database: temporary SQL Server 2025 LocalDB instance (17.0.4025.3) created for the run and deleted afterwards.
- Scenarios 1 to 4, applied and verified with the README commands: exit codes 0, 1, 1, 1 and the counts and keys listed in docs/EXPECTED-REPORTS.md. The four reports are in docs/reports/.
- Not repeated in this run: the two extra checks of 2026-10-27.
