# Partial-Transfer Production-Readiness Log — 2026-07-06

> Append-only operator log for the partial-transfer readiness program: making the
> verb/flow that moves a **subset of tables** between managed Cloud OutSystems
> environments (e.g. QA → UAT, differing physical table names, SS_KEY-matched
> schema shape) ready for a live operator test later today. Newest entries at the
> bottom. Plain description only — findings, accomplishments, decisions taken.

---

## Entry 1 — 2026-07-06, session start: orientation

- Session opened on branch `claude/fsharp-partial-transfer-production-1p4vpw` (clean tree,
  based on `main` @ 4ba720a).
- Read `CLAUDE.md`, the top `HANDOFF.md` letter (operator-shell chapter, 2026-07-03), and
  `src/Projection.Cli/Faces/Transfer.fs` in full.
- Confirmed the feature surface already exists in some depth:
  - `runTransfer` — the forward `transfer` face: dry-run by default, `--execute` gated on
    `PROJECTION_ALLOW_EXECUTE=1`, `--tables` subset selection, `--reconcile
    <table>:<column>` + `--user-map` CSV re-keying, revert policy, resumable runs,
    drop-set fail-loud exits.
  - `runReverseLegTransfer` — the reverse (B→A) leg: takes a **logical source contract**
    and a **physical sink contract** (two SsKey-aligned renditions of one authored
    model) — this is precisely the "same schema shape, different physical table names"
    cross-environment situation. Streaming vs materialized realization chosen by
    `ReverseLegRealization.choose`; journal-gated execute.
- Directly relevant docs identified for the deep study: `THE_USE_CASE_ONTOLOGY.md` (+ its
  acceptance/fitness/obligations satellites), `PRESCOPE_TRANSFER.md`,
  `CROSS_ENVIRONMENT_READINESS.md`, `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md`,
  `PREFLIGHT_CLOUD_INSERTION.md`, `J5_MANAGED_ENV_CAPABILITY_PLAYBOOK.md`,
  `REVERSE_LEG_WORK_PLAN.md`, `AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md`.
- Next: four parallel deep-study sub-agents (engine, docs/ontology, test coverage, CLI
  dispatch + contract acquisition) to map the current state and find blind spots.

## Entry 2 — 2026-07-06, environment readiness + study fan-out

- Launched four parallel deep-study agents:
  1. **Engine** — Projection.Pipeline transfer flow, `--tables` subset semantics, SsKey
     alignment, FK closure/cycle handling, sink insertion mechanics.
  2. **Ontology/docs** — the use-case ontology + acceptance/obligations satellites,
     transfer prescope, cross-environment readiness, cloud-insertion preflight,
     reverse-leg program status, open backlog items.
  3. **Test estate** — existing transfer/e2e docker tests, two-environment fixture
     patterns, subset + schema-validation coverage, gap list with templates to copy.
  4. **CLI/config** — exact command grammar, contract acquisition (CatalogRendition,
     capture verbs), preflight/compare verbs, environment/connection story, worked
     examples.
- Local substrate verified ready for e2e work: dotnet SDK 9.0.314, Docker 29.3.1, and the
  **warm SQL Server 2022 container is already up** (`projection-mssql-warm`, port 11433).
  Full solution build kicked off in the background.

## Entry 3 — 2026-07-06, ontology/docs study returned: the use case is named, and the sharpest gap is identified

The docs deep-study came back. Plain-language summary of what the documentation establishes:

- **This exact use case is a first-class, named flow.** `THE_CLI.md` §11–12 documents the
  `golden` flow verbatim: `{ "from": "cloud-qa", "to": "cloud-uat", "scope": "data",
  "tables": ["Customer","Order"], "rekey": "file:./secrets/uat-users.csv" }` — a subset of
  a peer cell's data promoted into a cloud sink, listed as "fully backed today," with
  `check` (canary/drift/data/ready) as the operator preflight. The ontology names it
  protein **P-3 (Dev→UAT with user re-key)** generalized cloud→cloud; the producer
  taxonomy calls QA→UAT the **`peer`** producer with the **`golden`** flow.
- **Identity law:** entity/attribute identity is `SsKey` (OutSystems GUID), designation is
  logical name, physical `OSUSR_*` name is a *disposition* — comparison always by SsKey,
  never physical name. `CROSS_ENVIRONMENT_READINESS.md` designs `projection check shape`:
  OSSYS-read both environments (recovering native GUIDs), `CatalogDiff` must report zero
  delta modulo `Readiness.toLogicalShape` normalization (constraint names/triggers/column
  checks derive from physical names and must be stripped). Espace-invariance is the law
  (A45 candidate).
- **Managed-cloud capability verdict (confirmed on a real managed environment
  2026-06-15):** SELECT/INSERT/UPDATE/DELETE granted, **no ALTER, no IDENTITY_INSERT**
  (error 1088) → `PreservedFromSource` is dead on managed cloud; **`AssignedBySink`
  (sink mints keys + FK remap) is the live path**; MERGE…OUTPUT capture permitted;
  rollback channel is plain SQL DELETE.
