# UAT Users Feasibility Backlog

- [x] Normalize bracketed or quoted schema.table input for `--user-table` so CLI runs mirror SQL Server exports.
- [x] Deduplicate `--include-columns` values case-insensitively to keep catalogs predictable for operators.
- [x] Fail fast when allowed user sources yield zero identifiers to surface missing or stale inputs immediately.
- [x] Investigate GUID support for OutSystems environments that still rely on non-numeric identifiers.
- [x] Normalize the uat-users pipeline to carry strongly typed identifiers across loaders, contexts, snapshots, and SQL emission so GUID and text values stay lossless.
- [x] Document identifier flexibility for operators and expand regression coverage for GUID-heavy inputs across loader and emitter tests.
