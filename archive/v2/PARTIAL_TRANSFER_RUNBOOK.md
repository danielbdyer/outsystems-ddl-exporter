# The Partial-Transfer Runbook — QA → UAT, step by step

> The end-to-end operating guide for moving a **subset of tables** between two
> managed Cloud OutSystems environments whose physical `OSUSR_*` names differ.
> Written 2026-07-06 (the preview-engine program). The companion narrative is
> `PARTIAL_TRANSFER_READINESS_LOG.md` (entry 14 is the semantics walkthrough).
>
> The shape of the whole flow: **configure → check go (red) → resolve each
> named decision → check go (green) → preview → execute → re-run at will.**
> Nothing is written to the sink until step 6.

---

## Step 0 — What you need before starting

- Connection strings for BOTH environments, supplied out-of-band (D9): either
  an environment variable per side or a file per side. The principals need
  database-scope `SELECT, INSERT, UPDATE, DELETE` — the managed-cloud default.
  (`INSERT` etc. are only exercised on the sink; the source is read-only in
  practice, but the same grant profile works for both.)
- The principals must be able to `SELECT` the OSSYS metamodel tables
  (`ossys_Espace`, `ossys_Entity`, `ossys_Entity_Attr`) — that is where the
  SS_KEY-aligned contracts come from.
- The `projection` CLI on a machine that can reach both databases.

## Step 1 — Configure `projection.json`

```jsonc
{
  "environments": {
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN",
                   "rendition": "physical", "archetype": "managed-dml" },
    "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN",
                   "rendition": "physical", "archetype": "managed-dml",
                   "grant": "data" }
  },
  "flows": {
    "golden": { "from": "cloud-qa", "to": "cloud-uat", "scope": "data",
                "tables": ["Customer", "Order"],
                "reconcile": [],
                "strategy": "replace" }
  }
}
```

Load-bearing choices:
- **`rendition: physical` on BOTH sides** — this is what routes the flow onto
  the SsKey-aligned peer leg (per-side OSSYS contracts, the shape gate, the
  relationship gate). Leave it unset and the flow rides the old name-blind
  transfer, which assumes matching physical names — the plan will print a
  voiced note warning you if you do.
- **`tables`** — logical entity names (what you see in Service Studio), not
  physical `OSUSR_*` names. Use `Module.Entity` if a name exists in more than
  one module (the resolver refuses ambiguity by name).
- **`strategy: replace`** — wipe-the-subset-then-reload; a re-run is
  idempotent. `merge` appends and will duplicate sink-minted rows on a re-run
  into a populated sink (the go board flags exactly this).
- **`reconcile`** — start EMPTY. The go board will tell you which entries you
  need (step 3).

## Step 2 — Run the go board (expect RED the first time)

```
projection check go golden
```

Read-only (plus a dry run — real reads, zero writes). It prints one line per
axis with `[ GO ]` / `[STOP]` / `[note]` marks and a final verdict, and exits
**5 while red, 0 when green** — so you can wire it into CI or a pre-flight
script and literally watch it turn green. `--format json` emits the whole
board as structured data (`{verdict, redCount, items:[{axis, status,
headline, remedy, detail}]}`) for pipelines that want more than the exit
code. One note for CI authors: the board's not-ready verdict is always
exit 5 (the `check shape` class); the live `--go` run refines that into 5
(shape) vs 9 (relationships / identities / drops).

`--review` opens the decision workbench on a real terminal: each reference
that escapes the transfer is one question with its answers side by side —
reconcile by a named column, re-key onto one chosen row, add the table to the
transfer, or declare the datasets identical — each carrying its exact counted
consequence, computed over the full rowsets through the same match the live
run uses. The arrow keys walk the decisions; Space selects the next answer and
the whole coupled unit's counts recompute; `w` writes the selections into
`projection.json` as the same `reconcile` / `tables` / `supportingScope`
entries this runbook teaches by hand. On a pipe, `--review` renders the same
decision tables one-shot (nothing is lost headless), and `--format json`
carries every consequence sentence.

