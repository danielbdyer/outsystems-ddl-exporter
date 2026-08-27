# Row tiers — the scale facts behind the added-scrutiny line

"At production row counts this change may block writes or run long" is a claim about a
specific table's magnitude. This file holds that magnitude, so the line is written from a
lookup and challenged against one. Tier is the order of magnitude of the row count
(`0` · `1–1k` · `1k–100k` · `100k–1M` · `>1M`); the standing threshold that adds scrutiny is
`>1M` (`classify-mechanism`).

The tiers are held **per environment**, because Dev, QA, and UAT hold their own row counts and
a magnitude claim names the environment it is about. The distributed proving template blends
the three environments' shapes — an extreme survives the merge — but its blended counts are
never magnitude claims: a record that says "at production row counts" cites the column for the
environment it means, from this file. Refresh a tier whenever a table crosses a boundary —
from the per-environment evidence packs at each capture (each pack's `rowCount` is that
environment's measurement), or from the estate itself once it is cut over. Record where each
tier came from and the date it was measured. A tier with no source recorded is a guess, and a
reviewer cannot rely on it. Update every cell of a row together; a row with one fresh cell and
two stale ones misleads more than a dated row.

The Dev cutover has not landed yet. It is the estate's final leg; QA and UAT are already
SSDT-managed. The rows below hold the sample substrate's seed counts in every column: they show
the format and feed the proving loop's own scrutiny checks. Replace them with the three real
environments' measurements at the first capture (the capture-point runbook's import step
produces all three packs in one sitting).

| table | Dev | QA | UAT | measured | source |
|---|---|---|---|---|---|
| dbo.Customer | 1–1k | 1–1k | 1–1k | 5 rows (sample) | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.Order | 1–1k | 1–1k | 1–1k | 4 rows (sample) | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.OrderLine | 1–1k | 1–1k | 1–1k | 8 rows (sample) | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.Product | 1–1k | 1–1k | 1–1k | 5 rows (sample) | sample seed (Data/Seed.sql), 2026-08-11 |
