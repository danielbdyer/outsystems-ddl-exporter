# Scale datapoints — the measured numbers behind the added-scrutiny line

"At production row counts this change may block writes or run long" is a claim with a shape.
This file holds the measured points on that curve, so the added-scrutiny line is written from
a measurement and challenged against one. `row-tiers.md` answers *which tier a table is in*;
this file answers *what a tier costs*.

Measured 2026-08-28 on the proving-ground scale lane (`../proving-ground/twin.scale.json` —
its own container, no static-data lane, floor-minted volumes; sqlpackage 170.5.76, SQL Server
2022 in Docker on the session host). The numbers are substrate-relative: the shape of the
curve travels, the absolute seconds do not. Re-measure on estate hardware before quoting a
window. Each publish is a Strict publish of a one-op model edit against the minted twin from
a scratch copy of the project; the "overhead floor" is the same publish with a no-op model,
and it varied 3.8–8.8 s warm (21.9 s on the first cold connection, one 14.7 s outlier right
after a bulk load) — read every green number against that noise band. To re-measure:
`TWIN_CONFIG=twin.scale.json twin up && twin seed --scenario scale` (or `scale1m`), then
publish the one-op edits.

## At the 100k–1M tier

OrderLine 120,000 rows; 181,400 rows total. Mint: 5.9 s, constraint trust gate included.

| operation | wall |
|---|---|
| no-op publish (overhead floor) | 3.8–8.8 s warm; 21.9 s cold |
| add-check, re-validated over 120k rows | 3.9 s |
| add nonclustered index, built over 120k rows | 4.3 s |
| add-fk, re-validated over 120k children — lands trusted | 4.3 s |
| drop populated column → blocked, `Msg 50000` | 6.5 s to the refusal |

Every operation, green or refused, sits inside the tool-overhead noise band: the engine's
scan cost is invisible at this tier.

## At the >1M tier

OrderLine 1,050,000 rows; 1,182,400 rows total. Mint: 28.0 s bare schema (~42k rows/s);
36.6 s re-minted through a live CHECK + FK + index — the trust gate and the constrained
bulk load cost ≈ 8.6 s at this volume, and the FK ends trusted after the re-mint.

| operation | wall |
|---|---|
| no-op publish (overhead floor) | 3.8–4.3 s |
| add-check, re-validated over 1.05M rows | 4.3 s |
| **add nonclustered index, built over 1.05M rows** | **15.4 s** |
| add-fk, re-validated over 1.05M children — lands trusted, zero orphans | 4.0 s |
| drop populated column → blocked, `Msg 50000` | 3.9 s to the refusal |

## What the two tiers say together

- **The index build is the first operation whose engine cost surfaces** — ~11 s over the
  floor at 1.05M rows, roughly linear in row count on this substrate. Index-shaped work
  (add-index, the rebuild inside modify-index, a clustered define-pk) is where the
  added-scrutiny window earns its name: at a 10–40M-row production table the same build is
  minutes of write-blocking work.
- **Constraint re-validation scans (CHECK, FK) are still invisible at 1M** on this
  substrate. Their added scrutiny at production scale is real but starts higher up the
  curve than the index build's.
- **The row-presence refusal is data-blind and O(1)**: `Msg 50000` arrives in floor time at
  every tier. A blocked publish never gets cheaper or dearer with volume — the guard reads
  row *presence*, not rows.
- The mint itself stays interactive at both tiers (seconds, not minutes), so proving at a
  realistic volume is a default, not a ceremony.
