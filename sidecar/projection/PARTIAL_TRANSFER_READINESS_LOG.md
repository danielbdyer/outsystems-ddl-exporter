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

## Entry 6 — 2026-07-06, operator decisions received; the peer leg is WIRED

Operator answered the four scoping questions: **full wiring** of the peer path;
**reconcile-by-key** as the default strategy for out-of-subset FK parents; **OSSYS
metamodel readable on both QA and UAT**; **replace-subset (WipeAndLoad)** re-run
semantics for today.

Landed since (all compiling, fast pool green):

- **`PeerTransfer` module (Pipeline)** — the peer leg's support: `acquireContracts`
  (both sides read from their own OSSYS metamodel → native GUID SsKeys, the identity
  that aligns across environments); `shapeVerdict`/`shapeGate` (SS_KEY-keyed schema
  compatibility scoped to the kinds the run touches: kind/attribute presence and
  column-shape facets BLOCK by name; constraint/index drift and widenings surface as
  advisories — nothing silent); `escapingFks` + `narrateEscapes` + `subsetFkGate`
  (every FK edge escaping the declared subset detected, per-edge strategy proposed —
  reconcile against the sink's own rows [candidate business-key columns derived from
  the target's single-column unique indexes], widen the subset, or --allow-drops; a
  live Execute with un-strategized escapes refuses by name, a preview narrates).
