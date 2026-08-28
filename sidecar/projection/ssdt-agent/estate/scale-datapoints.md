# Scale datapoints — the measured numbers behind the added-scrutiny line

"At production row counts this change may block writes or run long" is a claim with a shape.
This file holds the measured points on that curve, so the added-scrutiny line is written from
a measurement and challenged against one. `row-tiers.md` answers *which tier a table is in*;
this file answers *what a tier costs*.

Measured 2026-08-28 on the proving-ground scale lane (`../proving-ground/twin.scale.json` —
its own container, no static-data lane, floor-minted volumes; sqlpackage 170.5.76, SQL Server
2022 in Docker on the session host). The numbers are substrate-relative: the shape of the
curve travels, the absolute seconds do not. Re-measure on estate hardware before quoting a
window. Each publish is a Strict publish of a one-op model edit against the minted twin;
"overhead floor" is the same publish with a no-op model. To re-measure: `TWIN_CONFIG=twin.scale.json
twin up && twin seed --scenario scale` (or `scale1m`), then publish one-op model edits from a
scratch copy of the project.

## At the 100k–1M tier

OrderLine 120,000 rows; 181,400 rows total; mint 75 s including the constraint trust gate.

| operation | wall |
|---|---|
| no-op publish (overhead floor) | 8.8 s warm; 21.9 s cold |
| add-check, re-validated over 120k rows | 3.9 s |
| add nonclustered index, built over 120k rows | 4.3 s |
| add-fk, re-validated over 120k children — lands trusted | 4.3 s |
| drop populated column → blocked, `Msg 50000` | 6.5 s to the refusal |

The tier's finding: every green publish and the refusal alike complete in single-digit
seconds, dominated by sqlpackage model work — the engine's scan cost is invisible at this
tier. The row-presence guard is data-blind, so the refusal costs the same at every tier. The
added-scrutiny line's teeth begin above this tier.

## At the >1M tier

OrderLine 1,050,000 rows; 1,182,000 rows total.

| operation | wall |
|---|---|
| mint (wipe to full, trust gate included) | <fill in> |
| no-op publish (overhead floor) | <fill in> |
| add-check, re-validated over 1.05M rows | <fill in> |
| add nonclustered index, built over 1.05M rows | <fill in> |
| add-fk, re-validated over 1.05M children | <fill in> |
| drop populated column → blocked, `Msg 50000` | <fill in> |
