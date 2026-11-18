# Expected scenario reports

The table lists the EXPECTED result of each scenario, deduced from setup.sql and scenarios.sql. The section "Actual run" at the end compares it with the reports the verifier wrote on 2026-11-18.

Each scenario first restores both target tables from the source. The expectations below assume the source still contains the setup.sql example data and both tables are selected. Missing means source-only; extra means target-only.

| Scenario | Process exit | Table | Source rows | Target rows | Missing | Extra | Changed | Example key |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 1 — Equal | 0 | dbo.Customers | 3 | 3 | 0 | 0 | 0 | None |
| 1 — Equal | 0 | dbo.CustomerContacts | 3 | 3 | 0 | 0 | 0 | None |
| 2 — Missing | 1 | dbo.Customers | 3 | 3 | 0 | 0 | 0 | None |
| 2 — Missing | 1 | dbo.CustomerContacts | 3 | 2 | 1 | 0 | 0 | Missing: {"AccountId":"10","ContactId":"2"} |
| 3 — Changed | 1 | dbo.Customers | 3 | 3 | 0 | 0 | 1 | Changed: {"CustomerId":"2"} |
| 3 — Changed | 1 | dbo.CustomerContacts | 3 | 3 | 0 | 0 | 0 | None |
| 4 — Extra | 1 | dbo.Customers | 3 | 3 | 0 | 0 | 0 | None |
| 4 — Extra | 1 | dbo.CustomerContacts | 3 | 4 | 0 | 1 | 0 | Extra: {"AccountId":"10","ContactId":"99"} |

Scenario 1 should report MATCH for both tables and overall. Scenarios 2–4 should report DIFFERENCES overall and for the affected table; the unaffected table should report MATCH.

Scenario 2 deletes one target contact with a composite primary key. Scenario 3 changes only NotesText for CustomerId=2 from NULL to empty text; row counts remain equal, but one row hash should differ. Scenario 4 inserts one target-only contact. Key values are displayed as JSON strings.

## Actual run (2026-11-18)

Environment: Windows 11, SQL Server 2025 LocalDB 17.0.4025.3 (a temporary instance created for the run and deleted afterwards), .NET SDK 10.0.402 building for net8.0 on the 8.0.32 runtime. setup.sql ran once; each scenario was applied with the commands of the README and the verifier ran right after it.

| Scenario | Expected exit | Actual exit | Table rows and counts as expected | Report |
| --- | ---: | ---: | --- | --- |
| 1 — Equal | 0 | 0 | Yes | [scenario-1.md](reports/scenario-1.md) |
| 2 — Missing | 1 | 1 | Yes, missing {"AccountId":"10","ContactId":"2"} | [scenario-2.md](reports/scenario-2.md) |
| 3 — Changed | 1 | 1 | Yes, changed {"CustomerId":"2"} | [scenario-3.md](reports/scenario-3.md) |
| 4 — Extra | 1 | 1 | Yes, extra {"AccountId":"10","ContactId":"99"} | [scenario-4.md](reports/scenario-4.md) |

The files in reports/ are the migration-report.md written by each run, unedited. The two additional checks of VALIDATION.md (2026-10-27) are separate validation cases, not selectable scenarios in scenarios.sql, and were not repeated in this run.