Below the decisions, the workbench lists **the consent ledger** (2026-07-10):
every destructive or creative act the forecast plan performs — each wipe of a
table, each table whose rows arrive under freshly minted keys, each explicit
primary-key write, each match onto rows the target already holds, each set of
rows left out of the load — one line per act, with a complete statement of
what it does and a **fingerprint** pinning exactly what was read: a wipe
carries the target population's first key, last key, and row count; a match
carries a hash over every matched pair, every unmatched value, and the
target's exact duplicate counts. `d` blesses the act under the cursor at that
fingerprint; `a` blesses every act except an identity-insert (explicit-key
writes are blessed one at a time, deliberately). A blessing writes to
`projection.json` in the same keystroke, as a `signoff` entry:

```jsonc
"signoff": [
  { "mode": "replace" },
  { "act": "wipe:AppCore.Customer", "fingerprint": "population:1:2048:2048" }
]
```

The entry can equally be authored by hand — the board prints it verbatim on
each unblessed act's line. If anything the act would do changes after the
blessing — a row appears in the population, a matched pair re-points, a
duplicate lands on the target's match column — the fingerprint no longer
matches and the board says the blessing was captured at a different
fingerprint: read the act again and re-bless.

The ledger is also the live run's gate on the peer path: an Execute refuses
by name (`transfer.writeSignoff.actUnblessed`, exit 9) until every act it
performs is blessed at its current fingerprint, and the refusal carries the
full act list with each fingerprint — the same entries the board prints. The
board and the engine derive the acts and the fingerprints through the same
functions over the same reads, so what the board shows blessed is exactly
what the run will accept.

`--impact` writes the row-grain before/after artifact
(`go-board/<flow>.impact.html` + a `.json` twin), **triaged by coupling**
(2026-07-10): each relational unit of the transfer — a group of tables joined
by foreign keys — is classified and ranked. A unit whose source and target
hold the same rows (verified column by column) folds to one line; a unit
where every source row pairs with a row the target already holds folds to one
line; a unit with nothing to do folds to one line. The units that stay
expanded are exactly the ones needing attention: a column pointing at a table
outside the transfer (a decision to make), or rows that will be inserted or
deleted. The unit with the most affected rows opens first. The fold hides
scroll, never rows — every row is counted in the summary, and the `.json`
twin carries every unit in full.

The axes it judges, in order — the full forecast vocabulary:

| Axis | What it proves | Red means |
|---|---|---|
| routing | the flow rides the SsKey-aligned peer leg | renditions unset → fix step 1 |
| contracts | both OSSYS metamodels read; identities align | connection/grant problem — remedy printed |
| identity basis | (note) the target mints fresh surrogate keys; by-name alignment is name-derived | never red — the consequence for the key plane, stated so it is not a surprise |
| tables | your subset resolves (unambiguously) | typo or ambiguous name — use `Module.Entity` |
| reconcile | every reconcile entry resolves against the sink | bad entry — use `Module.Entity:Column` |
| shape | the two models are ONE shape over the transferred set | real divergence — align model versions |
| relationships | no FK escapes the subset un-strategized | **the main open decision — see step 3** |
| foreign refs | (note, only if `foreignRefs` is declared) references outside the contract, unverified | never red — confirm each target aligns across environments; a wrong declaration cross-wires the FK |
| load order | parents-before-children is proven | an unresolvable cycle — remedy printed |
| forecast | the DRY RUN: exact row counts per table | the dry run refused — reason printed |
| cycles / identities / drops | forecast of unbreakable cycles, unmatched identities, dropped rows | fix data / user-map, or accept with `--allow-drops` at run time |
| ambiguous source / target keys | duplicate reconcile keys — a record loses its identity or an older row displaces it | de-dup the key / pin a `ManualOverride` winner, or accept with `--allow-drops` |
| replayed drops | a resumable no-op re-run replays the prior run's drop verdict | clear the resume marker and re-run, or accept with `--allow-drops` |
| cdc | the sink is not CDC-tracked | decide `--allow-cdc` or de-track |
| grant | the sink principal carries db-scope DML | grant the missing permission |
| re-run | your strategy is safe against the sink's ACTUAL state | duplicates (merge into populated) or wipe blockers — remedy printed |
| signoff | a destructive wipe is greenlit in the flow's `signoff` | the mode is not declared — add `{ "mode": "replace" }` after reading the printed impact |
| consent | (note) the per-act ledger: every act the run performs, its fingerprint, and where each blessing stands | never red on the board — but a live Execute REFUSES (exit 9) until each act is blessed; bless in `--review` (`d`/`a`) or paste the printed entry |
| execute gates | (note) the two run-time gates | never red — informational |

