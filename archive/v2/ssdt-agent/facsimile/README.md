# The Facsimile

**A local copy that agrees with an environment on every question the publish engine will ask of its
data, and on nothing else.** It succeeds the Twin.

DacFx asks the data two things when it publishes a change:

- *is there a row?*
- *is there a row that breaks this rule?*

The facsimile reads the environment twice: its schema, then one count per question. It plants one
synthetic row for every "yes" in a LocalDB copy of that schema, and lets DacFx publish the change
onto it. What happens is the verdict. The Twin tried to resemble the data; the facsimile only needs
to agree with it.

**State:** specified test-first, not yet implemented (gate G0 of `ARCHITECTURE.md` §10). Its home is
v3, in C# (settled 2026-10-18). The Twin
stays in service until the facsimile reaches parity (G3) and has run beside it for one release cycle
(G4).

Since 2026-10-19 this spec shares one branch with v3's tree. It sits here, beside the tree it
succeeds, until WP 2.6 brings the catalog into `tests/Cases/` with its manifest rows, its own
budget, its citations and the lint's port (`../../../../V3_MILESTONES.md` §3.1 and §8).

## Read in this order

Bringing this into v3 (`kernel/`, `io/` and `cli/`, on this branch since 2026-10-19)? Start with
**`HANDOFF.md`**: what is settled,
where each part lands in v3, and the one decision to put to the operator first.

1. **`ARCHITECTURE.md` §0–§1:** the essence, and what "minimum viable code" means here.
2. **`ARCHITECTURE.md` §2:** everything the Twin does today, and what becomes of each part.
3. **`ARCHITECTURE.md` §3–§8:** the shape (a pure core and a thin shell), the vocabulary, the copy,
   the outcomes, the verbs.
4. **`INVARIANTS.md`:** the laws, each with its mechanism and the property its cases test.
5. **`cases/README.md`:** the grammar every case is written in, so the catalog runs as the test
   suite.
6. **`TARGET.md`:** how it is built: C# on .NET as a CLI with an MCP mode (with a PowerShell
   fallback), the layout, the dependencies, the coding style, the NFRs, and the order of work.
7. **`cases/90-traceability.md`:** which cases witness which law, operation, question kind, stop and
   Twin responsibility, plus the open `verify` questions.

The reasoning that led here, with its evidence, is `../../TWIN_SUCCESSION.md`, beside the ssdt-agent
tree.

## The catalog

| File | What it specifies |
|---|---|
| `cases/00-invariants.md` | property cases for the laws |
| `cases/10-questions.md` | which questions a delta asks |
| `cases/20-planter.md` | how witness rows are made, for every type and structure |
| `cases/30-outcomes.md` | how a publish is classified, tiered and recorded |
| `cases/40-ops-columns.md`, `cases/41-ops-keys.md`, `cases/42-ops-tables.md` | the 45 operations, each in its standard situations |
| `cases/43-compound.md` | releases made of several operations |
| `cases/50-environments.md` | access, drift, per-environment proof, Prod |
| `cases/60-privacy.md` | the privacy laws, attacked |
| `cases/70-process.md` | the verbs, `up`, config, CI, the toolchain |
| `cases/80-twin-parity.md` | every Twin responsibility and every Twin fact: kept or safely retired |
