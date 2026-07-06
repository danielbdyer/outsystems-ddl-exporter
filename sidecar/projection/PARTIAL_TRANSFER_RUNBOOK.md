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
script and literally watch it turn green.

The axes it judges, in order — the full forecast vocabulary:

| Axis | What it proves | Red means |
|---|---|---|
| routing | the flow rides the SsKey-aligned peer leg | renditions unset → fix step 1 |
| contracts | both OSSYS metamodels read; identities align | connection/grant problem — remedy printed |
| tables | your subset resolves (unambiguously) | typo or ambiguous name — use `Module.Entity` |
| reconcile | every reconcile entry resolves against the sink | bad entry — use `Module.Entity:Column` |
| shape | the two models are ONE shape over the transferred set | real divergence — align model versions |
| relationships | no FK escapes the subset un-strategized | **the main open decision — see step 3** |
| load order | parents-before-children is proven | an unresolvable cycle — remedy printed |
| forecast | the DRY RUN: exact row counts per table | the dry run refused — reason printed |
| cycles / identities / drops | forecast of unbreakable cycles, unmatched identities, dropped rows | fix data / user-map, or accept with `--allow-drops` at run time |
| cdc | the sink is not CDC-tracked | decide `--allow-cdc` or de-track |
| grant | the sink principal carries db-scope DML | grant the missing permission |
| re-run | your strategy is safe against the sink's ACTUAL state | duplicates (merge into populated) or wipe blockers — remedy printed |
| execute gates | (note) the two run-time gates | never red — informational |

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
   identities.
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
flag states per-run intent. Exit codes: `0` clean · `5` shape divergence · `9`
un-strategized relationships / unmatched identities / dropped rows (the report
names them; `--allow-drops` downgrades identity/drop refusals you have
deliberately accepted) · `6`/`7` connection/grant · `2` argument/spec errors.

What happens inside, in order: contracts re-acquired → gates re-checked (the
same ones the board ran) → subset wiped child-first (`strategy: replace`) →
parents loaded first, sink minting every identity → children's FKs re-pointed
through the captured old→new key map → nullable cycle FKs re-pointed by
phase-2 UPDATE → reconciled kinds' FKs re-keyed to the sink's existing rows →
the run report printed (same vocabulary as the preview).

## Step 7 — Re-run whenever you need

With `strategy: replace`, running step 6 again wipes the subset and reloads —
same result every time (proven by the idempotency e2e). Refresh QA-side data,
re-run, done. If the sink's reference data changed (reconciled tables), the
board re-validates the matches — run `check go` again first when in doubt.

---

## Troubleshooting quick table

| Symptom | Meaning | Move |
|---|---|---|
| board: `[STOP] contracts` | OSSYS metamodel unreadable | grant SELECT on `ossys_*` to the principal |
| board: `[STOP] shape` | the two environments genuinely run different model versions | deploy the same version to both, or narrow the subset |
| exit 9 at `--go` with drop lines | rows referenced identities that don't match | fix the reconcile data or accept with `--allow-drops` |
| raw SQL permission error mid-load | a TABLE-level DENY (invisible to preflight — the pinned G1 gap) | remove the DENY; the revert script (`transfer-revert.sql`) undoes sink-minted rows |
| board green but `--go` exits 7 | `PROJECTION_ALLOW_EXECUTE` not set | set it (the board's `[note]` line reminds you) |

## What the tool touches, per environment

Source: `SELECT` on `ossys_*` (contract) + `SELECT` on the subset's `OSUSR_*`
tables (rows). Sink: the same reads, plus `INSERT`/`UPDATE` on the subset's
tables (the load + FK re-point), `DELETE` on them (`replace` wipe / revert),
`#temp` staging in tempdb, and `MERGE … OUTPUT` (key capture). Nothing needs
ALTER, CREATE TABLE, or IDENTITY_INSERT — proven by the managed-grant e2e
suite (`PeerManagedGrantTransferDockerTests`).