The `ambiguous source / target keys` and `replayed drops` axes read the same
report fields the live run's exit-9 policy counts, so the board can no longer show
GREEN over a run that would then drop rows at `--go` (board/engine parity over the
whole drop-set).

## Step 3 — Resolve the open decisions (the usual one: escaping relationships)

A `[STOP] relationships` line means a chosen table points at a table you did
NOT choose. Every such edge needs one of two strategies:

1. **Reconcile it** (the default recommendation): the sink already holds that
   reference data under its own keys — tell the engine which business column
   matches rows across environments. The board prints the paste-able entry,
   e.g.:
   ```jsonc
   "reconcile": ["AppCore.City:Name"]
   ```
   Reconciled tables are never inserted into — source rows are matched to the
   sink's existing rows by that column, and every FK re-keys to the sink's own
   identities. Three rule forms, one grammar:
   - `"Module.Entity:Column"` — dynamic match by business column;
   - `"Module.Entity:=1234"` — **the single-owner pin**: EVERY reference
     re-keys to the one sink row `1234` (configuration tables owned by one
     designated user/row — no matching at all);
   - `"Module.Entity:Column:=1234"` — dynamic match first, the pinned owner
     catches every row the match misses.
   A pinned key that names no sink row refuses by name before any write
   (`transfer.reconcile.pinnedOwnerMissing`), and the go board probes every
   pin against the live sink (`pinned owners` axis) so you see it early.
2. **Widen the subset**: add the referenced table to `tables` — it transfers
   too (the sink mints its keys; FKs re-point automatically).

Re-run `projection check go golden` after each edit until the verdict is
GREEN. Other common decisions the board can raise: unmatched identities (fix
the reconcile data or accept the loss with `--allow-drops` at run time), a
CDC-tracked sink (`--allow-cdc`), merge-into-populated-sink (switch to
`strategy: replace`).

## Step 4 — GREEN

```
  VERDICT — GREEN. Every gate passes. Execute with: PROJECTION_ALLOW_EXECUTE=1 projection golden --go
```

Exit code 0. The setup is validated: every gate the live run will hit has
passed against the real environments, and the forecast tells you exactly how
many rows will move per table.

A green verdict has one honest variant. When a probe could not read the live
environments — a sink count that would not return (the forecast shows `?` for
that table), or a `foreignRefs` target outside the acquired contract — the axis
is a `[note]` marked *unverified* and the verdict reads **"GREEN. Every gate
passes; N finding(s) below remain unverified."** The exit is still 0 (nothing is
red), but the board is telling you it could not check those N facts — read the
named note line(s) before authorizing the run, and re-run the board once the
sink is reachable to measure them.

## Step 5 — Preview (the dry run, narrated)

```
projection golden
```

Preview is the DEFAULT (no flag). It runs the same engine with zero writes and
narrates the load plan in dependency order: per-table row counts and identity
dispositions ("assigned by the target" = sink mints keys; "re-keyed by rule" =
reconciled), deferred FK columns, anything unmatched or droppable, plus the
shape advisories. `projection golden > preview.txt` captures everything —
the safety information prints on stdout.

## Step 6 — Execute

```
PROJECTION_ALLOW_EXECUTE=1 projection golden --go
```

Two deliberate gates: the environment variable authorizes the environment; the
flag states per-run intent. And one consent gate: every destructive or
creative act the run performs must be blessed at its current fingerprint in
the flow's `signoff` — an unblessed act refuses
`transfer.writeSignoff.actUnblessed` before any write, listing every open act
with the exact entry that blesses it. Bless in the review workbench
(`projection check go golden --review`: `d` per act, `a` for everything but an
identity-insert), or paste the printed `{ "act": …, "fingerprint": … }`
entries by hand. Exit codes: `0` clean · `5` shape divergence · `9`
un-strategized relationships / unmatched identities / dropped rows / a write
the flow has not consented to (the report names them; `--allow-drops`
downgrades identity/drop refusals you have deliberately accepted) · `6`/`7`
connection/grant · `2` argument/spec errors.

What happens inside, in order: contracts re-acquired → gates re-checked (the
same ones the board ran) → subset wiped child-first (`strategy: replace`) →
parents loaded first, sink minting every identity → children's FKs re-pointed
through the captured old→new key map → nullable cycle FKs re-pointed by
phase-2 UPDATE → reconciled kinds' FKs re-keyed to the sink's existing rows →
the run report printed (same vocabulary as the preview).

