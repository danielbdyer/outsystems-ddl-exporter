# ADMIRE

Append-only log of V1 admirations and their V2 placements. The bridge
between V1's working knowledge and V2's pure architecture.

## What this is

For each meaningful V1 component the V2 effort is preparing to migrate,
this document records:

- **What it does** — in algebraic terms.
- **V2 placement** — pure pass in `Projection.Core.Passes`, adapter at a
  port in `Projection.Adapters.*`, or a split with the boundary explicitly
  named.
- **Migration path** — how V1's behavior gets carried into V2. Where the
  C# logic lives. What test fixtures it needs. What compatibility
  considerations apply.
- **Edges / risks** — non-obvious assumptions, lurking impurities, hidden
  invariants the future migrator should know about.

Entries are short — paragraphs, not essays. The corpus accumulates value
over time. Read top-to-bottom for chronological order.

## Format

    ## YYYY-MM-DD — V1 component (file path)
    **Status:** admired (placement decided) | extracted (V2 in place)

    ### What it does (algebraic terms)
    one or two paragraphs.

    ### V2 placement
    pure pass / adapter / split — with rationale.

    ### Inputs and outputs (V2 IR)
    one paragraph naming the V2 IR fields consumed and produced.

    ### Migration path
    paragraph or two on the carry-across. Test fixtures, compatibility,
    sequencing.

    ### Edges / risks
    bullets, terse.

---

(no entries yet — the first lands in commit 6 of session 2)
