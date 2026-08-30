# Row tiers — the scale facts behind the added-scrutiny line

"At production row counts this change may block writes or run long" is a claim about a
specific table's magnitude. This file holds that magnitude, so the line is written from a
lookup and challenged against one. Tier is the order of magnitude of the row count
(`0` · `1–1k` · `1k–100k` · `100k–1M` · `>1M`); the standing threshold that adds scrutiny is
`>1M` (`classify-mechanism`).

Refresh a tier whenever a table crosses a boundary — measured from the estate itself once it is
cut over, or from the Twin's evidence (`twin status`, the evidence tiers) before that. Record
where each tier came from and the date it was measured. A tier with no source recorded is a
guess, and a reviewer cannot rely on it.

The Dev cutover has not landed yet. It is the estate's final leg; QA and UAT are already
SSDT-managed. The rows below hold the sample substrate's seed counts. They serve two purposes:
they show the format, and they feed the proving loop's own scrutiny checks. Replace them with
real estate measurements at the Dev cutover. Measure each environment separately, because QA
and UAT hold their own row counts, which differ from Dev's.

| table | tier | measured | source |
|---|---|---|---|
| dbo.Customer | 1–1k | 5 rows | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.Order | 1–1k | 4 rows | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.OrderLine | 1–1k | 8 rows | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.Product | 1–1k | 5 rows | sample seed (Data/Seed.sql), 2026-08-11 |

Measured publish timings at the 100k–1M and >1M tiers — what each tier actually costs on a
Strict publish, and where the engine's scan becomes visible over the tool's overhead — live
in `scale-datapoints.md`, re-measured from the proving-ground scale lane
(`../proving-ground/twin.scale.json`).