- **Routing**: `MovementDirection.UpPeer` (a physical→physical env→env flow, i.e. the
  `golden` QA→UAT shape) now routes to the new `PlanAction.TransferPeer` → the new
  `runPeerTransfer` CLI face → gates → the SAME rename-aware contract-pair engine the
  reverse leg proved (`runReverseLegThroughConnectionsWith` path). An env→env flow
  with UNSET renditions keeps the old name-blind `Transfer` (the identical-rendition
  escape hatch). Two new named refusal axes with distinct exits:
  `transfer.peer.shapeDivergence` → exit 5 (matching `check shape`'s verdict class),
  `transfer.peer.subsetFkEscapes` → exit 9 (drop-set class); unreadable OSSYS
  metamodel → schema-read axis, exit 6.
- **Routing tests updated** (the two pins that moved) + a new pin: unset-rendition
  env→env still routes to plain `Transfer`.

## Entry 7 — 2026-07-06, THE E2E TEST FOUND A REAL ENGINE BUG (the reason this test had to exist)

Stood up `PeerAlignedTransferDockerTests` (Integration, Docker-SqlServer): two real
databases on the warm container — cell A = the edge-case OSSYS estate (`OSUSR_*`),
cell B = the same estate with every physical name shifted (`OSUSR_X*`), contracts
OSSYS-read from each side, subset transfer executed through the contract-pair engine.

**First run failed — and the failure is a genuine, previously-invisible engine defect,
not a test bug:** the edge-case estate contains a self-referencing entity
(`Category.ParentCategoryId`, nullable). The cycle resolver
(`CycleResolution.asymmetric2CycleStrategy`) only handled 2-cycles; a 1-node SCC
(self-FK) REFUSED resolution, and the topological-order pass then degraded the WHOLE
catalog to its alphabetical fallback. Consequence: `Customer` (alphabetically before
`City`) loaded BEFORE its FK parent, the sink-minted-key remap for City was empty at
Customer's load time, and every Customer row's CityId FK missed → skip-and-diagnose
dropped/diagnosed the rows (exit 9). **One nullable self-reference anywhere in the
estate silently poisoned the load order of every unrelated table pair.** Real
OutSystems estates are full of this shape (Category.Parent, Employee.Manager,
Folder.Parent) — the operator's QA→UAT test today would almost certainly have hit it.

**Fix (engine, minimal and targeted):** the resolver now treats a **Weak (nullable)
self-edge** as breakable *for ordering* — the two-phase load's deferral machinery
already re-points nullable self-FKs in phase 2 regardless of order (the F1 lift,
2026-06-10), and the resolved SCC stays in `Cycles`, so `deferredFkColumns` still
defers the column. A MANDATORY self-FK still refuses (honest — it genuinely cannot
load in one pass). Pure resolver tests added for both arms.

Also new pure coverage (`PeerTransferTests`): `resolveLoadSet` refusal semantics
(previously untested anywhere), escaping-FK detection incl. nullable-edge softening +
reconcile-candidate proposals + chain-stops-at-boundary, subset-FK gate
refuse/downgrade contract, shape-gate blocking vs advisory classification (missing
kind/attribute, type change, widening, nullability tightening/loosening, scoping), and
the two new Preflight exits.

## Entry 8 — 2026-07-06, the self-FK fix landed in two halves; ALL THREE e2e scenarios GREEN

The resolver rule alone was not enough — a second, deeper defect sat beneath it:
`internalEdgesOf` (the topo pass's SCC edge enumerator) explicitly excluded self-pairs
(`if a <> b`), so a 1-member SCC's self-edge was invisible to ANY resolver by
construction. Both halves are now fixed: self-pairs are enumerated, the resolver
breaks a Weak (nullable) self-edge for ordering (deferral still re-points it in
phase 2 — the resolved SCC stays in `Cycles`), and the 2-cycle arm explicitly ignores
self-edges so its semantics are byte-preserved. A mandatory self-FK still refuses.

One test-side correction along the way (the engine was right, the assertion wrong):
`DBCC CHECKIDENT(…, RESEED, 500)` on a never-inserted table makes the NEXT minted
value 500, not 501 — the verbatim-FK check now tests "not the source's surrogates +
no dangling FK" instead of a numeric boundary.

**The peer e2e suite is GREEN (real SQL Server, two espace-variant databases,
`OSUSR_*` vs `OSUSR_X*`):**
1. **A declared table subset lands in the differently-named sink** — contracts
   OSSYS-read from each side, SsKeys align by native GUID, City loads before Customer
   (topological, despite the estate's self-FK Category), sink mints keys, the
   Customer→City FK re-points through the capture remap (source surrogates provably
   absent), the (email → city-name) join identical source↔sink, zero drops.
2. **Reconcile-by-key for an out-of-subset parent** — subset [Customer] only, City
   reconciled `MatchByColumn NAME` against rows the sink already held under its own
   surrogates (501/502): City written 0 rows, disposition re-keyed-by-rule, both
   customers point at the sink's own city rows. This is the operator's chosen default
   strategy for partial refresh, now witnessed end-to-end.
3. **Replace-subset idempotency** — WipeAndLoad × 2 over the subset: counts stable,
   join stable, no duplicates.

Full fast + docker pools running for regression coverage of the resolver change.

## Entry 9 — 2026-07-06, the operator runbook for today's QA→UAT partial-transfer test

**Config (`projection.json`)** — both environments declared `rendition: physical`
(this is what routes the flow onto the new SsKey-aligned peer leg; leaving renditions
unset keeps the old name-blind transfer as an escape hatch):

```jsonc
{
  "environments": {
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN",  "rendition": "physical" },
    "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data",
                   "rendition": "physical", "archetype": "managed-dml" }
  },
  "flows": {
    "golden": { "from": "cloud-qa", "to": "cloud-uat", "scope": "data",
                "tables": ["Customer", "Order"],
                "reconcile": ["ServiceCenter.User:Email"],
                "rekey": "file:./secrets/uat-users.csv",
                "strategy": "replace" }
  },
  "readiness": { "schema": "cloud-qa", "confirm": ["cloud-uat"] }
}
```

**The run sequence:**
1. `projection check shape` — the espace-safe estate readiness gate (OSSYS-reads
   both environments, SS_KEY-keyed, zero-delta-modulo-physical required). Exit 0 =
   ready; 5 = real divergence/data dealbreaker; 6 = unreadable.
2. `projection golden` — the PREVIEW (dry-run is the default): narrates the load
   plan in dependency order, every escaping-FK edge with its proposed strategies
   (reconcile candidates derived from the target's unique business keys), unmatched
   identities, cycles. No writes.
3. `PROJECTION_ALLOW_EXECUTE=1 projection golden --go` — the live run (both gates:
   the env var authorizes the environment, `--go` states per-run intent).
   `strategy: replace` = wipe-the-subset-then-load (the chosen idempotent re-run
   semantics). Exit 0 clean; 9 = rows dropped or escapes un-strategized (add
   reconcile entries or `--allow-drops` after reading the report); 5 = shape
   divergence; 6/7 = connection/grant.

**What the new gates do for you:** contract acquisition reads each side's OSSYS
metamodel (native GUID SS_KEYs) — QA's and UAT's `OSUSR_*` names may differ freely;
the shape gate refuses by name if the models genuinely diverge over the transferred
set (attribute presence/type/nullability/length), with advisories for benign drift;
the subset-FK gate refuses a live run while any relationship escapes the subset
without a strategy. Reconciled kinds (`reconcile:` / `rekey:`) match rows the sink
already holds by business key — nothing is inserted into them; FKs re-point.

**Known residuals that could still bite today (all pre-existing, none new):**
- Object-scope DENY is invisible to the DB-scope grant preflight (G1) — a per-table
  DENY surfaces mid-load, not pre-flight.
- Contract-vs-live drift: a model column missing from the deployed table is a raw
  SqlException, not a named refusal (G2). `check shape` first mitigates.
- Grant preflight demands INSERT over every model kind, not just the subset —
  over-refusal possible on narrowly-granted sinks.
- The G10 resume marker is subset-insensitive (irrelevant under `strategy: replace`).
- `--user-map` CSV resolves tables by physical name (espace-unsafe); prefer the
  `Module.Entity` reconcile form.
- Composite-PK kinds refuse under sink-minted identity (named refusal, no
  business-key fallback except via reconcile).
- Streaming refuses `--tables` (subsets run materialized — memory scales with the
  largest table in the subset).

## Entry 10 — 2026-07-06, FINAL VERDICT: every gate green; the program is complete

- **Fast pool: PASSED** (4,100+ tests, 37s) — including the new `PeerTransferTests`,
  the self-loop resolver rules, and the re-based composer parallelism pins (the
  unresolvable-cycle fixture moved to the honest two-weak-edge 2-cycle; a NEW pin
  proves a nullable self-FK no longer surrenders the estate's parallel levels).
- **Docker pool: PASSED in full** (511s) — zero regressions from the cycle-resolver
  change across the whole transfer/reverse-leg/migration/deploy canary estate, plus
  the three new peer e2e scenarios green.
- **Release builds: clean** (both test assemblies + transitively every src project) —
  including the FS3511 fix from entry 4; production binaries build again.

**What today's session delivered, end to end:**
1. The blind-spot map (entries 3, 5) — four deep studies, all findings logged.
2. The Release-build fix (entry 4).
3. The peer (QA→UAT) SsKey-aligned partial-transfer leg: OSSYS contract acquisition
   per side, shape gate, subset-FK gate with strategy proposals, dispatch wiring,
   voice/exit-code axes (entries 6, 9).
4. A REAL engine bug found by the new e2e canary and fixed in two halves (entries
   7–8): self-FK 1-node SCCs no longer silently degrade the entire load order.
5. The e2e proof: data moves between two genuinely differently-named environments,
   SS_KEY-aligned, FKs re-pointed, reconcile-by-key for out-of-subset parents,
   idempotent replace-subset re-runs.
6. The operator runbook for today's live test (entry 9), incl. the named residuals
   that remain (all pre-existing, none blocking the happy path).

## Entry 11 — 2026-07-06, PHASE 2 OPENED: adversarial hardening + mock-environment e2e program

Operator directive: critical analysis to find remaining bugs, make the flow easier,
and build much more robust end-to-end testing with mock OutSystems environments that
confirm our understanding of the permissions and the tables we interact with.

Four deep adversarial agents launched in parallel:
1. **Peer-leg correctness hunt** — priority: physical COLUMN-name divergence between
   environments (OutSystems keeps stale physical names after renames — does the
   rename projection actually cover the column plane, or only tables?); shape-gate
   blind spots; reconcile-resolution asymmetries; wipe-order hazards; second-order
   effects of the self-loop resolver rule; face gate holes; metamodel-vs-deployed
   type fidelity on the bulk write plane.
2. **Permission-footprint inventory** — every SQL statement the peer path executes,
   per phase, with its minimum permission and its behavior when denied (named
   refusal / named descent / raw exception); the mock managed-grant recipe; the
   mid-load-surprise list.
3. **Flow ergonomics** — ranked proposals (J2 default User:Email reconcile,
   reconcile-spec SsKey translation, preview closure sizing, config validation
   notes, voice copy).
4. **Mock-environment fixture + e2e matrix design** — parameterized espace prefixes
   (not string-replace), the DML-only principal recipe, P0/P1/P2 scenario matrix.

Known self-critique going in (to be verified by agent 1): today's peer e2e runs as
an ADMIN principal — it proves identity alignment and FK re-pointing but NOT the
managed-cloud grant envelope; the FK-retrust descent path
(FkTrustNotRestoredOnBulkLoad) is unexercised on the peer leg; IDENTITY reseed
mechanics differ under a real grant.

## Entry 12 — 2026-07-06, phase-2 findings triaged and FIXED (two critical, three high, five medium)

The four agents returned. Every confirmed finding fixed this session, each with tests:

**CRITICAL (both confirmed, both fixed):**
1. **The rename map was kind-BLIND.** `RenameProjection` flattened per-kind attribute
   renames into ONE `Map<Name,Name>` applied to EVERY kind's rows: renaming
   `Invoice.Status → State` silently re-keyed `Order.Status` too — Order's value became
   unreachable at the sink getter and the column wrote NULL. Now KIND-SCOPED
   (`renameMapByKind` / `forKind`) through the materialized repoint, the streaming
   basis, phase-2 projections, and the reconcile ingest; the flat API is deleted;
   regression pins added. (This poisoning existed on the REVERSE LEG too — pre-existing,
   now dead everywhere.)
2. **`--allow-drops` on escaping subset FKs did not drop — it cross-wired.** No engine
   code drops/NULLs an FK to an out-of-subset un-reconciled kind; the rows landed
   carrying the SOURCE environment's surrogate values, silently pointing at whatever
   sink rows own those keys (exit 0, empty drop report). The bypass is REMOVED: a live
   run refuses (`transfer.peer.subsetFkEscapes`) until the target is reconciled or the
   subset widened; the narration now proposes the copy-pasteable
   `Module.Entity:Column` reconcile form.

**HIGH (fixed):** sink-only attributes are now OMITTED from the bulk column list (the
sink's default genuinely applies; before, `KeepNulls` pinned an explicit NULL and a
mandatory sink-only column crashed raw); the shape gate's Length arm blocks
open-ended-source→bounded-sink (was misread as "wider"); Precision/Scale block only a
narrowing (was a false refusal on widening); Nullability judges the COLUMN plane (the
facet's own vocabulary).

**MEDIUM (fixed):** materialized `--resumable` against a managed-DML sink now refuses
BY NAME (`transfer.reverseLeg.resumableSinkUnsupported`) instead of dying raw on the
progress table's CREATE TABLE (only the streaming arm was archetype-guarded);
`resolveLoadSet` refuses duplicate logical names as ambiguous and accepts
`Module.Entity` (was silent last-wins); the peer face refuses a bad reconcile spec
BEFORE the gates (exit 2 — was mis-blamed as exit-9 escapes); parallel FK edges
between one pair (Employee.Manager + Employee.Mentor) no longer wedge the topo order
via a stale indegree (strength combines: breakable only if ALL parallel edges are
weak); a live Execute on an ALPHABETICAL (degraded) load order refuses by name
(`transfer.loadOrderUnproven`) instead of loading children before parents.

**Ergonomics landed** (from the ranked list): peer refusals render through the GATE
surface (statement + next move; was the flat GenericStop wall); shape advisories +
escape proposals land on STDOUT (a redirected preview no longer silently loses the
safety info); env→env data flows with UNSET renditions get a voiced note naming the
name-blind assumption and the `rendition: physical` fix; `--user-map` accepts the
espace-safe `Module.Entity` form; the flow menu shows `reconcile:` tags; refusal
messages hint the logical form for peer transfers.

## Entry 13 — 2026-07-06, THE MOCK-ENVIRONMENT PROGRAM: managed-grant e2e landed, and it caught a live-test-killing bug in hour one

New fixtures: `OssysSeedBuilder` (espace-key parameterization — a named transform, not
a string hack), `DmlPrincipal` (the managed-cloud principal: EXPLICIT db-scope
SELECT/INSERT/UPDATE/DELETE — deliberately not db_datareader/writer, whose rights
don't surface in `fn_my_permissions` and would false-trip the grant preflight), and
`MockOutSystemsEnv` (metamodel + espace-prefixed physical tables + optional managed
principal; single or paired cells).

`PeerManagedGrantTransferDockerTests` — five scenarios, ALL GREEN:
1. **Grant conformance probe** — the principal's permission evidence is exactly what
   the engine's preflight reads; IDENTITY_INSERT/CREATE TABLE/ALTER fail in their
   documented error classes; #temp staging and MERGE…OUTPUT succeed. (Also pinned: SQL
   Server 2022 auto-grants two VIEW ANY COLUMN * KEY DEFINITION rows to every user —
   presence/absence assertions, not set equality.)
2. **The peer subset happy path with DML-only principals on BOTH sides** — including
   contract acquisition through the restricted logins. Plus a genuine mechanics
   finding: FK-targeted kinds ride the MERGE capture lane which validates constraints
   INLINE — the sink FK ends enabled AND TRUSTED, no ALTER ever needed. (The
   bulk-lane untrusted tolerance belongs to non-FK-targeted kinds.)
3. **Reconcile-by-key under the grant.**
4. **Unreadable sink metamodel → named schema-read refusal (exit 6).**
5. **G1 pinned on the peer dispatch** — object-scope DENY INSERT is invisible to the
   DB-scope preflight: raw permission exception mid-load, the parent kind already
   landed (partial write). Cross-references the reserved promotion stub.

**THE BIG CATCH (would have killed today's live test at step one):** the OSSYS
metamodel extraction FAILED ENTIRELY under the managed grant —
`sys.check_constraints.definition` is NULL without VIEW DEFINITION, and the
`columnChecks` rowset reader treated it as required (`adapter.ossysSql.rowMapping`,
whole read dead). Fixed: the definition is optional through the whole chain; a
definition-less check row is SKIPPED (a named erasure — ColumnChecks are
physical-realization artifacts the shape verdict strips and the data plane never
reads; privileged reads still carry them). Also: adapter-level extraction failures now
classify onto the schema-read axis (exit 6) instead of unclassified-3.

## Entry 14 — 2026-07-06, THE SEMANTICS WALKTHROUGH (plain shorthand, post-fix state)

**The one-line model.** Every entity and attribute has a GUID (SS_KEY) that survives
environments; names — logical and physical — are costumes over that GUID. The transfer
moves rows by GUID and only touches names at two well-defined seams.

**1. Contracts.** Run start: read each environment's OSSYS metamodel (plain SELECTs on
ossys_* tables over that env's own connection). Result per side: a catalog where every
kind/attribute carries its GUID + THAT environment's names (logical AND physical).
Nothing is authored or assumed — each side describes itself.

**2. Physical names never cross the wire.** Reads SELECT using the SOURCE catalog's
physical table/column names; writes INSERT/MERGE using the SINK catalog's physical
names; the two sides pair up per kind by GUID. So QA's `OSUSR_ABC_CUSTOMER` vs UAT's
`OSUSR_XYZ_CUSTOMER` is a non-event — there is no mapping table, no convention, no
string transform; each side just uses its own physicals.

**3. Renames — the exact role, when, and where.** In flight, a row is a bag of values
keyed by LOGICAL attribute name (not physical, not position). Normal case: both
environments carry the same logical names (same model version) → the rename machinery
is a no-op (empty map). It exists for ONE situation: the same attribute (same GUID)
has DIFFERENT logical names in the two environments — e.g. you renamed
`Customer.Email → ContactEmail` in QA and haven't promoted to UAT. WHEN: computed once
at run start — diff the two contracts; every same-GUID/different-logical-name
attribute yields a source-name → sink-name entry, grouped BY KIND (today's critical
fix: one kind's rename can no longer touch another kind's same-named column). WHERE:
applied exactly once per row, between ingest and plan-build (materialized: re-key the
row's bag; streaming: re-key the stream's header once) — after that, everything
downstream speaks sink vocabulary. Entity-level renames need nothing at all
(everything is GUID-keyed); the only name-sensitive input is YOUR `tables:` list,
which resolves against the SOURCE's logical names (`Module.Entity` if ambiguous).

**4. Gates (before any write).** SHAPE: diff the two contracts by GUID with
physical-realization artifacts stripped; blocks only what breaks insertability
(missing kind/attribute in scope, type change, narrowing, NULL→NOT-NULL); everything
else is a printed advisory. SUBSET-FK: every FK from a chosen table to an un-chosen,
un-reconciled table must get a strategy — reconcile it against the sink's own rows
(proposal printed as a paste-able `Module.Entity:Column`) or widen the subset. No
bypass: those rows would otherwise land carrying QA's key values pointing at the
wrong UAT rows.

**5. The load.** Order = FK topology (parents first; verified, or the run refuses).
The sink MINTS every identity (managed cloud forbids IDENTITY_INSERT): each parent
row inserts via a MERGE that captures old-key → new-key; children's FK values re-point
through that capture before their own insert. Nullable FKs inside a cycle load NULL
first, then a phase-2 UPDATE re-points them. Reconciled kinds (Users, reference data)
are never inserted — their source rows match sink rows by the business key you named,
and FKs re-key to the SINK's existing identities.

**6. Re-runs.** `strategy: replace` wipes the subset's sink rows child-first, then
reloads — run it twice, same result.

**7. Permissions (now proven, not assumed).** Everything above fits database-scope
SELECT/INSERT/UPDATE/DELETE plus tempdb #staging — the whole flow runs green with
that exact principal on BOTH sides, including the metamodel reads. One read-side
subtlety found and absorbed: without VIEW DEFINITION the server hides check-constraint
bodies; those are now skipped as a named erasure (they never affect data transfer).
The one true blind spot that remains: a table-level DENY is invisible to the
preflight and surfaces mid-load (G1, pinned with a test).

## Entry 15 — 2026-07-06, PHASE 2 CLOSED: every gate green

- **Fast pool: PASSED** (45s) — all phase-2 fixes + updated pins + new pure suites.
- **Docker pool: PASSED IN FULL** (536s; +25s over baseline = the five new
  managed-grant scenarios, inside the predicted budget) — zero regressions from the
  rename-map kind-scoping, the topo parallel-edge dedupe, the ordered-load gate, the
  resumable capability gate, the cell-shaping sink-only omission, or the
  check-definition optionality, across the entire transfer / reverse-leg / migration /
  deploy / CDC canary estate.
- Both peer suites green: identity/mechanics (admin) AND the managed-grant envelope
  (restricted principals both sides).

**Phase-2 ledger, summarized:** 2 critical + 3 high + 5 medium engine/gate bugs found
by adversarial review and fixed with tests; 6 ergonomics improvements; the
mock-OutSystems-environment fixture stack; 5 managed-grant e2e scenarios; 1
live-test-killing extraction bug caught BY the new mock environments in their first
hour (check-constraint definitions under VIEW-DEFINITION-less principals); the
semantics walkthrough (entry 14) for operator validation.

## Entry 16 — 2026-07-06, THE PREVIEW ENGINE: the go board (red→green), the dry-run forecast, and the runbook

Operator directive: all forecast capabilities in one place, a dry run, CLI-visible
open-decision flagging, and a validateable red-until-configured → green model, plus a
stepwise end-to-end explanation.

**Landed — `projection check go <flow>` (THE GO BOARD):**
- One typed checklist (`GoBoard`, Pipeline) judging every axis a live run hits:
  routing, contracts, tables, reconcile, shape (+advisories), relationships
  (escaping-FK open decisions with paste-able remedies), load order, **the engine
  DRY RUN** (real reads, zero writes → exact per-table row forecast, unmatched
  identities, drop forecast, unbreakable cycles), CDC posture, grant evidence
  (+the standing G1 note), re-run semantics judged against the sink's ACTUAL state
  (merge-into-populated duplicates; replace wipe-blockers probed), and the run-time
  execute gates as a note.
- Every red line carries the reason AND the exact remedy. Verdict is total:
  GREEN ⇔ zero red. **Exit 0 green / 5 red — CI-able**: wire it red, fix the named
  decisions, watch it turn green.
- The check derives the flow through the SAME `planFlow` path a real run takes
  (A44: the check and the run cannot drift); previews print a pointer to the board.

**Proven:**
- Pure: verdict algebra, render marks/remedy/detail/next-move.
- Docker e2e (`GoBoardDockerTests`, managed-grant principals both sides): an
  unconfigured flow (escaping FK) is RED exit 5 with the named decision; adding the
  proposed reconcile turns it GREEN exit 0 (with the dry-run forecast: "Customer: 2
  row(s) will transfer (assigned by the target)"); a real sink-metamodel divergence
  turns it RED again; the sink stays byte-untouched throughout (the dry run never
  writes).
- Fast pool + the full peer/board docker sweep green.

**The stepwise end-to-end guide** is now a first-class doc:
`PARTIAL_TRANSFER_RUNBOOK.md` — configure → check go (red) → resolve each named
decision → green → preview → execute → idempotent re-run, with the axis table, the
decision playbook, exit codes, troubleshooting, and the exact per-environment
permission footprint.

**Final verdict:** fast pool PASSED; the full docker pool PASSED IN FULL (536s) with
the go-board e2e aboard. The preview-engine program is closed.

## Entry 17 — 2026-07-06, THE LIVE REHEARSAL: the runbook executed verbatim with the real CLI; two hotspots fixed

Operator directive: identify likely-failure hotspots and course-correct; iterate the
runbook critically, step by step.

**What ran** (a mock QA/UAT pair on the warm container: espace-shifted physical names
`OSUSR_*` vs `OSUSR_X*`, DML-only logins on BOTH sides, the operator's exact
`projection.json` from the runbook, the REAL CLI binary — the first time the whole
config-discovery → flow-parse → dispatch → engine path was driven end-to-end):

1. `projection` (menu) — flows table with the tables tag. ✓
2. `projection check go golden` — **RED exit 5**: both escapes named
   (Customer.CityId→City, SalesOrder.CategoryId→Category) with paste-able remedies;
   forecast "4 row(s) across 2 table(s)". ✓
3. Reconcile entries pasted → **RED again, and rightly**: the identities forecast
   caught that UAT held NO Category rows ("Category source '1'/'2' have no sink
   match — a live run halts before any write") — the exact missing-reference-data
   trap a live first run would hit, caught with zero writes. ✓
4. Sink reference data fixed → **GREEN exit 0**. ✓
5. `projection golden` (preview) → **HOTSPOT #1 FIXED**: the headline counted
   RowsWritten (0 in a dry run) — "0 row(s) would move" over a 4-row forecast.
   A DryRun headline now counts RowsIngested ("4 row(s) would move"). **HOTSPOT #2
   FIXED**: the load plan printed all 15 modeled tables (13 all-zero noise); it now
   shows the TOUCHED tables (moving or reconciled) + one collapse line for the rest.
6. `--go` without PROJECTION_ALLOW_EXECUTE → named refusal exit 7 with remedy. ✓
7. The live run → 4 rows moved; ground truth verified by SQL: customers minted
   900/901 with CITYID re-pointed to the sink's OWN 501/502 (name-reconciled),
   orders minted 9000/9001 pointing at the NEW customer keys with categories
   name-matched to the right rows (Leaf→Leaf, not positional). ✓
8. Re-run → idempotent (counts stable, zero dangling FKs); source untouched. ✓

**Also fixed en route:** the seed needs QUOTED_IDENTIFIER ON under raw sqlcmd
(test-infra only, engine unaffected).

**Course-corrections written into the runbook** — a new "Live-environment hotspots"
section covering what the rehearsal cannot prove: User-entity references (the gate
cannot see an edge whose target kind is absent from the contract — check manually),
materialized-subset memory at scale, wipe duration on large re-runs, Encrypt=True on
real connections, command timeouts on slow links, the standing G1 DENY blind spot,
and the cosmetic note-truncation. Plus the "Rehearsed end-to-end" section recording
exactly what was validated.

Fast pool + the peer/managed-grant/go-board docker sweep: GREEN after the narration
fixes.

## Entry 18 — 2026-07-06, THE PROVING LOOP: success-undo artifacts + `projection revert`

Operator directive: live revert commands AND written revert scripts, so a small
declared subset can be transferred, proven, and deliberately reverted.

**What existed:** revert was FAILURE-COMPENSATION only — `transfer-revert.sql` was
written (or auto-executed) when a run crashed mid-load; a SUCCESSFUL run left no
undo artifact, and no verb could execute one.

**What landed:**
1. **The success-undo artifact.** Every successful Execute now writes
   `transfer-undo.sql` into the revert dir (default: cwd) — the same precise
   child-first DELETE-by-captured-key script, targeting exactly the rows THIS run
   minted; pre-existing/reconciled rows are never in it. Written by the engine's
   success tail (`writeUndoArtifact` beside `writePlan`); distinct filename from the
   failed-run compensation so the two never shadow each other. The run's closing
   narration prints the path + the revert command.
2. **`projection revert [--script <p>] --against <env> [--go]`** — the deliberate
   undo verb (Intent → planRevert → face). Preview by default (tables + captured-key
   counts, zero deletes); a live run needs the env gate + `--go` and executes in ONE
   transaction (all deletes land or none — a failure rolls back and says so), with
   per-table rows-deleted narration. Exit: 0 / 2 (script missing/args) / 6 (conn) /
   7 (gate) / 3 (rolled back).
3. **Proven live with the real CLI** (the rehearsal pair, DML-only principals):
   transfer --go → `Undo script written: ./transfer-undo.sql` → revert preview
   (2 statements over 2 tables, 2 keys each, no deletes) → revert --go →
   "Reverted — 4 row(s) deleted" → sink counts back to zero, pre-existing cities +
   categories untouched. And pinned durably: the `proving loop` docker e2e
   (GoBoardDockerTests) runs transfer → artifact → face preview → face live revert →
   pre-transfer state, under managed-grant principals.
4. **Runbook Step 6½** documents the loop + the two honesty notes: a
   replace-strategy WIPE is not undone (the undo removes what the run minted), and
   the undo should run before new app activity references the minted rows (an FK
   from a newer row rolls the transaction back, loudly).

Fast pool + the transfer/revert docker sweep (GoBoard + both peer suites +
ReverseLegCanary): GREEN.

## Entry 19 — 2026-07-06, THE PARITY SWEEP: every leg inherits this session's protections

Operator directive: keep the other reverse-leg flows in feature parity.

The audit (the full matrix is in the PR): most of the session's fixes were already
engine-shared (kind-scoped renames, cell shaping, resolver semantics, narration,
undo artifacts via the shared `writePlan`). Three genuine gaps found and closed:

1. **The escaping-FK guard was FACE-only.** The legacy reverse leg and the forward
   transfer accept `tables` subsets and reached the engine with the cross-wiring
   hazard unguarded (the phase-2 CRITICAL-#2 class). New: `Transfer.subsetEscapeGate`
   — a leg-neutral Execute pre-write gate (`transfer.subsetFkEscapes`, same exit-9
   axis) in `runCore`'s chain, making the hazard unreachable from ANY entry point.
   The peer face keeps its richer per-edge strategy narration in front of it.
2. **The streaming arm lacked `orderedLoadGate`** — the one Execute path that could
   still load on a degraded (alphabetical) order. Wired into its pre-write chain
   (executeGate → orderedLoadGate → validateUserMap).
3. **The forward transfer's `--resumable` had no capability gate** — the same raw
   CREATE TABLE crash on a managed sink the reverse selector already refused. The
   face now refuses by the same name (`transfer.reverseLeg.resumableSinkUnsupported`),
   with the sink capability threaded from the flow's declared archetype.

Named non-goals (documented, not silent): the go board stays env→env-scoped (legacy
model-sourced flows keep their probe sheet; wiring the board there is clean
follow-on); streaming success-undo derives from the capture journal when needed
(estate-scale key lists don't belong in a .sql artifact; streaming has no `--tables`
so it sits outside the small-sample proving loop).

Pure pins added (`subsetEscapeGate` semantics + exit-9 classification). Fast pool
GREEN; **the full docker pool PASSED IN FULL (519s)** — every leg's canaries
(forward transfer, legacy reverse, streaming, peer, managed-grant, go-board,
proving-loop) green through the new gates. The parity sweep is closed.

## Entry 20 — 2026-07-06, THE FINAL PASS: two Opus critiques applied (refactor + DX)

Operator directive: identify duplication/refactoring needs and polish DX before
moving on. Two Opus critics swept the session's ~4,400-line diff; every accepted
finding applied, the explicit not-dos honored.

**Refactoring (the duplication the session created, now paid down):**
- `ConnectionRef.Raw` (with its DECISIONS amendment): the in-memory carrier for an
  already-resolved secret. Kills the temp-file-secret dance at FOUR sites (the go
  board's dry run + three test fixtures) — the workaround was persisting the secret
  to disk, strictly worse than the D9 discipline it dodged. `Raw` never round-trips
  into config (maps to the `live:` spec form only).
- `TransferSubset.escapingEdges`: the ONE escaping-relationship traversal, consumed
  by both the engine backstop and the peer face's rich detector — the two
  predicates can no longer drift (board-green-while-engine-refuses is structurally
  impossible).
- `parseReconcileInputs`: four near-identical reconcile/user-map parse blocks in the
  faces collapsed to one helper (the peer face's deliberate defer-missing-file
  divergence preserved as a named flag).
- The `DmlPrincipal` promotion FINISHED: `ReverseLegCanaryTests`' private duplicate
  deleted, eight call sites swapped to the shared module.
- `PeerAlignedTransferDockerTests`' blind `String.Replace` espace shift replaced
  with `OssysSeedBuilder.withEspaceKey`; both peer suites' `throughConnections` ride
  `Raw`.
- Explicit NOT-DOs (per the critique, with reasons on record): no rename of
  `transfer.reverseLeg.resumableSinkUnsupported` (operator-facing compat > cosmetic
  accuracy); no `runCheckGo` preamble decomposition (the linear cascade IS the
  clearest form); no View-ification of the board render (deliberate report format;
  pure core already split).

**DX (the operator-surface polish):**
- **THE WRONG-SINK REVERT GUARD** (the critique's top finding): every undo/revert
  artifact now carries a provenance header naming the database its keys were
  captured against; `projection revert` refuses by name (`revert.sinkMismatch`,
  exit 7) when `--against` resolves elsewhere — `--force` is the deliberate
  override; a header-less (pre-stamp) artifact proceeds with a printed note.
  Pinned in the proving-loop e2e (pointing the undo at the SOURCE refuses).
- **`check go --format json`**: the board's typed structure serializes
  (`{verdict, redCount, items:[{axis, status, headline, remedy, detail}]}`) — the
  CI-able claim now has a machine-readable surface beyond the exit code. Pure pin
  added; runbook notes the board's exit-5 vs the live run's 5/9 refinement.
- Copy: "no reconcile rules declared yet" (the green-at-zero contradiction gone);
  "modulo" de-jargoned; revert preview says "row(s) to delete" (not "captured
  key(s)"); a second revert says "Nothing to revert" instead of feigning deletion;
  escape proposals lead with the paste-able move (truncation-safe); ONE name — "the
  go board" — everywhere.

Fast pool + the full peer/managed/go-board/reverse-leg docker sweep: GREEN — and
**the full docker pool PASSED IN FULL (510s)** with every final-pass change aboard.
The final pass is closed; the branch is ready for live operation.

## Entry 21 — 2026-07-06, THE SINGLE-OWNER PIN: one designated sink row owns every reference

Operator directive (pre-live-test): the moved subset is configuration-domain tables
whose every reference should belong to ONE designated user/row in the sink —
first-class, with fallback to the dynamic matching when no pin is provided.

**Landed — three rule forms, ONE grammar (flow `reconcile:` / `--reconcile`):**
- `Module.Entity:Column` — dynamic match by business column (the incumbent; the
  fallback when no pin is given — unchanged).
- `Module.Entity:=1234` — **the single-owner pin**: every source reference re-keys
  to the ONE sink row `1234`. No matching at all.
- `Module.Entity:Column:=1234` — dynamic match FIRST; the pinned owner catches
  every row the match misses (the graceful composite).

**Zero new engine cases:** the pin forms map onto the EXISTING strategy algebra —
`FallbackToAssigned(key, ManualOverride ∅)` (a match-nothing primary: everything
falls to the owner) and `FallbackToAssigned(key, MatchByColumn col)`. The gates,
the board, the escape coverage, and the wipe-exclusion all inherit through the same
reconciliation map.

**The safety half:** a pinned key that names NO sink row would dangle every
re-keyed reference (a raw 547 mid-load). Now: `reconcileKind` surfaces
`MissingPinnedOwners`; `Transfer.validatePinnedOwners` refuses BY NAME
(`transfer.reconcile.pinnedOwnerMissing`, the reconcile class — NOT downgradable by
--allow-drops: the rule is wrong, not the data) on BOTH preWrite chains
(materialized + streaming); and the go board gains a `pinned owners` axis that
probes every pin against the live sink — the missing-owner case is an early red
line, not an execute-time halt.

**Proven:** pure (parse forms + refusals; resolve→algebra mapping; pin-all re-keys
every source identity with nothing unmatched; match-then-pin splits correctly;
missing owner surfaces + the named refusal) and docker e2e
(`PeerAlignedTransferDockerTests`, 4th scenario): `AppCore.City:=501` re-keys BOTH
customers to the one pinned sink city — including the source row whose city was a
DIFFERENT city — zero city rows written; `:=9999` refuses by name pre-write, sink
byte-untouched. Runbook Step 3 + usage carry the forms. Fast pool + the full peer
docker sweep GREEN.

**Final verdict:** the full docker pool PASSED IN FULL (484s) with the pin aboard.
Ready for the live test.

## Entry 22 — 2026-07-07, THE SEQUENTIAL-ACCESS CATCH: the live run's first real estate killed both contract reads at row-mapping

**What the live run hit (step one, contracts):** both OSSYS metamodel reads died with
`adapter.ossysSql.rowMapping: Failed to map row 0 of result set 'attributes': Invalid
attempt to read from column ordinal '2'. With CommandBehavior.SequentialAccess, you may
only read from column ordinal '18' or greater.` — the go board's "a metamodel could not
be read" red line. A one-off reorder of the offending read just moves the violation
(fix the ordinal-2 re-read by hoisting it and the very next read of ordinal 0 throws
the same class), which is the tell that the CONTRACT, not the call site, was wrong.

**Root cause.** `MetadataSnapshotRunner` executes the rowsets script with
`CommandBehavior.SequentialAccess` (cells stream; the drained JSON rowsets skip
without buffering), whose live-reader contract is *each ordinal visited at most once,
strictly ascending*. The typed mappers assumed random access. One mapper violated it:
`mapAttributeRow`'s PhysicalCol fallback — NULL `PhysicalColumnName` (ordinal 17) →
re-read AttrName (ordinal 2). The canary never fired it because the edge-case seed
populates every `Physical_Column_Name` and the script's sys.columns backfill covers
the rest; a real estate carries orphan attributes (metadata outliving a dropped
column) whose physical name survives NULL to the mapper.

**The refactor (class-retiring, not site-patching):** capture-then-map. `captureRow`
is now the ONLY cell-access site against the live reader — one ascending,
single-visit sweep materializing each row at rest (`RowAtRest`) — and all 13 mappers
consume the captured row, where ordinal access carries no order or visit-count
obligation. The typed accessors (`readString`/`readInt`/`readBool`/`readGuidOpt` +
opts) keep their NULL-guard semantics (DBNull carried verbatim from `GetValue`);
capture failures classify through the same `RowMappingFailure` axis. SequentialAccess
stays (its win — no double row-buffering, free JSON-rowset drain — is untouched);
`ReadSide`'s streaming reader already honored the contract by construction and is
unchanged.

**Proven:** new `MetadataSnapshotSequentialAccessTests` (docker) seeds the edge-case
fixture PLUS the orphan shape (active attribute, NULL `Physical_Column_Name`, name
matching no physical column) — on the pre-refactor code it reproduces the live
failure verbatim; on the refactored runner the extraction succeeds and the orphan
falls back to the upper-cased attr name while its neighbors keep their real physical
names.

**Final verdict:** the full sweep PASSED IN FULL — fast pool 3937/3937 (39s), docker
pool 289/289 (518s, regression test aboard), scale pool 16/16 (79s). The contracts
gate no longer dies on estates carrying orphan attributes.

## Entry 23 — 2026-07-07, THE ENTITY-LESS ESPACE: the read that survived row-mapping died at module assembly

**What the live run hit (contracts, take two):** with the SequentialAccess fix aboard,
the extraction completed and the metamodel read died one stage later —
`module.kinds.empty: Module OssysOriginal <guid> must contain at least one Kind.`
(The `OssysOriginal <guid>` is the module's SsKey rendered via `%A`; the guid is the
espace's SS_Key.)

**Root cause.** A real estate routinely carries espaces with NO entities — UI, theme,
and service modules. The rowsets script's `#E` exports every espace (module filter +
IncludeSystem only; no entity-existence condition), and `parseRowsetBundle` fed every
module row to `Module.create`, whose LR1/A39 non-empty-kinds invariant (shipped
2026-05-18, deliberately) refuses the empty module — one entity-less espace failed the
WHOLE read. V1's posture is explicit and triplicated: "Module 'X' contains no entities
and will be skipped" (`ModuleDocumentMapper` / `ModelDeserializerFacade` /
`FullExportApplicationService`) — a parity gap the canary never exposed because every
seeded espace has entities.

**The fix (skip as a NAMED erasure, per the compensating constraint the LR1 decision
codified — "adapters always produce non-empty modules"):** `parseRowsetBundle` now
skips espaces with no entity rows, and the new pure producer
`OssysRowsetReader.entityLessModules` names each skip (`adapter.ossys.module.entityLess`,
Info — the normal shape of a real estate); `LiveModelRead` wires it into the existing
notice rollup alongside the columnReality/primaryKey divergences, so the erasure rides
the notice artifact + the one calm Warn line, never silence. `Module.create`'s
invariant is UNTOUCHED — it remains the guard against corrupt shapes; the adapter now
honors its side of the contract.

**Named residual:** the JSON path (`OssysJsonReader.parseModule`) carries the same
latent shape — a V1 `osm_model.json` bearing an entities-less module (V1's own mapper
guards against reading one, so V1 tools skip it at read time) would still fail V2's
JSON read with `module.kinds.empty`. Not the live-transfer path (contracts ride the
rowset path on both sides); fix deferred until a JSON-path consumer meets one.

**Proven:** pure (`OsmRowsetReaderTests` — skip yields the populated-modules-only
catalog; the notice producer emits exactly one Info entry per skipped module, zero
when all are populated) and docker e2e (`EntityLessModuleReadDockerTests` — edge-case
seed + an entity-less espace: extraction → bundle → catalog parse succeeds, module
absent, erasure named).

**Final verdict:** the full sweep PASSED IN FULL — fast pool 3939/3939 (51s), docker
pool 290/290 (629s), scale pool 16/16 (113s), the new tests aboard both pools. Both
live-run catches of this program (Entries 22 + 23) are closed; the contracts gate
reads real estates carrying orphan attributes AND entity-less espaces.

## Entry 24 — 2026-07-07, THE DUPLICATE SS_KEY: scope parity for contract reads + the inactive-shadow erasure

**What the live run hit (contracts, take three):** `catalog.kinds.duplicateKey:
Catalog has Kind SsKey OssysOriginal <guid> duplicated across modules; A4 requires
Kind identity to be globally unique.` The estate didn't change — each prior fix
(Entries 22, 23) peeled the read one stage deeper into what a whole real estate
actually looks like.

**Root cause, two-ply:**

1. **The transfer's contract reads ignored the projection.json model scope.**
   Full-export/publish read through `readConfigModel` →
   `SnapshotScopeBinding.fromModel cfg.Model` (declared `model.modules` narrow at
   query time; `includeInactiveModules` defaults false; `onlyActiveAttributes`
   defaults true). The peer transfer's `acquireContracts` → `Source.ofOssys` →
   `LiveModelRead.fromConnSpec` read with `defaultParameters` — the
   show-me-everything stance (all modules, system included, INACTIVE ENTITIES
   included). The transfer saw strictly more estate than any other verb.
2. **What the wider view exposed:** an entity MOVED between modules leaves an
   inactive `ossys_Entity` row in the old espace with the SAME SS_Key as the
   active row in its new home. Load both → two Kinds, one SsKey → A4 refusal
   kills the whole read.

**Fix 1 — scope parity (one modeled estate, every verb reads it the same way).**
`Source.ofOssysWith` + `PeerTransfer.acquireContractsWith` are the new
scope-bearing siblings (zero-default `ofOssys` / `acquireContracts` delegate, per
the sibling-wrapper discipline); `runPeerTransfer` + `runCheckGo` take the
contract scope and the dispatcher binds it from `SnapshotScopeBinding.fromModel
shaping.Model` — the transfer and its go board now read the SAME modeled estate
as full-export/publish, on both sides. An unscoped config still binds
`onlyActiveAttributes=true` (the config default), which also pre-empts the
inactive-duplicate-attribute layer beneath this one.

**Fix 2 — the inactive-shadow erasure (robustness inside any scope).** The
entity-less skip generalizes into `OssysRowsetReader.normalizeBundle` — the ONE
normalization pass every rowset parse applies (idempotent; the live read calls it
directly to surface the notices):
- step 1, inactive-shadow resolution (`adapter.ossys.kind.inactiveShadow`, Info):
  duplicate SS_Key with EXACTLY ONE active row → the active row is carried, each
  inactive shadow dropped + named, and references aimed at a shadow's EntityId
  re-aim at the survivor (same GUID — identity is the conserved charge). Two
  active, or all inactive → carried UNCHANGED; the A4 refusal stays the terminal
  for a contradiction only the operator can adjudicate.
- step 2, the Entry-23 entity-less skip, computed AFTER step 1 — a module whose
  only entity was a dropped shadow skips too.

**Proven:** pure (`OsmRowsetReaderTests` — shadow dropped + named with the
survivor/shadow ids in metadata; reference re-aim; two-active still refuses on
A4; normalization idempotent; entity-less tests re-homed onto `normalizeBundle`)
and docker e2e (`EntityLessModuleReadDockerTests` — seed + an inactive City
shadow in a second espace: extraction → normalize → parse succeeds, City carried
exactly once in its active home, both erasures named).

**Final verdict:** the full sweep PASSED IN FULL — fast pool 3943/3943 (52s), docker
pool 291/291 (649s), scale pool 16/16 (111s), the normalization + scope-parity tests
aboard. Three live-run catches (Entries 22-24) closed in one day; the contracts gate
now reads a whole real estate — orphan attributes, entity-less espaces, and
moved-entity shadows — under the same model scope every other verb honors.

## Entry 25 — 2026-07-07, THE EFFECTIVE TRANSFER GRAPH: the board and the engine stop judging the whole estate

**What the live run hit (the go board, post-contracts):** three related reds on a
small declared subset — (1) `grant` STOP because the cloud principal carries
object/column-scope DML with NO database-scope grant, and the probe read
`fn_my_permissions(NULL,'DATABASE')` only; (2) `load order` STOP because the
topology ran on the WHOLE sink contract, so an unrelated estate cycle degraded the
order alphabetical; (3) dry-run `cycles` naming unbreakable-cycle FKs entirely
outside the requested subset. The diagnosis (operator review, confirmed): an
ABSTRACTION GAP — the engine had every ingredient of "what does this transfer
actually touch" (LoadSet, reconciled kinds, ingestScope, the face's gateScope) but
no reified carrier, so every consumer that predates subsets (topology, plan
cycles, grant planning) silently evaluated the estate.

**The carrier:** `TransferScope` (Core) — WriteKinds (declared or whole estate,
minus reconciled), Nodes (write ∪ reconciled as ISOLATED graph nodes — reported,
never ordered), and `TransferScope.topology` over the induced subgraph
(`TopologicalOrderPass.runScopedWith` — FK edges bind only between written kinds;
same algorithm, one graph-construction parameter). Built ONCE from the inputs
every gate already holds; consumed by the engine's Execute gates AND the go
board, so the forecast and the live run cannot evaluate different graphs. A full
non-reconciling transfer is the identity scope (byte-identical; property-pinned).

**Engine deltas (Execute gates, per the operator-approved review):**
`orderedLoadGate` and `executeGate`/`UnbreakableCycleFks` judge the scoped graph
(unrelated cycles stop refusing subset runs; a cycle THROUGH a reconciled kind
correctly breaks — it is never written); `DeferredFkColumns` shrink to scoped
cycle members (fewer Phase-2 UPDATEs); the slice apply (`runGoldenApply`) scopes
to its slice's kinds; the streaming leg gets the reconciled-edge drop; synthetic
legs unchanged (whole estate is their true graph).

**Grant deltas:** `Preflight.WriteAction` gains `Update` (Phase-2 re-point and
the MERGE lane genuinely UPDATE — INSERT never implied it; the capability survey's
data sets follow); `plannedTransferWrites` is scope × emission (INSERT+UPDATE per
written kind; DELETE under a wipe — previously ungated); `captureGrantEvidenceFor`
probes per planned table at OBJECT scope (`fn_my_permissions('<obj>','OBJECT')`,
object grain), recorded in `GrantEvidence.ProbedObjects` — a probed object's rows
are EFFECTIVE permissions and AUTHORITATIVE (no database fallback), so **the
pinned G1 gap closes for planned tables**: a table-level DENY refuses
`transfer.insufficientGrant` pre-write with ZERO partial write (both pinned tests
PROMOTED — `ReverseLegCanaryTests` L3Deny + the peer scenario 5). The go board's
grant item renders the SAME planned-write evaluation the engine enforces —
object-scope-only DML is a [ GO ].

**Proven:** pure (`TransferScopeTests` — scoped topo ignores unrelated cycles,
reconciled-edge drop, full-transfer identity, planned-write verbs;
`PreflightTests` — probed-object precedence incl. the DENY-under-db-grant shape;
`CapabilitySurveyTests` re-pinned with Update) and docker (`GoBoardDockerTests` —
the unrelated-cycle board goes GREEN while the unscoped topology provably
degrades on the same contract; `PeerManagedGrantTransferDockerTests` — the new
OBJECT-scope-DML cell lands the subset with zero grant violations and no
database-scope DML at all; both G1 promotions).

## Entry 26 — 2026-07-07, THE WEAK-FEEDBACK RESOLVER (v5): cycle handling made total, and the resolved-cycle refusal bug the audit caught

**Why (operator-directed completeness follow-on to Entry 25):** the review of
V1's manual cycle-ordering override (`CircularDependencyOptions` — z-index
positions that DISABLE automatic detection wholesale, keyed on espace-fragile
physical names) concluded V2 should overcome it with a complete resolver rather
than carry the knob. The gap named there: the asymmetric-2-cycle resolver refused
symmetric-weak 2-cycles and every SCC ≥ 3 — shapes the two-phase deferral can
provably load.

**The resolver (`CycleResolution.weakFeedbackStrategy`, pass v5):** find a cycle
(deterministic DFS); every edge on it non-Weak → refuse, naming EXACTLY that
cycle (and the remedy: make one of its FK columns nullable — it then defers);
otherwise break the smallest Weak edge on it and repeat until acyclic. The
complete case map (self-loops, 2-cycles asymmetric/symmetric/strong, SCCs ≥ 3
weak-bearing/strong-cored, fused rings, cascade conservatism) is the docstring
table; invariants I1–I5 (broken ⊆ Weak ⊆ deferrable; acyclic on resolve;
refuse ⟺ an all-strong cycle exists; input-order independence; a pure weak ring
breaks ONE edge) are property-tested. Manual overrides stay DEFERRED with a
named trigger in the DECISIONS index (a cycle whose breakability is not
schema-inferable).

**The correlated catch:** `UnbreakableCycleFks` judged ALL cycle members, but a
RESOLVED SCC deliberately stays in `Cycles` for deferral — so a resolved
asymmetric 2-cycle's strong edge was flagged unbreakable and `executeGate`
refused a load the proven order satisfies (latent until now: the canary estates
only carry self-loop cycles). `TopologicalOrder.unresolvedCycleMembers` now
feeds unsatisfiability; `cycleMembers` still feeds deferral. The algebra reads
cleanly: resolved → strong edges satisfied by ORDER, weak by DEFERRAL;
unresolved → nullable defers, non-nullable is the named refusal.

**Also caught while here (the sweep's one red):** the per-object grant probe
treated a MISSING sink table as zero permissions — a misleading
`insufficientGrant` for what is a shape fact. An absent object now stays
unprobed (database-scope fallback), and the chunk-resume crash witness holds.

**Proven:** pure — `CycleResolutionTests` (case map + I1–I5 FsCheck properties),
`TopologicalOrderPassTests` (v5 flips), `DataLoadPlanTests` (resolved →
satisfiable; unresolved → named diagnostic), `DataEmissionComposerTests`
(unresolvable fixture moved to nullable+Restrict, preserving both premises);
full fast pool green.

**Final verdict (Entries 25 + 26 together):** the full sweep PASSED IN FULL —
fast pool 3965/3965 (35s), docker pool 293/293 (572s), scale pool 16/16 (96s),
with the scope-parity, object-scope-grant, G1-promotion, unrelated-cycle, and
resolver-v5 witnesses all aboard, and the Release-configuration build verified
(the FS3511 witness caught and fixed the probe-loop shape before it shipped).
The go board and the engine now judge the same effective transfer graph, and
cycle handling is total over its case space with the correlated
deferral/unsatisfiability algebra property-pinned.

## Entry 27 — 2026-07-07, THE FORECAST TABLE: the board states the data change (before → after), proves its reconcile proposals, and previews the SQL

**Operator request:** the forecast "gestures indirectly" at the data change —
show the planned before and after of the target environment side by side;
strengthen the automatic reconcile suggestions; add an option to preview the
planned SQL.

**The forecast item is now a table.** The dry run's report carries its
`DataLoadPlan` (`TransferReport.Plan`, DryRun only — Execute never retains
estate-scale rows past the run, and the streaming preview's plan carries no
rows), and the face joins it against LIVE sink counts probed over a
short-lived connection:

```
table                        before      +add     match      -del       after
dbo.OSUSR_XABC_CUSTOMER           0         2         -         0           2
dbo.OSUSR_XDEF_CITY               2         0         2         0           2   reconciled — matched to existing sink rows, no insert
TOTAL                             2         2         2         0           4
```

`after = before − deletes + adds`, per row and in TOTAL; a count that will
not probe renders `?` and poisons the totals (never a silent 0); a
WipeAndLoad kind's `-del` is its live before-count (the wipe), scoped by the
same `TransferResume.wipeTargets` walk the live wipe takes. `KindOutcome`
gained `RowsMatched` (the remap's per-kind bindings) so a reconciled kind's
"match" column is the measured match count, not an inference.

**Reconcile proposals now carry LIVE evidence.** Each escaping target's
candidate columns — the sink's single-column unique indexes, then
name-shaped text attributes (`Name`/`Code`/`Email`/…) when no index
nominates one, at most three per target — are probed against the actual
pair: sink uniqueness (`COUNT` vs `COUNT DISTINCT`) plus a bounded sample
(200 distinct source values, parameterized) matched into the sink. The
relationships red item's detail now reads
`evidence: reconcile 'AppCore.City:Name' (unique-indexed on the sink) —
sink-unique; 2/2 sampled source value(s) found in the sink: a STRONG
candidate.` — the operator pastes a PROVEN rule. A probe that cannot run is
a NAMED `Unprobed` verdict; a pair that will not open degrades to the
static proposals, named.

**`check go <flow> --sql` writes the planned SQL.** `Transfer
.plannedSqlPreview` renders the SAME plan the dry run built through
`StaticSeedsEmitter.emitFromPlan` (the A35/A36 text-realization sibling of
the live bulk lanes, the `SliceApplyRun.emit` composition): the child-first
wipe DELETEs under strategy replace, phase-1 MERGEs in FK-safe plan order,
then the phase-2 deferred-FK re-points — written to
`go-board/<flow>.planned.sql` with a header naming the contract
(AssignedBySink keys mint at run time; the artifact's values are the plan's
source-side keys, shown for shape). An advisory board line names the path.

**Also caught while here:** `check go golden --format json` REFUSED — the
positional filter counted a value-bearing flag's value token (`json`) as a
second flow name. The positional walk now skips `--format`'s value.

**Proven:** pure — `PeerTransferTests` (forecast-table arithmetic/alignment/
`?`-poisoning, `plannedSqlPreview` wipe order + loadSet scoping + no wipe
under Incremental, `narrateEvidence` strength ladder); docker — the
red/green/red board now asserts the evidence lines (STRONG, 2/2 sampled),
the before→after table, and the `--sql` artifact's content on the live pair.

## Entry 28 — 2026-07-08, THE BOARD-CLARITY PASS: the go board says exactly what it means

**Operator request (nine parts):** the shape advisory must name what MATCHED,
not only what differs; the forecast must split declared rows from
brought-along rows (naming the pulling edge) and show BOTH physical names;
reconciled matches must report which columns differ, with a config to ignore
audit fields; drops/identities/deletes must show whole rows; the re-run
"in-subset parent" line was opaque; the relationships-GO vs re-run-STOP read as
contradictory; and the whole thing should be more tabular.

**Shape — the affirmative tiers.** `ShapeVerdict` gained `Proven : string list`:
the comparison ladder that HELD over the transferred set (entity names → column
names → column definitions → foreign keys → indexes → entity-level facets). The
board's shape line now closes with `Matched: entity names, column names, column
definitions (type/length/precision/nullability), foreign keys.` — so "indexes
differ" can never again leave the operator guessing how deep the agreement runs.
Each advisory class carries its OWN action language: an index difference is
"performance-only … No action needed before this transfer"; an FK difference is
"the load follows the SOURCE model's relationships … No action unless the run
refuses"; entity-facet drift "never rewrites a transferred row."

**Forecast — origins, both physical names, match drift.** `ForecastLine` gained
`Source` (the source physical coordinate) beside `Table` (the sink's); the table
now renders `source (read) | target (written) | before | +add | match | -del |
after`. Declared tables render first; kinds pulled in by a relationship render
after, each noting `brought along by Customer.CityId -> City`. Reconciled kinds
carry a new `match drift` axis: `Reconciliation.reconcileKindWith` compares every
matched source/sink pair column-by-column (excluding the PK and the operator's
`reconcileIgnore` set), and the board reports which columns differ with sample
values — GREEN when they agree, ADVISORY when they drift, always naming the
`reconcileIgnore` move for expected audit drift (CreatedOn / UpdatedOn). The
`reconcileIgnore` config is one shared list of attribute names beside `reconcile`
in projection.json, threaded through the whole engine (`WriteOptions
.ReconcileIgnore`, every reverse-leg/streaming/peer entry).

**Whole rows.** `DataLoadPlan.DroppedRows` and `ReconciledIdentity.UnmatchedRows`
carry the FULL records behind the drop-set and the unmatched-identity set; the
board's `drops` and `identities` axes render the first five rows in full with the
reference that failed each. The wipe preview under strategy-replace samples the
first five rows each `-del` would delete (`SELECT TOP 5`), so `deletes` shows the
actual records, not a count.

**Re-run — plain and non-contradictory.** The opaque `X.Y -> in-subset parent (N
referencing row(s))` became `<child> has N row(s) whose <col> points at <parent>
(in the transferred set) — the wipe of <parent> would orphan them`. And the
apparent contradiction is resolved by naming DIRECTION: `relationships` judges the
subset's OUTBOUND foreign keys (do its rows dangle?), `re-run` judges INBOUND
references from OTHER sink tables (would a replace-wipe orphan them?) — two
different questions, now labelled as such on both axes.

**The drops-under-replace question the operator asked.** A dropped row (unmatched
reference) is dropped at plan-build, so `+add` is already net of it and it is NOT
re-inserted after the wipe; the `-del` column is the WIPE count (every existing
row), a separate quantity. The forecast note now says so explicitly on a wiped
kind that also drops: "wiped but NOT re-inserted."

**Proven:** pure — `PeerTransferTests` (Proven tiers: clean pair affirms all,
index-only diff keeps deep tiers + drops 'indexes'; forecast source+target
columns), `ReconciliationTests` (full unmatched rows; matched-pair divergence
with sample values; audit-field ignore), `DataLoadPlanTests` (DroppedRows carries
the whole row + failed reference); docker — the red/green/red board asserts the
source+target physical names, the brought-along note, the match-drift green line,
and the OUTBOUND-relationship wording on the live managed-grant pair.

**Final verdict (Entry 28):** the full sweep PASSED IN FULL — fast pool
3975/3975 (47s), docker pool 293/293 (632s), scale pool 16/16 (109s), all exit
0, with the Proven-tier, forecast-origin/dual-name, match-drift/reconcileIgnore,
whole-row-preview, and re-run-direction witnesses aboard, and the Release
build verified (FS3511 clean). Shipped as a new PR from the branch restarted on
main after #659's squash-merge.

## Entry 29 — 2026-07-08, SUPPORTING SCOPE: the config models the business intent of the supporting rows

**Operator request:** a `supportingScope` array of typed objects, sibling to
`tables`, that says WHY each supporting (non-payload) table is touched — "these
rows are not the payload, but they support the payload's integrity." Max out the
vocabulary against the graph relationship tree; instead of secondary
dependencies hiding under `tables`, the config confidently says: this is an owned
child / a seeded reference / an existing reference / a shared anchor / a static
lookup, and these are the dependent rows I refuse to harvest.

**The vocabulary — six relationships, two families.** References the payload
points AT (outbound escaping edges): `existing-reference` (match the target's own
rows by a business `key`, never copy), `reference-seed` (insert the referenced
rows the target lacks), `shared-anchor` (re-point every reference to one target
row, optional dynamic match first), `static-lookup` (match by `key` and hold to
ZERO divergence). Dependents that point AT the payload (inbound edges):
`owned-child` (`of` the parent; copied along, cascade-verified), `blocked-dependent`
(`of` the parent; deliberately not harvested). The terse `reconcile` strings are
the shorthand — `T:Col` → existing-reference, `T:=k` → shared-anchor,
`T:Col:=k` → shared-anchor-with-match — resolved into the SAME model
(`SupportingScope.ofReconcileEntries`); supportingScope is canonical.

**The decisive lever — the graph already carries the truth.** OutSystems
"Delete Rule = Delete" already reads as `OnDelete = Cascade` /
`EdgeStrength.Cascade`; escaping edges are already traversed. So the config
DECLARES intent and the board VERIFIES it: an owned-child whose edge is a
protect-rule (not cascade) is caught red; a reference nothing in the payload
points at is caught red; a blocked-dependent that is not a real inbound dependent
is caught red. `TransferSubset.dependentEdges` (the net-new inbound mirror of
`escapingEdges`) supplies the dependent-family evidence, and completeness now
closes both directions: every escaping reference and every inbound dependent is
either classified or named as unaccounted.

**Desugar over new plumbing.** Each relationship projects onto a primitive that
already exists — `SupportingScope.desugarToStrings` appends to the flow's
`tables` / `reconcile` string lists the engine already threads, so the reconcile
and load-set portions need NO new engine field. Two genuinely-new write behaviors
land, both gated to empty defaults: `reference-seed` insert-only-missing (a
`WriteOptions.SeedKinds` pre-filter that drops the rows a preserved-key target
already holds, never overwriting), and the inbound-orphan gate (Execute +
WipeAndLoad refuses by name when an out-of-payload dependent holds referencing
rows, with `blocked-dependent` as the acknowledgement channel).

**The board.** A new `supporting scope` axis verifies each declared intent and
reports unaccounted escapes; the forecast note reads the declared intent
("owned child of Movie", "seeded reference", "existing reference — matched to the
target's own rows") instead of the ownership-blind "brought along by K.col -> T";
a static-lookup whose matched rows drift reds the match-drift axis.

**Proven:** pure — `SupportingScopeTests` (the reconcile bridge, desugar, typed
resolve, the six verify claims, `dependentEdges`, completeness),
`MovementSurfaceTests` (parse→render→parse round-trip byte-identity + the
also-payload / no-reason named refusals). Docker — `GoBoardDockerTests`: the
axis confirms a declared City reference and catches a mislabeled owned-child red
on the live managed-grant pair. Release build clean (FS3511 verified).

## Entry 30 — 2026-07-08, THE RENDERING ELEVATION: the board (and the live run) route through the `View` engine — responsive forecast, per-claim guarantees, the References/Dependents tree

**Operator request:** make the board rendering responsive to terminal size using
the Spectre implementation — "really zhush it up." Print the GUARANTEE each
references/dependents claim makes, grouped by category, so the operator FEELS the
confidence (an identical-match static entity should read as a stated no-op, green).
Normalize the presentation across the joins that make sense. Present the
multi-relationship information as a hierarchical / graphical interface. Scope:
board AND the live run; hierarchy fully expanded.

**The move.** The go board and the live-run report were the last two operator
surfaces still on raw `printfn` + fixed-width `sprintf`. Both now BUILD a `View`
(the Spectre-backed `REPORTING_HORIZON` substrate) and render through the one
engine — "one substrate, many lenses", the `TtyRenderer` precedent. The pure
`GoBoard.Board` stays the ONE substrate; the machine lens (`GoBoard.toJsonString`)
is byte-for-byte untouched (the CI contract).

**Typed carriers, additive.** `GoBoard.Item` gains `Body : ItemBody`
(`Plain | Forecast | Scope`) with `forecastItem` / `scopeItem` constructors that
ALSO set the existing flat `Detail` strings; `render` / `toJsonString` never match
`Body`, so every existing site and the JSON are unchanged. Primitive-typed
carriers (strings + `Status`) because `GoBoard` compiles before `SupportingScope`
— the assembly boundary held.

**The forecast is now a responsive `View.Table`** — Spectre auto-sizes its columns
to the terminal, so the wide physical-name columns reflow instead of clipping;
`+add` reads green, `-del` red; a `TOTAL` row closes it; the free-text note drops
to a per-row `Note` beneath so it never fights the table's width.

**The supporting-scope axis is a fully-expanded hierarchy** — grouped References /
Dependents → per-claim `Disclosure` carrying the normalized JOIN edges (which
payload columns point at a reference; which dependent columns point back), the
authored intent, and the GUARANTEE a Confirmed claim earns. `guaranteeOf` states
each of the six invariants in THE_VOICE register: a static-lookup identical-match
reads as "a verified no-op on the lookup", green — the confidence made explicit.
`SupportingScope.scopeGroups` is the ONE builder both the board and the live-run
report project, so the guarantee tree cannot drift between the readiness surface
and the run.

**The live run too.** `narrateTransferReport` is rebuilt on `View`: the load plan
is a responsive table, the cycle / unmatched / drop sections are status-glyphed
`Disclosure` blocks, and a declared-scope flow closes with the same guarantee tree
("the invariants that held", threaded from the resolved supporting scope).

**The width discipline the docker run taught.** The board is a REPORT — its proof
lines (reconcile evidence, dropped rows, wipe previews) must print in FULL, exactly
as the raw-`printfn` predecessor emitted them. So the `View` text cap is unbound,
and a REDIRECTED sink widens its console `Profile.Width` so Spectre never wraps a
proof line mid-phrase (the captured evidence substring had straddled the width-100
wrap); a real TTY keeps its width so the forecast table reflows. The table
auto-sizes to its own content regardless. One gap fixed in passing: a `Red` item
with a remedy but no detail now still surfaces the remedy in the rich lens (the
plain renderer always printed it).

**Proven:** pure — `GoBoardViewTests` (responsive forecast render, the
Confirmed-claim guarantee + join edge under the family tree, the green/red verdict
next-move, `toJsonString` byte-identity under a `Body`-bearing item) +
`SupportingScopeTests` (`guaranteeOf` per relationship + the THE_VOICE
banned-word scan; `scopeGroups` family split with join edges); docker —
`GoBoardDockerTests` (the board + supporting-scope substrings survive the
Table/tree format on the live pair). Release build clean (FS3511 verified).

## Entry 31 — 2026-07-08, THE WIDGET ELEVATION: the three unused Spectre widgets (Tree, Rule, Progress), each placed where it honestly fits

**Operator request:** an agent noted three Spectre widgets never used by the TTY —
progress among them. Find every use case across the rendered surfaces and implement
each fully, as seen fit.

**The three, and the survey that placed them.** Three subagents cross-surveyed every
renderable surface. The finding: a widget is worth adding only where the substrate
already wants its shape — forcing it elsewhere regresses what the hand-rolled form
does better.

**`Tree` → exactly one site.** New `View.Tree` (+ a `TreeNode` = label + status +
children, so the Spectre `Tree` mapping + `toJson` stay total). Connector lines
(`├─`/`└─`/`│`), always fully expanded. The go board's References / Dependents
guarantee hierarchy is the one clean fit — deep, static, one-shot, and already
rendered fully-expanded with gating disabled. Everywhere else the nesting is
depth-gated (`Disclosure`, the `Navigator` dig, the `▸ N more` collapse) or
breadth-capped (`Lane`, `Trail`) or navigator-coupled (the at-scale `Comparison`
groupings) — Spectre's non-gated `Tree` would regress those, so they stay hand-rolled.
`GoBoardView.scopeTree` now builds the `Tree`, the shared lens for both the board and
the live-run guarantee report.

**`Rule` → the mastheads and captions.** New `View.Rule of title option * Status` — a
titled divider that NAMES the band it opens. Applied at the go board masthead + a
verdict separator tinted by the outcome; the readiness board's `cutover readiness`
masthead + `ledger` footer; the `Bench` / `Run events` / `Flows` table captions;
`Comparison`'s `by module` rollup. **The width hazard the docker path would have
caught:** the board widens a redirected profile to 100 000 columns (so proof never
wraps); a full-width rule there would be a 100 000-char line — the render arm caps it
to `plainWidth` on the widened sentinel, leaving the content-sized forecast table
untouched.

**`Progress` → a determinate bar in the live Watch, honoring §13.** Spectre's
`Progress` widget owns the console with its own render loop (incompatible with the
Watch's `Live` region) and its row-118 cash-out trigger has not fired — so rather than
force the widget or build a deferred feature early, the Watch's active-stage line
gained a fixed-width bar (`▇▇▇▇▇░░░░░`) off the already-shipped `stageProgress` feed.
It draws PRECISELY when a real denominator is known (`Total > 0`) — the honest reading
of §13's "never a bar that misstates"; an unknown total draws no bar, the count-up
carries it. Fill scaled to a constant width (the R6 meter uses the total AS its width).

**One substrate, three lenses held.** Both new cases carry a `toJson` arm and a
plain-lens fallback (Spectre box-drawing survives NoColors); the `Navigator`'s
`labelOf`/`filterView` gained arms (a `Rule` filters on its title like a leaf; a
`Tree` prunes like a `Disclosure`).

**Proven:** pure — `ViewTests` (the `Rule` title + json + the width cap on a widened
sink; the `Tree` connectors + fully-expanded leaves + nested json), `WatchTests` (a
determinate stage draws a bar; an unknown total draws none; the bar is fixed-width);
docker — `GoBoardDockerTests` (the scope tree's substrings survive the `Tree`
connectors; the masthead/verdict rules do not break the board). Release build clean
(FS3511 verified).

## Entry 32 — 2026-07-08, THE PROGRESS WIDGET + THE PLAN WIZARD: the animated bars for the long legs, and a guided decision-walkthrough that names the WHY of every transfer choice

**Operator request:** take the Progress widget further (the full animated display, not
just the Watch bar); and drive up the transfer's strategy space + operator experience —
"almost like a wizard where you can decide what path you want and be perfectly clear on
WHY."

**The standalone Progress widget (`--progress`).** The row-118 cash-out, fired by operator
pull. `ProgressRenderer.fs` opens Spectre's animated `Progress` display as a
**mutually-exclusive alternative** to the Watch board (both own the channel-2 `Live`
region), a second rendering of the SAME `summary.stageProgress` feed via a pure fold
mirrored onto `ProgressTask`s. It copies the Watch's off-thread teardown discipline exactly
(enqueue-only subscriber, background body under `withWriter Null`, `clearSubscribers` on
every exit). Honest determinacy (§13): a real bar only where a genuine denominator arrives
(`extract` / `deploy` / `load`-by-kind); a spinner otherwise. Gated on a real terminal, so a
pipe is untouched clean NDJSON, and it suppresses the auto-Watch when set.

**The guided transfer "plan" wizard (`check plan <flow>`).** The DECLARATIVE counterpart to
the go board — the hybrid the operator chose. `check go` verdicts readiness; `check plan`
walks each transfer decision axis (write strategy / identity / scope / realization /
staging) and, for each, names the CURRENT choice, the ALTERNATIVES, the tradeoff each
carries, and the exact config edit. This is the "more strategies" surface: the transfer's
strategy space, already expressible in config but scattered, made one legible menu (the A44
`expressible ⇔ reachable` half) — no new engine mode.

- Pure `TransferPlan.fs` (the decision model + `ofCurrent` + `toJsonString`), rich
  `TransferPlanView.fs` (a `Rule` section per decision, the alternatives as a fully-expanded
  `Tree` — dogfooding the widget arc). Piped / CI: a one-shot declarative report on stdout,
  never a prompt. A real terminal: the plan on channel 2 + an optional `Intervene` prompt to
  pick the write strategy, which **persists to `projection.json`** (`RelaxationStore.setFlowString`
  — the A44 move the Migrate relaxation gate pioneered). Every branch is a hand-reachable
  config edit; the wizard invents no stored knob the engine doesn't already derive.
- Dispatched as `check plan` (a `Shell.ReadOnly` verb beside `check go`); the go board gains
  an advisory `strategy options` pointer to it.

**A note on the verb name.** The plan proposed `transfer plan`; it shipped as `check plan`
— it is a read-only flow analysis exactly like `check go`, so it slots into the check family
with minimal plumbing and pairs in discoverability ("go" = ready?, "plan" = what are my
options?).

**Proven:** pure — `ProgressRendererTests` (the fold + the off-thread teardown / channel-1
suppression / exception propagation), `TransferPlanTests` (one decision per axis, the marked
current + config edit, every option a non-empty why, the THE_VOICE scan, `reselectStrategy`,
`toJsonString`), `TransferPlanViewTests` (the `Rule`/`Tree` render). Manual — `check plan
golden` renders the full decision tree (plain + `--format json`) reflecting the flow's real
settings. Release build clean (FS3511 verified).

---

## 2026-07-08 — The write-signoff greenlight: a verified, first-class approval for destructive write modes

The partial transfer could WIPE (`strategy: replace`), assume-empty (`fresh`), drop rows
(`--allow-drops`), flood CDC, or write explicit keys — all on the two run-time gates
(`PROJECTION_ALLOW_EXECUTE` + `--go`) with **no durable, per-flow record that the operator
understood and approved the specific destruction**. This closes that: `flows.<flow>.signoff`
is a first-class, declarative array enumerating each destructive mode a flow may perform, the
run REFUSED until the declaration exists (default-on, breaking by design).

- **A new kind of predicate.** `supportingScope` is STRUCTURAL (the graph confirms or
  contradicts it); a signoff is AUTHORIZATION — there is no graph fact, the board reds and the
  engine refuses BY NAME until the mode is greenlit. It joins the
  `PROJECTION_ALLOW_EXECUTE`/`--go`/`--allow-drops`/`--allow-cdc` family, but durable and
  auditable. `WriteSignoff.fs` (pure): the closed `WriteMode` DU (a new destructive mode is a
  compiler event forcing its impact arm), `parseMode`/`modeLabel`, the `guaranteeOf`-style
  `impactOf` (stative, THE_VOICE), and the total `verify` (`Confirmed | Missing | ScopeMismatch`).
- **The two-traversal.** Board and engine read the SAME `TransferScope.WriteKinds` (the DELETE
  targets — NOT `Nodes`, which folds in reconciled kinds that are matched, never wiped), so the
  "covered" verdict cannot drift. The go-board `signoff` axis reds-until-greenlit (and reds on a
  scope-mismatched approval whose declared `tables` do not cover the wipe); the engine mirrors
  it at the live-write seam (`transfer.writeSignoff.ungreenlit`), belt-and-suspenders for a
  scripted `--go` that never ran `check go`.
- **Rich + verified.** Each entry carries the required `mode` + optional `tables` scope
  (empty = the whole flow's set; a declared scope is verified to cover the wipe, so a stale
  approval cannot rubber-stamp a wider blast radius) + `acknowledgedImpact` (echoed on the
  board) + `approvedBy`/`date`.
- **Boundary.** The transfer five (`replace/fresh/drops/cdc/identity-insert`) enforce here;
  `delete-scope` is emission-lane (`emission.deleteScope`, a different config plane) — enumerated
  in the vocabulary, its gate a named follow-on (Active deferral). Streaming already refuses
  WipeAndLoad, so the materialized path is the only wipe path and the gate covers it.
- **The `_comment` skip (Part 0).** A `_`-prefixed key in `flows`/`environments`/`defaults`/
  `slices`/`sliceFlows` no longer crashes the parser — one `isCommentKey` predicate guards all
  five map-style loops (the house `_`-skip the codebase lacked); A44 unaffected (render never
  emits `_`-keys).

**Proven:** pure — `WriteSignoffTests` (the six-mode label bridge, the THE_VOICE impact scan,
`verify`'s missing/covering/scope-mismatch/wrong-mode/empty-plan verdicts, the A44 config
round-trip incl. omit-when-empty). Docker — `GoBoardDockerTests` (a WipeAndLoad reds without a
signoff, greens when `replace` is greenlit, reds on a too-narrow scope); `PeerAligned…` (the
engine refuses an ungreenlit WipeAndLoad BY NAME, sink untouched). The four wiping fixtures now
declare their greenlight; the sample's `golden-with-scope` carries a worked example. Release
clean (FS3511).

---

## 2026-07-09 — The transfer-guarantees program: wizard greenlight-write, airtight static-lookup, delete-scope emission gate, precise-impact artifact

Four hardenings so the operator blesses a destructive run on EVIDENCE and every declared
guarantee is held to its literal promise (full detail in `DECISIONS 2026-07-09`).

- **`check plan` greenlight-write** — after the wizard flips a flow to `replace`/`fresh`, a
  second prompt writes the matching `signoff` (`RelaxationStore.setFlowSignoff`), so the flow
  is not left immediately RED. The A44 move applied to the greenlight.
- **Airtight `static-lookup`** — `Reconciliation.staticLookupIdentity` asserts the two
  environments hold the IDENTICAL dataset (bidirectional column diff + extra/missing rows,
  surrogate-PK and `reconcileIgnore` excluded), enforced on the go board (`static lookup` axis)
  AND the engine (`transfer.staticLookup.diverged`, both realization paths) over the same
  `report.StaticLookupDivergences` — the two-traversal.
- **`delete-scope` emission gate** — `emission.signoff` (reusing the `WriteSignoff` vocabulary)
  greenlights the convergent-delete arm; `buildPolicyFromConfig` refuses
  `emission.deleteScope.ungreenlit` otherwise. Resolves the Active-deferral logged when the
  transfer signoff shipped.
- **Precise-impact artifact (`check go --impact`)** — `TransferImpact` segments the transfer
  graph, denormalizes each component into nested documents (owned children conjoined, refs
  inlined), and classifies every row (add/delete/change/unchanged); `TransferImpactView` writes
  a self-contained HTML artifact + JSON twin to `go-board/<flow>.impact.*`. Delta rows in full;
  the unchanged remainder counted. Design pinned by an approved mockup.

Pure suites green (Reconciliation +8, TransferImpact +10, delete-scope gate +3); Release clean.
Docker witnesses for `--impact` and the static-lookup board case are the named follow-on.

---

## 2026-07-09 — Audit follow-on: cross-environment mis-key landmines, identity-insert gate, fail-closed revert, journal durability, unproven-refusal witnesses

A multi-agent audit of the transfer engine / gates / resume-revert / test estate
surfaced a cluster of Tier-0 correctness landmines (silent mis-key on exit 0) and
Tier-1 destructive-write authorization holes. This entry records the slices landed
(each committed with tests; Release FS3511-clean; pure pool + transfer docker
classes green on the warm container). The remaining audit items are the named
follow-on at the bottom.

- **T0.1 — collation-parity reconcile match.** The Core reconcile match indexed
  business keys ORDINALLY while the sink and the go-board evidence probe match
  under the sink's `CI_AS` collation. Same fact, two rulers: a source key the
  board previewed GREEN the engine could drop (exit-9 on a green board), or a
  `FallbackToAssigned` pin silently re-keyed every ordinal-missed FK to the owner.
  A new named `Reconciliation.matchKey` (case-fold + trailing-trim; blankness still
  decided on the raw value) folds both the sink index and the source lookup for
  `MatchByColumn`/`MatchByColumns`, so engine and board agree by construction.
  Accent- and leading-space-sensitivity preserved; a per-column collation contract
  for non-default collations is the named follow-on.
- **T0.2 — fail-loud duplicate sink key (NM-58).** A duplicate reconcile key on
  the SINK re-keyed every matching source FK onto the oldest row, silently
  displacing the rest, on exit 0. `AmbiguousTargetMatchKeys` now counts in
  `droppedRowCount`/`hasDrops` (exit 9, cleared by `--allow-drops` or a
  `ManualOverride`), symmetric with NM-51's duplicate SOURCE key; the go-board
  forecast reads the same field (two-traversal).
- **T0.4 — capture-journal durability.** `append` now fsyncs (`Flush(true)`); the
  docstring NAMES the at-least-once window honestly (sink commits first, then the
  append — a crash between re-writes/re-mints the chunk on resume) instead of
  asserting atomicity; the write-ahead intent protocol that closes the window is
  the named follow-on. `load` and `openResumeIndex` now tolerate a torn TRAILING
  line (mid-crash partial, keyed on the missing closing newline) instead of
  throwing and wedging resume/revert; a newline-terminated corrupt line still
  throws (the not-silently-lossy contract holds).
- **T1.5 — identity-insert gated by signoff.** Identity-insert (explicit source
  PKs into an IDENTITY column under `KeepIdentity`) was a first-class `WriteMode`
  gated NOWHERE — the engine's signoff refusal fired only for `WipeAndLoad`. The
  engine now refuses `transfer.writeSignoff.ungreenlit` when the plan performs
  identity-insert without the flow greenlighting it, on ANY Execute (the merge
  path too). Detection is plan-derivable (`Transfer.identityInsertTables`:
  a `PreservedFromSource` load onto an IDENTITY-PK kind), so board and engine gate
  one fact. Breaking by design (like the 2026-07-08 signoff program); the NM-40
  test-only rename seams pre-greenlight it (inert under `Structural`), production
  routes through `runReverseLegThroughConnectionsWith`'s real signoff. The go board
  is env→env-scoped (AssignedBySink) and never drives an identity-insert flow, so
  no board twin here (the reverse-leg probe sheet is that surface).
- **T1.7 — fail-closed revert wrong-sink guard.** `projection revert` deletes BY
  KEY in whatever `--against` resolves to. The guard was fail-OPEN: a header-less
  script proceeded with a note, and only `database=` was compared. A pure
  `TransferRevert.guardVerdict` now refuses a header-less script
  (`revert.headerMissing`) and a `server=` OR `database=` mismatch
  (`revert.sinkMismatch`, server via `sink.DataSource`); `--force` overrides. The
  face is thin (parse + decide + render).
- **T1.9 — honest `fresh` impact text.** The `fresh` signoff prose claimed the
  target is "assumed empty ... overwritten without a diff", milder than the act
  (fresh performs the SAME full child-first wipe as replace). Corrected so
  greenlighting `fresh` is not misled about the blast radius.
- **Witnesses — two shipped guarantees pinned by name.** `staticLookupRefusalOf`
  made public: the pure pool now witnesses `transfer.staticLookup.diverged` (a
  divergent static-lookup refuses; a clean one passes). `orderedLoadGate` gains a
  witness: an alphabetical-degraded order refuses `transfer.loadOrderUnproven`.

**Proven:** Release FS3511-clean; pure pool 4073+; docker `PeerAlignedTransfer`,
`PeerManagedGrant`, `GoBoardDocker`, `TransferCanary`, `ReverseLegCanary` all green.
PR #663.

**Named follow-on (the remaining audit items):** T1.6 (route `--allow-drops`/
`--allow-cdc` through the durable signoff layer); T1.8 (mirror the WipeAndLoad
populated-sink refusal at the incremental seam — the merge-into-populated
duplication hole); T1.10 (signoff arm on the streaming reverse-leg path —
defense-in-depth, needs threading signoffs into the streaming signature); T0.3
(surface out-of-contract FK escapes as a named condition — needs an acknowledgement
path, since platform-`User` FK ubiquity makes a hard refusal too broad); the shared
two-environment peer fixture; and docker witnesses for `--impact`, the
static-lookup board case, and `supportingScope.inboundOrphan`.

### Addendum — T1.6 + T0.3 landed (same session)

- **T1.6 — drops/cdc via the durable signoff.** Row loss / CDC-flood is now
  acknowledged by the `--allow-drops`/`--allow-cdc` flag OR the flow's durable
  `Drops`/`Cdc` signoff (`Transfer.dropsAcknowledged`/`cdcAcknowledged`). `runCore`
  folds the signoff into the effective flags so the pre-write gates and the face's
  exit code read one acknowledgement — closing the "scripted `--go --allow-drops`
  drops rows with nothing durable to audit" gap. Streaming keeps flag-only (noted).
- **T0.3 — out-of-contract FK escapes refused; durable `foreignRefs` ack.** An
  in-subset FK to a NON-User kind absent from the acquired contract loaded the raw
  source surrogate (a silent cross-wire). The engine now refuses
  `transfer.subsetFkEscapes.targetOutOfContract` unless the flow declares the
  reference stable in `foreignRefs` (a first-class, A44-round-tripping config field
  threaded onto `WriteOptions.ForeignRefsAcknowledged`); the platform-User path is
  excluded (`Reference.IsUserFk`). Widening the acquisition is the other remedy.

Remaining named follow-on: T1.8 (incremental populated-sink refusal), T1.10
(streaming signoff arm), the shared two-environment fixture, and docker witnesses
for `--impact` / the static-lookup board / `supportingScope.inboundOrphan`.

### Addendum — T1.10 + T1.8 + the shared harness landed (same session)

- **T1.10 — streaming signoff arm.** `runStreamingReconcilingWithRenames` now
  threads the flow's `signoffs` and carries the same identity-insert gate runCore
  does, so the materialized and streaming gate blocks cannot drift. Inert on
  today's sink-mint streaming plan; a guard for a future preserve-keys path.
- **T1.8 — populated-sink refusal.** A merge/Incremental Execute into a sink that
  already holds rows in the transferred set now refuses
  `transfer.incremental.populatedSink` at the live-write seam — mirroring the go
  board's `re-run` axis over the same `populated` fact — instead of silently
  re-minting (duplicating) AssignedBySink rows. The escape is `strategy: replace`.
  The `re-run honesty` canary that pinned the old duplication is updated to the
  refusal. WipeAndLoad and first-loads are unaffected.
- **T3 — shared two-cell harness + witnesses.** `PeerEstateHarness.run2Cell`
  bootstraps two SsKey-aligned cells (source `OSUSR_*` / sink `OSUSR_X*`), reads
  both OSSYS contracts, and hands them to a body — a new two-cell witness is one
  call. Landed two docker witnesses over it for the shipped-but-unproven engine
  refusals: `transfer.supportingScope.inboundOrphan` and (peer-path)
  `transfer.incremental.populatedSink`.

Remaining named follow-on: docker witnesses for `check go --impact` and the
static-lookup board case (the `PeerEstateHarness` makes these cheap now); the
streaming write-ahead intent protocol (T0.4's core duplication window).

### Addendum — the remaining follow-ons closed (same session)

- **Docker witnesses over the shared harness** — the shipped-but-unproven engine
  guarantees now fire against a live pair (`PeerWitnessDockerTests`):
  `transfer.supportingScope.inboundOrphan`, `transfer.incremental.populatedSink`,
  `transfer.staticLookup.diverged`, and `TransferImpact`'s add/delete/change/
  unchanged classification against a real two-DB delta (`readKindRows` added to
  `PeerEstateHarness`).
- **B — the streaming write-ahead intent protocol** (T0.4's core) — a chunk is
  journaled INTENT (fsync'd) BEFORE the sink write and COMPLETE after; a crash
  between leaves an IN-DOUBT chunk the resume path probes (sink row count vs the
  kind's completed-chunk total) — non-committed → safe re-write; maybe-committed →
  refuse `transfer.resume.chunkInDoubt`. The silent re-mint / duplication window
  is closed; normal resume is preserved (ReverseLegStreaming green).

The partial-transfer hardening program (Tier-0/1 + the shared harness + the four
docker witnesses + the streaming intent protocol) is complete on this branch.

### Addendum — cloned-module (name-aligned) peer leg (2026-07-09)

A NEW secondary case: two CLONED modules — an espace cloned into a
differently-named module whose entities are same-named with identical
attributes, but which are DISTINCT OSSYS entities (different native-GUID
`SsKey`s). The existing peer leg pairs by SsKey equality end to end (shape gate,
rename map, `DataLoadPlan.buildWith`), so a clone reads as all-removed/all-added
— the shape gate refuses, and a bypass would load `[]` for every kind. Support
is opt-in and DEFENSIVE:

- **`NameAlignment.align` (new pure pass, `Projection.Pipeline`).** Rewrites the
  SOURCE contract's SsKey-bearing fields (module / kind / attribute / reference /
  index) to the SINK's corresponding SsKeys, matched BY NAME within an
  operator-declared source→sink MODULE map, keeping physical coordinates / types
  / structure. After the pass the pair is SsKey-aligned, so the entire existing
  engine runs UNCHANGED (the aligned clone diffs to zero — the core pure witness).
  Shape is judged on the espace-safe logical shape (`Readiness.toLogicalShape`),
  so a physical default-constraint-name difference is not a false divergence.
- **Named refusals.** `alignment.module.unmatched` (short-circuit),
  `alignment.entity.unmatched` / `.ambiguous`, `alignment.attribute.unmatched` /
  `.shapeDivergence` (STRICT — any `AttributeFacet` difference), and the
  config-parse `alignment.mode.unknown`. FK targets with no sink counterpart keep
  their source SsKey and fall to the existing T0.3 out-of-contract gate — no new
  code. Entity/attribute mismatches are collected into one report.
- **Identity basis is A1-BOUNDED.** Name-derived identity is materially weaker
  than the native GUID; the mode is entered only by explicit opt-in
  (`alignment: "by-name"` + `alignMap` on the flow, threaded like `foreignRefs`),
  never auto-detected (a mis-wired pair must not silently "align by name"). Peer
  leg only; the rendition reverse leg keeps `by-sskey`.
- **Placement.** The pass runs at the PEER FACE (`Faces/Transfer.fs`), rebinding
  the acquired source before the SsKey-keyed gates, and identically in the go
  board's `TransferPeer` branch (board/engine two-traversal parity) — no change
  to `WriteOptions` / `runCore` / the runner.
- **Proof.** `NameAlignmentTests` (10 pure: the diff-to-zero + shape-gate-Ok
  witness, FK re-point by identity, every refusal, determinism) + the A44
  round-trip for `alignment`/`alignMap` + a live docker witness
  (`ClonedModuleTransferDockerTests` over `OssysSeedBuilder.asClonedModule`, which
  re-mints every `SS_Key` GUID): the un-aligned pair refuses `shapeDivergence`,
  the name-aligned pair reads as one shape and lands the subset with the FK
  re-pointed by identity.

**Refinement (same day) — strictness is subset-scoped.** `align` now takes the
resolved load scope: an entity actually being transferred must be a clean clone
(name + strict shape) or the pass refuses; a neighbor in a mapped module is
re-keyed by name where it uniquely matches (so FK targets stay consistent) but a
name/shape mismatch there is NOT a refusal (its data is not moving). A full
transfer (no subset) stays strict estate-wide. `alignForMode` resolves the
declared `tables` against the source (names are rewrite-invariant) and the face
/ board pass it in — so an unrelated drifted table can no longer block a scoped
transfer, while the transferred set is still held to the strict-clone bar.

**Hardening (multi-agent review) — the identity re-keying can no longer silently
corrupt or be silently dropped.** Three adversarial reviews confirmed the pass's
core safe (bypass surface, reconcile/align SsKey-space consistency, board/engine
parity, `toLogicalShape` completeness, determinism, feature-interaction matrix)
and surfaced two defects, both now closed:

- **Identity injectivity.** `align` guarded sink-side ambiguity but trusted
  source-side and map-value uniqueness; `Map.ofList` on source-keyed pairs would
  launder a many-to-one VALUE collision (two source identities → one sink) into
  duplicate SsKeys that `CatalogDiff` silently collapses (rows vanish). Reachable
  from config (`alignMap: {A: Shared, B: Shared}`). Now refused two ways: a
  value-injectivity guard in `align` (`alignment.identityCollision` — the run-time
  net over module/entity/attribute/reference/index/sequence collisions) and a
  parse-time `cli.config.alignMapNotInjective`. A clean clone is 1:1, so neither
  fires on a genuine clone.
- **Silent drop on mis-route.** `alignment: by-name` is consumed only on the
  `UpPeer` route; a by-name flow that forgot `rendition: physical` routed to the
  name-blind leg and dropped `alignMap` silently. `resolveFlowSpec` now refuses
  `cli.flow.alignmentNotPeer` when a by-name flow is not a physical→physical peer.

Plus: the shape-divergence refusal renders facet names cleanly (no `%A`); the
three best-effort by-name loops collapsed to one helper. New tests pin the
strict-scope-*bites* path, the collision + mis-route + FK-escape + sequence
refusals/behaviors, the `AlignmentMode` round-trip, and — at the live seam — the
FK re-point at ROW level (reseed the sink identities, then assert no verbatim and
no dangling FK), the one hazard the pass exists to prevent.
