# Performance Readout Playbook

This playbook captures the workflow for replaying a full-export drop against a staging SQL Server, collecting telemetry, and summarizing the results for stakeholders.

## 1. Prerequisites

- A staging database restored from production backups or representative seed data.
- A principal with permission to execute the generated safe/remediation/static seed scripts **and** read dynamic management views (`sys.dm_os_wait_stats`, `sys.dm_tran_locks`, `sys.dm_db_index_physical_stats`).
- The generated artifacts from the most recent `full-export` run: `opportunity.safe.sql`, optional `opportunity.remediation.sql`, and any static seed scripts.

## 2. Running the load harness via the CLI

After a successful `full-export`, operators can append the `--run-load-harness` flag to replay the scripts immediately:

```bash
dotnet run --project src/Osm.Cli -- \
  full-export \
  --config ./config/full-export.json \
  --build-out ./out/full-export \
  --apply-connection-string "Server=sql-stage;Database=ExportHarness;User ID=Harness;Password=***" \
  --apply-safe-script true \
  --apply-static-seeds true \
  --run-load-harness \
  --load-harness-report-out ./out/full-export/load-harness.report.json
```

When the flag is present the CLI resolves the safe/remediation/static seed script paths from the completed run, replays them against the staging database, and writes a structured JSON report. The command line stream summarizes batch counts, wait stat deltas, lock counts, and fragmented indexes so operators can spot hotspots quickly.

### Companion tool usage

The harness logic is also exposed as a standalone console for ad-hoc replays:

```bash
dotnet run --project tools/FullExportLoadHarness -- \
  --connection-string "Server=sql-stage;Database=ExportHarness;User ID=Harness;Password=***" \
  --safe-script ./out/full-export/opportunity.safe.sql \
  --static-seed ./out/full-export/static-seeds/dbo.OSUSR_SEED.sql \
  --report-out ./out/full-export/load-harness.report.json
```

Provide as many `--static-seed` arguments as required. Use `--remediation-script` when remediation batches must be validated.

## 3. Telemetry captured

Each harness run records the following metrics for every script:

- **Batch timings** – the elapsed time for each `GO` batch, allowing operators to pinpoint long-running statements.
- **Wait stat deltas** – lock and IO waits observed while the script executed (`LCK_M_*`, `PAGEIOLATCH_*`, `WRITELOG`, `CXPACKET`, `CXCONSUMER`).
- **Lock summary** – a count of outstanding locks by resource type and mode immediately after the script finishes.
- **Index fragmentation snapshot** – the top 20 indexes by fragmentation (`sys.dm_db_index_physical_stats`), including page counts for prioritization.
- **Warnings** – permission or DMV access failures are recorded per script so gaps in telemetry can be triaged.

The JSON report aggregates run-level metadata (start/stop timestamps and total duration) alongside script-level details. Persist the report with the rest of the run artifacts for long-term analysis.

## 4. Summarizing results in `notes/perf-readout.md`

When preparing the performance readout:

1. Attach the harness report path and execution timestamp.
2. Highlight any batches exceeding agreed thresholds (e.g., >30 seconds) and identify the tables/indexes involved.
3. Capture the top wait types and lock modes observed, noting whether contention persisted after the run.
4. List indexes with fragmentation above operational baselines (commonly >20%) and whether maintenance tasks are scheduled.
5. Document remediation recommendations (e.g., add indexes, split batches, tweak batching hints) based on the telemetry trends.

The JSON schema is stable, so teams can automate summaries by parsing `Scripts[].{Category, Duration, BatchTimings, WaitStats, LockSummary, IndexFragmentation}`.

## 5. Reporting cadence

- Run the harness for every significant full-export drop (new modules, major schema shifts, large static seed refreshes).
- Update this playbook with findings and accepted thresholds after each review so future runs have clear success criteria.

Persist the consolidated summary in this document (append dated sections) and link the JSON artifact to the corresponding CI run or change ticket.
