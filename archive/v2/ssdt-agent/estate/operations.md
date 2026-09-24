# Operations performed on this estate

Append-only. One row per shipped change, appended **at the production apply** — that is the
moment "performed on this estate" becomes true. Until an op-slug appears here, it is a
first-time operation and the record carries the added-scrutiny line; the moment it does, that
line is discharged by citing this row instead. (`classify-mechanism` and the reviewer both
read this file — the lookup, never a recollection.)

This estate cuts over to SSDT in stages, and Dev is last. QA and UAT are already SSDT-managed:
each was cut over by its own baseline publish. Dev's trunk switches over next, and only then do
changes promote through the pipeline, Dev → QA → UAT.

Those QA and UAT cutover publishes set the two environments' starting schema. They are not rows
in this register. A row here means one change that shipped *through the pipeline*, which first
becomes possible after the Dev cutover. So this register opens empty. Until a change's operation
has a row here, its record still carries the "first time on this estate" scrutiny line — even
when QA or UAT already has the column or table that operation produces, because that came from
the baseline publish, not from a tracked pipeline change.

| date | op-slug | object | PR | proof | notes |
|---|---|---|---|---|---|
