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