## Step 6½ — The proving loop: revert what you just transferred

Every successful `--go` writes **`transfer-undo.sql`** into the revert dir
(default: the working directory) — the precise child-first
`DELETE`-by-captured-key script for exactly the rows this run minted.
Pre-existing sink rows (reconciled tables, anything already there) are never
in it. The run's closing narration prints the path and the revert command.

```
projection revert --against cloud-uat                    # preview: tables + key counts, no deletes
PROJECTION_ALLOW_EXECUTE=1 projection revert --against cloud-uat --go   # the undo, ONE transaction
```

- `--script <path>` points at a specific artifact (default `./transfer-undo.sql`;
  a FAILED run's compensation script is `transfer-revert.sql` — same verb runs it).
- The live revert runs in one transaction: all deletes land or none do (a
  failure rolls back and says so).
- So the small-sample proving loop is: declare a tiny `tables` subset → board
  green → `--go` → verify in the target app/DB → `revert --go` → the sink is
  back where it started. Iterate until confident, then widen the subset.
- **The wrong-sink guard**: every artifact carries a provenance header naming
  the database the keys were captured against; `revert` refuses by name
  (`revert.sinkMismatch`) if `--against` resolves to a different database.
  `--force` is the deliberate override for a legitimately renamed/restored
  copy. Never point a revert at any environment other than the one the
  transfer wrote.
- Two honesty notes: (1) a `replace`-strategy run's WIPE is not undone by the
  script — the undo removes what the run MINTED; rows the wipe deleted are
  gone (on a first load into empty tables this distinction is empty). (2) the
  undo targets captured keys — run it BEFORE new app activity writes rows that
  reference the minted ones (an FK from a newer row blocks the delete; the
  transaction rolls back and tells you). A second revert of the same artifact
  reports "Nothing to revert" rather than pretending to delete again.

## Step 7 — Re-run whenever you need

With `strategy: replace`, running step 6 again wipes the subset and reloads —
same result every time (proven by the idempotency e2e). Refresh QA-side data,
re-run, done. If the sink's reference data changed (reconciled tables), the
board re-validates the matches — run `check go` again first when in doubt.

## Step 8 — Export the subset to CSV files instead of a live sink

The same subset machinery can land as FILES: declare a csv environment and
point a flow's `to` at it.

```jsonc
{
  "environments": {
    "cloud-qa": { "access": "direct", "conn": "env:CLOUD_QA_CONN",
                  "rendition": "physical", "archetype": "managed-dml" },
    "exports":  { "access": "csv", "out": "exports/golden" }
  },
  "flows": {
    "golden-csv": { "from": "cloud-qa", "to": "exports", "scope": "data",
                    "tables": ["Customer", "Order"],
                    "withReferenced": true }
  }
}
```

`projection golden-csv` reads the declared tables live from the source and
writes one CSV per table into `out` — named by physical table
(`OSUSR_ABC_CUSTOMER.csv`), header row = physical column names, RFC 4180
quoting, UTF-8 — plus one `export-manifest.json` recording, per table, the
Service Studio names (module, entity, and each column's logical name), the
row count, and how the table entered the export. Nothing is written to any
database, so there is no execute gate: `--go` changes nothing, and
`check go` on a csv flow says exactly that and points you at the run.

**`withReferenced`** controls what happens when a declared table's foreign
keys point at tables you did NOT declare. Off (the default), the export
writes only the declared tables and the run names every escaping reference —
those foreign-key values will not resolve inside the files. On (or `--with-
referenced` for one run), the export follows the references and carries the
rows they point at — only the rows actually referenced, followed
transitively until the set closes. STATIC reference tables are excluded
either way: their content is identical in every environment by declaration,
so the file would add bytes, not information. The manifest marks each pulled
table `referenced` so the two populations are never confused.

Two caveats to read the files with:
- A database NULL and an empty text value are both written as an empty
  field — the source read collapses them before any file is composed.
- Rows arrive in primary-key order (the source read's own order), so the
  same source produces the same files.

---

## Troubleshooting quick table

| Symptom | Meaning | Move |
|---|---|---|
| board: `[STOP] contracts` | OSSYS metamodel unreadable | grant SELECT on `ossys_*` to the principal |
| board: `[STOP] shape` | the two environments genuinely run different model versions | deploy the same version to both, or narrow the subset |
| exit 9 at `--go` with drop lines | rows referenced identities that don't match | fix the reconcile data or accept with `--allow-drops` |
| raw SQL permission error mid-load | an object-scope DENY on a table OUTSIDE the board's probe set (a write-time-only table, or a schema/column-scope DENY) — the narrow G1 residual | remove the DENY; the revert script (`transfer-revert.sql`) undoes sink-minted rows |
| board green but `--go` exits 7 | `PROJECTION_ALLOW_EXECUTE` not set | set it (the board's `[note]` line reminds you) |

## Rehearsed end-to-end (2026-07-06)

Every step above was executed verbatim with the real CLI against a mock QA/UAT
pair (espace-shifted physical names, DML-only principals on both sides):
menu → board RED (2 escapes named) → reconcile entries pasted → board RED
again (the identities forecast caught missing sink reference data BEFORE any
write) → sink reference data fixed → GREEN → preview ("4 row(s) would move
across 4 table(s)") → `--go` without the env var refused (exit 7, remedy
printed) → live run (customers minted 900/901 with cities re-pointed to the
sink's own 501/502; orders minted 9000/9001 pointing at the NEW customer keys
and name-matched categories) → re-run idempotent (counts stable, zero dangling
FKs, source untouched).

## Live-environment hotspots (what the rehearsal CANNOT prove — read before your first real run)

1. **User references.** Real estates carry `CreatedBy`/`UserId` columns
   referencing the platform User entity (system espace). If the User kind is
   IN the acquired contract, any such edge shows up on the board's
   `relationships` line — reconcile it (`Users.User:Username` or by email).
   If your subset tables carry user columns but NO such line appears, the
   User entity may be OUTSIDE the contract — the gate cannot see a reference
   whose target kind is absent, and user ids DIFFER between environments.
   Check manually before trusting those columns.
2. **Scale.** A `tables` subset runs MATERIALIZED: the subset's rows are
   resident in memory on the machine running the CLI. Fine for thousands to
   low millions of small rows; a very large table in the subset needs the
   streaming realization, which does not yet support `--tables`. Start with
   modest subsets.
3. **Wipe duration on re-runs.** `strategy: replace` deletes with plain
   `DELETE` (no TRUNCATE — the grant has no ALTER): a re-run over a very
   large already-loaded subset holds a long transaction. First loads into an
   empty sink are unaffected.
4. **Connection strings.** Real managed environments typically require
   `Encrypt=True` with a valid certificate chain (do not copy the
   `TrustServerCertificate=True` from lab examples), plus VPN/IP-allowlist
   network reach from the machine running the CLI to BOTH databases.
5. **Command timeouts.** Contract acquisition is one metamodel batch (fast
   even on large estates), but a multi-million-row ingest SELECT can exceed
   default command timeouts on slow links — if you hit timeouts, narrow the
   subset first.
6. **The narrow preflight blind spot** — the board's `grant` axis now evaluates
   OBJECT-scope effective permissions (database OR per-table grants, with DENYs
   subtracted) over the tables the run plans to write and read, so an object-scope
   `DENY` on one of those tables IS caught before any write. The residual it
   still cannot see: a `DENY` on a table pulled in only at write time (outside the
   probed planned/read set), or a `DENY` applied at SCHEMA or COLUMN scope rather
   than object scope — either surfaces mid-load as a raw permission error. The
   revert script (`transfer-revert.sql`) removes the partial rows; auto revert is
   `--auto-revert`.
7. **Cosmetic**: one narrow-terminal note line can render truncated with `…`
   (a known display residual); the board and load plan are unaffected.

## What the tool touches, per environment

Source: `SELECT` on `ossys_*` (contract) + `SELECT` on the subset's `OSUSR_*`
tables (rows). Sink: the same reads, plus `INSERT`/`UPDATE` on the subset's
tables (the load + FK re-point), `DELETE` on them (`replace` wipe / revert),
`#temp` staging in tempdb, and `MERGE … OUTPUT` (key capture). Nothing needs
ALTER, CREATE TABLE, or IDENTITY_INSERT — proven by the managed-grant e2e
suite (`PeerManagedGrantTransferDockerTests`).
