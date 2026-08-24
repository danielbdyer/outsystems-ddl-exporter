# Row tiers — the scale facts behind the added-scrutiny line

"At production row counts this change may block writes or run long" is a claim about a
specific table's magnitude. This file holds that magnitude, so the line is written from a
lookup and challenged against one. Tier is the order of magnitude of the row count
(`0` · `1–1k` · `1k–100k` · `100k–1M` · `>1M`); the standing threshold that adds scrutiny is
`>1M` (`classify-mechanism`).

Refresh whenever a table crosses a tier boundary — from the estate itself once cut over, or
from the Twin's evidence (`twin status`, the evidence tiers) before that. Stamp the source
and date; a tier without its basis is a guess wearing a table row.

The Dev cutover (the estate's final leg — QA and UAT are already SSDT-managed) has not yet
landed. The rows below are the **sample substrate's** seed state — they exist as the worked
example of the format and serve the proving loop's own scrutiny checks; replace them with
estate measurements at the Dev cutover, and refresh per environment (QA and UAT hold their
own row counts).

| table | tier | measured | source |
|---|---|---|---|
| dbo.Customer | 1–1k | 5 rows | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.Order | 1–1k | 4 rows | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.OrderLine | 1–1k | 8 rows | sample seed (Data/Seed.sql), 2026-08-11 |
| dbo.Product | 1–1k | 5 rows | sample seed (Data/Seed.sql), 2026-08-11 |