- **THE SHARPEST GAP (blind spot #1):** the `peer`/`golden` mechanism as documented
  *assumes matching `OSUSR_*` physical names between source and sink* — but real QA/UAT
  espace naming makes prefixes differ. The cross-rendition write-target resolution that
  handles differing names (`runWithRenames` / the reverse leg's two-contract mechanism)
  was built and witnessed **only for the `legacy` (B→A) leg**, never wired or tested for
  a peer (QA→UAT) transfer with two independently-named physical contracts. This is
  precisely the scenario the operator will test today.
- **Other open items pulled from the docs** (consolidated; engine/tests studies to
  confirm which are still live at HEAD):
  1. `golden` re-key against a real cloud pair unwitnessed (canaries are offline Docker
     with matching names; user-directory probe P10 residual).
  2. Live two-independently-read-DB identity alignment: `ReadSide` synthesizes SsKeys
     from *physical* coordinates, so two live reads never align; the OSSYS-read fix
     (`check shape` slices S1–S5) is a 2026-06-21 design whose shipped status must be
     verified against code.
  3. `golden` needs an explicit `--reconcile` tail (no default User:Email from config).
  4. G1: object-scope DENY escapes the DB-scope grant preflight → mid-load partial write.
  5. G2: live-source schema drift vs contract → raw unnamed SqlException.
  6. No idempotent re-run: second Execute into a populated sink duplicates every
     `AssignedBySink` row (honest modes today: WipeAndLoad or journal-gated resume).
  7. Table-subset transfers force the materialized arm (not streaming) — throughput note.
  8. Composite/multi-column-PK and non-IDENTITY-PK tables refuse under `AssignedBySink`;
     no business-key fallback except the User kind.
  9. CDC verdict on real managed environment still PARTIAL.
- Also surfaced by the session's stop hook, independent of the docs: the **perf-gate
  Release build currently fails** with FS3511 at `SyntheticLoadRun.fs:130` on an
  untouched tree (markdown-only commit) — verifying now; if confirmed pre-existing it is
  itself a production blocker (Release binaries won't build).

## Entry 4 — 2026-07-06, Release build was broken at HEAD; fixed

- Confirmed: `dotnet build src/Projection.Pipeline -c Release` fails at HEAD (clean tree)
  with FS3511 in `SyntheticLoadRun.run` — the known Release-only state-machine
  reducibility failure (survival rule 5). Production Release binaries could not build.
- Fix applied (mechanical, per the house scars): the tuple-pattern `match` that headed
  the `task { }` now resolves before the task opens, and the pure middle (module filter →
  Faker-coordinate refusal → config fold → opt-in graph analytics) is hoisted to a
  module-level `private prepareSynthesis`. The task body keeps the reducible
  `match! → match → match! → match → return` spine. Zero behavior change intended;
  fast pool + Release build of the Integration assembly running to verify.

## Entry 5 — 2026-07-06, all four deep studies returned: the full blind-spot map

All three code-facing studies **independently converge** on the same central finding.

**What already exists and is genuinely strong:**
- The use case is named end-to-end in the target docs: the `golden` flow
  (`{ from: cloud-qa, to: cloud-uat, scope: data, tables: [...], reconcile:
  [ServiceCenter.User:Email], rekey: file:… }` in `examples/projection.sample.json`),
  invoked as `projection golden [--go]`, dry-run by default, double-gated
  (`PROJECTION_ALLOW_EXECUTE=1` + `--go`).
- **`projection check shape` is SHIPPED, not just designed** (verified directly in
  `Faces/Diff.fs:122-168` + `Projection.Pipeline/Readiness.fs`): OSSYS-reads every
  configured environment (native OutSystems GUID SsKeys — espace-safe), normalizes away
  physical-realization artifacts (`Readiness.toLogicalShape`), and requires
  `CatalogDiff` zero-delta ⇒ "one shape" across environments with different `OSUSR_*`
  names. Exit 0 ready / 5 divergence-or-dealbreaker / 6 unreadable. This satisfies the
  "SS_KEY tables+attributes must match to validate schema shape" core requirement — as a
  *manual* preflight.
- The engine's dual-catalog machinery (`runReverseLegThroughConnectionsWith`) is exactly
  the right shape for "same schema, different physical names": two SsKey-aligned
  contracts, `CatalogDiff.between` → rename map, reads with source names, writes with
  sink names, all correspondence by SsKey. Proven live in docker canaries (M3/LE-1,
  LE-2) including a genuinely differently-named table pair (`Customer` →
  `OSUSR_XF_CUSTOMER`).
- FK handling: whole-catalog topological order, two-phase deferred-FK loads, unbreakable
  cycle refusal by name, three identity dispositions; managed-cloud reality (no
  IDENTITY_INSERT, error 1088) handled via `AssignedBySink` (sink mints keys, MERGE…
  OUTPUT captures source→assigned pairs, FKs re-pointed through the remap), business-key
  reconciliation for shared kinds (Users), fail-loud drop accounting (exit 9,
  `--allow-drops` downgrade), revert scripts/auto-revert of sink-minted rows.
- Two-DB espace-invariance canary exists (`OssysComprehensiveFixtureTests`): two OSSYS
  databases, same GUIDs, `OSUSR_` vs `OSUSR_X` names → readiness sees one shape.

**THE central blind spot (all three studies, code-confirmed):** for a *peer* (QA→UAT)
transfer with genuinely differing physical names there is **no wired path**:
1. The forward `Transfer` path (which the `golden` flow routes to) reads its ONE
   contract from the **source** (`ReadSide.readSchema source`,
   `TransferRun.fs:2137`) and writes with the **source's physical names** into the sink
   — object-not-found (or worse) if UAT's `OSUSR_*` names differ.
2. The rename-capable reverse-leg path is dispatch-gated to `rendition: logical` →
   `rendition: physical` (on-prem→cloud) and its contracts are two renditions of ONE
   authored model — there is no "physical-as-deployed-at-QA" vs
   "physical-as-deployed-at-UAT" rendition.
3. Two independent `ReadSide` live reads can never align (SsKeys synthesized from
   physical coordinates) — but **OSSYS reads do align** (native GUIDs), and `check
   shape` already proves it. The missing piece is feeding two OSSYS-read,
   SsKey-aligned per-environment contracts into the existing rename-aware engine.

**Secondary blind spots (consolidated from the three code studies):**
- No schema-compatibility gate on the transfer path itself: `CatalogDiff.Reshaped`
  (type/length/nullability divergence) is computed and discarded; sink live schema
  never verified against the contract → drift = mid-load SqlException, not a named
  refusal. `check shape` exists but is not wired as a transfer precondition.
- `--tables` subset has **no FK-closure story**: an in-subset child pointing at an
  out-of-subset parent is (a) mass-dropped w/ exit 9 if the parent is IDENTITY-PK
  (`AssignedBySink` remap has no captures), (b) copied verbatim w/ orphans surfacing
  only at FK re-trust (or silently on ManagedDml) if business-PK, (c) correct only
  under a manual `--reconcile`. The espace-aware closure machinery
  (`Closure.fs`/`ClosureOracle.fs`) exists but is wired only to `slice-extract`/
  `slice-apply`, not `transfer`.
- G10 resume marker is subset-insensitive: a completed `--tables A` resumable run makes
  a later `--tables B` run a silent no-op replay.
- Grant preflight demands INSERT over the WHOLE estate even for a 2-table subset
  (`plannedTransferWrites` ignores LoadSet) — over-refusal on narrowly-granted sinks.
- `WipeAndLoad` + subset: out-of-subset children referencing wiped rows → unhandled 547
  mid-wipe.
- `--user-map` resolves tables by physical name only (espace-unsafe); `--reconcile` has
  the espace-safe `Module.Entity` form.
- Object-scope DENY invisible to the DB-scope grant probe (G1, pinned); contract-named
  column missing from live source = raw unnamed SqlException (G2, pinned/reserved).
- Streaming refuses `--tables` (named follow-on) — subsets always run materialized
  (full per-kind rows in memory).
- No argv surface for `--tables`/`--reconcile` — flow config (`projection.json`) is the
  only expressible surface (by design, A44).
- `transfer.tablesUnknown` exits 3 (unclassified) instead of the argument-error 2.
- Composite-PK `AssignedBySink` kinds refuse by name (no business-key fallback outside
  the User family) — must be surveyed per-table before a real run.

**Test-estate gaps (docker e2e), with templates identified:**
1. **No test moves DATA between two differently-named environments** — schema-shape
   equivalence is proven, data movement is not. Template: two `WithEphemeralDatabase`
   on `EphemeralContainerFixture` + the `OSUSR_`→`OSUSR_X` trick + OSSYS reads on both
   sides + `CatalogDiff.between` rename map into the rename-aware engine.
2. FK-outside-subset (`--tables` child w/ parent left behind, sink pre-populated with
   parents) — never asserted.
3. `--tables` × FK-cycle/deferred-FK combination — never asserted together.
4. Pure unit test for `resolveLoadSet` — missing entirely (cheapest gap).
5. Reserved stubs already in-tree for G1/G2 refusals (bodies empty).

**Readiness plan for today's operator test** (pending operator answers): commit the
Release fix; stand up e2e #1 (the different-physical-names data-move witness); then
either wire the peer-with-renames dispatch path or document the engine-level invocation;
propose FK-closure strategies per table for the chosen subset.
