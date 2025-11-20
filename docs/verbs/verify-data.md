# verify-data

`verify-data` runs the base-layer M1.3 data integrity checks without requiring the load harness. It compares the source database (QA) against the target database (UAT staging) using the filtered OutSystems model and the same naming overrides applied during emission. The command currently verifies table-level row counts and nullable column NULL counts, emitting warnings instead of hard failures so operators can investigate quickly.

## Usage

```bash
dotnet run --project src/Osm.Cli -- \
  verify-data \
  --manifest ./out/full-export/full-export.manifest.json \
  --source-connection "Server=qa;Database=QA;TrustServerCertificate=True" \
  --target-connection "Server=uat;Database=UAT;TrustServerCertificate=True"
```

- `--manifest` (optional): Resolves the filtered `model.json` path recorded by `full-export` (defaults to `./full-export.manifest.json`).
- `--model` (optional): Overrides manifest discovery when the model is elsewhere on disk.
- `--config` (optional): Forces a specific pipeline configuration; otherwise the command reuses the manifest’s `configPath` or the environment defaults that `full-export` would pick up.
- `--report-out` (optional): Output path for `data-integrity-verification.json` (defaults next to the manifest).
- `--source-connection` / `--target-connection`: Required connections for QA and UAT staging.

The report uses a modular schema so future UAT-users verification can slot in alongside the base layer:

```json
{
  "overallStatus": "PASS|WARN",
  "verificationTimestampUtc": "2025-11-20T00:00:00Z",
  "baseVerification": {
    "passed": true,
    "warnings": [],
    "tablesChecked": 12,
    "rowCountMatches": 12,
    "nullCountMatches": 34
  },
  "uatUsersVerification": null
}
```

## When to use it

- **Manual apply flows**: If scripts are being applied outside the load harness (e.g., DBA-led deployment or SSDT publish), run `verify-data` afterward to prove row and NULL preservation before promoting to UAT.
- **CI smoke**: Add the command after artifact apply steps to catch data loss early without incurring the heavier checksum-based M1.8 work.

## Optional: run with the load harness

Operators who prefer an automated replay can still lean on the load harness, then call `verify-data` for a quick post-replay sanity check:

```bash
dotnet run --project src/Osm.Cli -- \
  full-export ... --run-load-harness --load-harness-connection-string "Server=uat;Database=UAT" \
  --load-harness-report-out ./out/full-export/load-harness.json

dotnet run --project src/Osm.Cli -- \
  verify-data --manifest ./out/full-export/full-export.manifest.json \
  --source-connection "Server=qa;Database=QA" --target-connection "Server=uat;Database=UAT"
```

This keeps the data integrity checks standalone for teams that do not use the harness today, while documenting how to enable the harness when staged replays are desirable.
