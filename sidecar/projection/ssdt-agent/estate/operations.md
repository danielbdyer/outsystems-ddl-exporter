# Operations performed on this estate

Append-only. One row per shipped change, appended **at the production apply** — that is the
moment "performed on this estate" becomes true. Until an op-slug appears here, it is a
first-time operation and the record carries the added-scrutiny line; the moment it does, that
line is discharged by citing this row instead. (`classify-mechanism` and the reviewer both
read this file — the lookup, never a recollection.)

The cutover is staged and Dev goes last: QA and UAT are already SSDT-managed (cut over from
their own baseline publishes), Dev's trunk switch is the final leg, and the first
pipeline promotions (Dev → QA → UAT) begin after it. The QA/UAT cutover publishes are those
environments' **baselines**, not rows here — "performed on this estate" means shipped
**through the pipeline**, which first becomes possible after the Dev cutover. The register
opens empty; until an op-slug has a row, its added-scrutiny line stands even where QA or
UAT's schema already exhibits the op's result.

| date | op-slug | object | PR | proof | notes |
|---|---|---|---|---|---|
