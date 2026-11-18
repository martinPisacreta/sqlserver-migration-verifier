# SQL Server migration verification

Generated (UTC): 2026-11-18T09:01:48.7095110+00:00

Result: DIFFERENCES — one or more selected tables differ.

Missing means present only in source; extra means present only in target. Changed means the same serialized primary key has a different SHA-256 row hash.

| Table | Source rows | Target rows | Missing | Extra | Changed | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| dbo.Customers | 3 | 3 | 0 | 0 | 0 | MATCH |
| dbo.CustomerContacts | 3 | 4 | 0 | 1 | 0 | DIFFERENCES |

## dbo.CustomerContacts

### Extra keys — showing 1 of 1

- {"AccountId":"10","ContactId":"99"}


## Scope

Selected tables only; independent database snapshots. Pause writes before acceptance. Counts cover all scanned rows; key examples are capped at 20 per category and table, ordered by serialized key bytes. Hash equality is probabilistic and does not validate constraints, indexes, procedures or application behavior. See README.md for supported types and limits.
