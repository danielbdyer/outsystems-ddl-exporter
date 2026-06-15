# THE VECTOR — Execution Kickoff

### The operational contract for implementing the treatise (for the next agent + its agent teams)

> **What this is.** The executable companion to `THE_VECTOR.md` (executive) and `THE_VECTOR_UNABRIDGED.md` (full
> annals). Those two derive *what* to do and *why*; this one is *how to run it*: the read order, the environment
> scars, the guardrails, the surface map, the orchestration shape, the per-move definition of done, and the gated
> wave plan. Read the two treatises (their `Reconciliation addendum — 2026-06-15` first), then this.
>
> **You are authorized to orchestrate.** This task is explicit opt-in to multi-agent orchestration: **author and
> run `Workflow`s** (one per wave or move-cluster) with teams of subagents. (If a session reminder says ultracode
> is off, treat *this authorization* as the standing opt-in for the VECTOR work.) Do not re-derive the analysis —
> it is done, adversarially verified, and gated. Implement from the specs.

---

## §0 — The situation (read this first)

- **The core analysis is intact and verified on `main` (2026-06-15).** The keystone surfaces are unchanged:
  `PhysicalSchema.fs` (`PhysicalForeignKey` has no `IsTrusted`; `ofCatalog` takes no `DecisionOverlay`),
  `Tolerance.fs` (exactly 8 variants, all `@ladder` Schema/Data — zero Decision/Identity/Time),
  `NORTH_STAR.matrix.generated.md` (L1 5/5 · L2 4/5), `AXIOMS.md`/`AxiomTests.fs` (no drift). The counts hold
  (112 citations, 42 Skips, seven emitter targets). **Every move below is still live.**
- **The reverse-leg DML engine has landed** (Phases 2–5, merged): NM-31 closed (the streaming arm has a reconcile
  leg), resume/idempotency/dry-run hardened. *Build on it, not it.* It makes **`M3`** (the real-wire swept proof)
  more ready than the treatise assumed — the real inverse leg now exists and is witnessed.
- **The database-archetype work has landed and is at rest** (`Archetype = FullRights | ManagedDml`,
  `CapabilityProfile.of`, `KeymapSpill.fs`, `reconcileArchetype`). **It is NOT a concurrent program** — there is
  no in-flight track to coordinate with, and you may edit any surface freely. It matters to you two ways: (1) as a
  **precedent to copy** (§4), and (2) because two files it extended overlap a VECTOR move — read their current
  shape before editing (§5).

## §1 — Read order (≈45 min, before any code)

1. `KICKOFF.md` — the five-minute brief.
2. `THE_VECTOR.md` + `THE_VECTOR_UNABRIDGED.md`, **addendum first**, then §6/§7 (executive) or Parts VII/VIII
   (unabridged): the toolbox (M1–M20) and the gated wave roadmap.
3. `THE_USE_CASE_ONTOLOGY.md` — the target index (the move alphabet, the laws T12–T16 + A43).
4. `AXIOMS.md` + `tests/Projection.Tests/AxiomTests.fs` — run
   `dotnet test --filter "FullyQualifiedName~AxiomTests"`; do not trust restated counts anywhere.
5. `DECISIONS.md` — the operating-disciplines index + the **Active deferrals** index + the 2026-06-15 entries.
6. `CLAUDE.md` §4 (survival rules) + §5 (the load-bearing commitments).
7. `DATABASE_ARCHETYPES.md` §1–§2 — the precedent you will copy for `M4`/`M15`.

## §2 — Environment & build discipline (the scars that will bite you today)

- **⚠️ Docker tests SILENTLY NO-OP on this Windows box.** `DockerDaemon.ensureRunning()` checks a *Linux* socket,
  so every `EphemeralContainerFixture` test passes as a ~0.4 ms `()` no-op **unless** the warm-container conn is
  set: `$env:PROJECTION_MSSQL_CONN_STR="Server=localhost,11433;User Id=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False"`
  (`scripts/warm-sql.sh conn` prints it). **Confirm via per-test TRX durations in seconds, never the green count**
  (this is survival-rule #12 in operational form). The keystone (`M1`) and `M3` have Docker-gated witnesses — this
  *will* matter.
- **`dotnet` is not on the bash PATH** — it lives at `C:\Users\danny\AppData\Local\Microsoft\dotnet\dotnet.exe`.
  Build and test via **PowerShell**.
- **Never run the pure + Docker pools in one `dotnet test`** — it OOM-kills the host. Use `scripts/test.sh`
  (`fast` / `docker` / `canary` / `focus <name>` / `all`). **CDC test classes use `IsolatedContainerFixture`**
  (`sp_cdc_enable_db` flips instance-wide state — never on the warm container).
- The scripts you will lean on: `scripts/test.sh`, `scripts/warm-sql.sh`, `scripts/matrix-status.sh` (regenerates
  `NORTH_STAR.matrix.generated.md`), `scripts/verifiability-gate.sh` (the phantom-coverage gate),
  `scripts/run-analyzers.sh`, `scripts/perf-gate.sh`. Each is its own documentation.
- **FS3511 in Release**: no `let rec` / tuple-`let!` / tuple-pattern-`for` inside `task { }` — hoist helpers,
  bind single values. **`[<Literal>]` only on CLR primitives** (`[<Literal>] decimal` detonates at module load).
  At any `{ X.create … with … }` site, **count the fields** — omitted fields silently inherit ctor defaults with
  no warning.
- **A perf-gate verdict is VOID if anything else runs on the host** — run it solo before believing a regression,
  and re-record the baseline only with a DECISIONS amendment.

## §3 — Non-negotiable guardrails (hard constraints)

- **Honesty before features.** Execute **Wave 0 first.** Never build atop an over-claim.
- **Every `AXIOMS.md` change carries its `AxiomTests.fs` witness in the same commit.** Every new erasure is a
  closed-DU `ToleratedDivergence` variant with its `@ladder` tag — never a silent drop.
- **Pure core** (zero I/O, no clock, no `Task`/`Async`, no module-level mutable except `Bench`); **A18** (Π never
  takes `Policy`); **typed-AST-first** (no `StringBuilder` for SQL/JSON/XML); **determinism is constructed** (sort
  by `SsKey`; byte-identical output is T1). Any curried-prefix `DecisionOverlay`/overlay argument **defaults to
  `empty` and must preserve byte-identity at `empty`** — guard with the existing T1 goldens (`GoldenEmissionTests`).
- **IR-grows-under-evidence: do NOT build the deferred moves** (§10). A feature that is not a corollary of the
  adjunction with a fired trigger is the wrong feature.
- **Total decisions, named refusals, downgrades never silent.** The cutover-safety commitments (R6, the T-30/T-15
  ladder, V1 stays warm) do not loosen.

## §4 — The landed precedent to copy

The archetype work just demonstrated the exact discipline several VECTOR moves require — copy it rather than
inventing a shape:

- **For `M4` (`ConstraintState` DU) and the `M15` fitness functions — copy `CapabilityProfile.of`.** The archetype
  is a **closed DU** that **expands at one total site** (`CapabilityProfile.of` in `MovementSurface.fs`) into a
  per-capability bundle, with the coarse legacy facet (`Grant`) demoted to a **derived projection**
  (`Archetype.grant ∘ ofGrant = id`, round-tripped in `ArchetypeTests.fs`) and **reconciled against probed
  evidence** (`CapabilitySurvey.reconcileArchetype → ArchetypeFinding`). That is precisely the move `M4` makes for
  `(HasDbConstraint, IsConstraintTrusted)` → `ConstraintState`, and the shape `M15`'s in-assembly Facts should
  take. The pattern, end to end, is in the repo now.
- **For `M1` (the keystone) — copy the curried-overlay-defaulting-to-`empty` discipline.** The SSDT emitter is
  already overlay-curried and byte-identical at `DecisionOverlay.empty` (slice-2.2 T1 test). Symmetrize the
  *reader-side* projection (`PhysicalSchema.ofCatalog`) the same way: `ofCatalog : DecisionOverlay -> Catalog ->
  PhysicalSchema`, default `empty`, byte-identical guard. `ReadSide.fs:1171` already recovers `is_not_trusted` —
  the read leg is free.
- **For the private-ctor / closed-DU laws generally** — the house derive-macro (`private` + smart ctor +
  `[<RequireQualifiedAccess>]`) is enumerated in `CONSTELLATION.md` §9.8.9; `ArtifactByKind` and now `Archetype`
  are the worked examples.

## §5 — Surface map (which files each move touches)

Most moves are on surfaces disjoint from the recently-landed work, so you can fan out freely. The **only two files
to read in their current shape before editing** (because the archetype work extended them) are flagged ⚠.

| Move(s) | Primary surfaces |
|---|---|
| `M1` keystone | `Projection.Core/PhysicalSchema.fs`, `Projection.Targets.SSDT/PhysicalSchemaReader.fs`; tests `CanaryRoundTripTests.fs` / `DecisionEmissionTests.fs` / `AdjunctionLawTests.fs` |
| `M1′` / `M2` tolerances | `Projection.Core/Tolerance.fs`, `Projection.Targets.SSDT/Render.fs` (the `CreateTrigger` skip), `Projection.Core/CanaryResidual.fs`; regen via `scripts/matrix-status.sh` |
| `M3` swept proof | `tests/.../CatalogCodecTests.fs` (`catalogGen → genCatalogPair`), a new property file, `AxiomTests.fs` |
| `M7`/`M11`/`M12`/`M13` algebra | `Projection.Core/CatalogDiff.fs` |
| `M5` digest | `Projection.Core/VersionedPolicy.fs` |
| `M8` JSON seam | `Projection.Targets.Json/*`, a shared writer helper in Core or `Targets.Json` |
| `M9` binding algebra | `Projection.Pipeline/*Binding.fs`, `Pipeline.fs` |
| `M6` `[<Struct>]` surrogate keys ⚠ | `Projection.Core/SurrogateRemap.fs` — **now also carries `IdentityPolicy`/`SinkLoadCapability` (archetype Slice C); read it first** |
| `M15`/`M17` fitness + totality ⚠ | `tests/Projection.Tests/`, `Projection.Analyzers/`, `scripts/run-analyzers.sh`, CI config; `Projection.Pipeline/CapabilitySurvey.fs` — **now also carries `reconcileArchetype`/`ArchetypeFinding`; read it first** |
| `M16` citation gate + matrix binding | `AxiomTests.fs`, `scripts/matrix-status.sh`, `scripts/verifiability-gate.sh`, `AXIOMS.md`/`PRODUCT_AXIOMS.md` |
| `M18` `toJson` | `Projection.Pipeline/ReportRun.fs`, a JSON codec in `Targets.Json` |
| `M4` `ConstraintState` | `Projection.Core/Catalog.fs` + the catalog codec — **behind the persisted-state-migration story** |
| `M14` `Traversal` | `Projection.Core/Optics.fs`, `Catalog.fs` — **gated on the compile-order split** |

## §6 — Orchestration shape (per wave)

1. **Scout** the wave's moves against live code (one read-only pass): confirm each move's surfaces, the exact
   witness test it must turn green, and whether that witness is pure or Docker-gated.
2. **Fan out** the **independent** moves to implementer agents, each in its **own git worktree**
   (`isolation: "worktree"`) so parallel edits never conflict. **Serialize** moves that share a file (e.g. the
   several `CatalogDiff.fs` moves `M7`/`M11`/`M12`/`M13`; the `Tolerance.fs` moves `M1′`/`M2`).
3. **Adversarially verify** each move as it lands — the same discipline that built the treatise: build green
   (warm, per §2) + the **named witness test green** + `scripts/matrix-status.sh` regenerated + `verifiability-
   gate.sh` clean. Default the verifier to skeptical; a move is done only when its witness is a live green test or
   a regenerated artifact, **never a judgment**.
4. **Wave-close** (one integrator agent): merge the worktrees, run the relevant suite warm, perform the
   chapter-close ritual (§7), and confirm the wave's exit criterion.

## §7 — Per-move definition of done + the chapter-close ritual

**A move is done when** all hold: (a) the named **witness test is live and green** (and its citation is registered
so `M16`'s gate will check it); (b) the build is clean in Release; (c) if it added/changed an axiom, the
`AxiomTests.fs` entry is in the **same commit**; (d) if it added an erasure, a closed `ToleratedDivergence` variant
+ `@ladder` tag exists and `scripts/matrix-status.sh` regenerated the matrix; (e) `verifiability-gate.sh` is clean;
(f) if it touched a `Bench`-labelled hot path, the perf-gate is green (run solo).

**The chapter-close ritual (per wave):** regenerate the matrix and confirm the intended cell moved (or is honestly
capped); write the `AXIOMS.md`/`Tolerance.fs` amendments and a `DECISIONS.md` entry naming the wave; **prepend** a
forward-looking top letter to `HANDOFF.md` (never overwrite); update `CLAUDE.md` only if a Tier-1 surface changed
ownership; and re-verify the survival list. Then pause and report (§9).

## §8 — The waves (Part VIII of the treatise — in order, gate between them)

- **Wave 0 — Honesty & fitness** *(all S/M, no new capability; protects everything after).* `M1′`
  (`DecisionNotReadBack`/`FkTrustUnreflected` tolerance) · `M2` (name the silent `CreateTrigger` drop) · `M16`
  (`citationOf` existence gate + structural matrix binding) · `M15` (in-assembly fitness Facts; promote
  `run-analyzers.sh` + the perf-gate to CI) · the count corrections (112/42/seven targets/linear-writer-only).
  **Exit:** no green matrix cell is unfalsifiable-by-construction; the guardrails are in-assembly.
- **Wave 1 — The keystone.** `M1` (`PhysicalForeignKey.IsTrusted` + overlay-aware `PhysicalSchema.ofCatalog` + the
  decision-readback property; read leg free at `ReadSide.fs:1171`). **Auto-retires `M1′`.** **Exit:** the live
  canary witnesses `NoCheckFk`/`EnforceUnique` survival.
- **Wave 2 — Reversible algebra & real-wire proof.** `M11` (triangle-inequality theorem) · `M13` (drop the
  vestigial `Result` on `between`) · `M12` (the ~3-line groupoid `inverse`) · `M3` (`genCatalogPair` + swept T16,
  in-process backend first; the landed reverse leg supplies the real backend) · `M20` (transactionality
  honest-naming half: `GateLabel.MidWriteNotProtected` + the pure gate — **not** the deferred live wrapper).
  **Exit:** the groupoid laws green; the change algebra property-swept; the destructive failure mode a named refusal.
- **Wave 3 — Compression & idiom.** `M7` (diff `ChannelSpec`) · `M8` (`JsonDocumentWriter` seam) · `M9` (FsToolkit
  `validation { }` bindings + close the model-read/live divergence) · `M17` (totality functor) · `M6` (`[<Struct>]`
  on `SourceKey`/`AssignedKey`, measure-then-promote) · `M19` (`[<Measure>] row`) · the `Changed → Reshaped`
  rename. **Exit:** the descriptor-duplication retired; the riskiest config seam closed.
- **Wave 4 — Corollary cashes & gated deepenings.** `M18` (`ChangeManifest.toJson`) · `M14` (`Traversal` optic —
  only after the compile-order split is resolved) · `M4` (`ConstraintState` DU — **behind the persisted-state-
  migration story**: name NM-34 as the store-codec contract; gate every serialized-form change) · `M5` (digest
  projection). **Exit:** the CDC-norm machine-queryable; the store-migration checklist active.

## §9 — The first move, and the cadence

**Make `M1′ + M2` first** — the two cheapest moves (a handful of closed-DU variants). They cost almost nothing and
immediately stop the engine over-claiming on three of five axes, restoring the one guarantee the whole epistemic
spine rests on (*the generator under-claims, never over-claims*). Then **`M1`**, the real cash, which turns the
honest tolerance back to an earned green. Honesty first, then the theorem.

**Cadence:** treat each wave as a chapter — run its workflow, integrate, run the chapter-close ritual, then
**pause and report** with the green-test/artifact evidence and the updated `HANDOFF.md` top letter before opening
the next wave. Stay in the loop; do not silently chain all five waves.

## §10 — Deferred-with-trigger (do NOT build)

Honor the treatise's moat verbatim:

| Deferred | Re-open trigger |
|---|---|
| Full `SchemaMove` unification | a real divergence between the four move enumerations fires (do only the zero-risk `Changed → Reshaped` rename) |
| The live atomic `BEGIN TRAN` wrapper | the managed-login grant survey resolves (per `Preflight.fs`) |
| `TighteningStrategy` descriptor (`M10`) | a fifth tightening intervention, or a divergence between the four |
| DACPAC reader / second `Ingest` source | a second catalog source materializes |
| Policy-version plane → Episode | a cross-run digest-comparison need + `M5` (digest stability) |
| `LineageTree` consumer | a pass that genuinely branches its lineage materializes |
| Permissions as a projected **content** axis | a flow must *publish* grants (the eject) — note the *capability* disposition already landed via the archetype |
| Docker N≥20 real-wire FsCheck sweep | coverage-guided fixtures **or** an N≥20 container pool |
| `EmitError` split into layer-scoped types | a second consumer needs the layer distinction |
| Dead FK-config retirement | a DECISIONS amendment names the V1-parity obligation discharged |
| ReadSide flat-synthesis change | an authored-schema *attribute* round-trip is shown to fail (then the fix is per-attribute extended-property emission, `M-equivalent L3.M6`, **not** changing the flat fallback) |

---

*Hold the spine. Name every refusal, count every crossing, and leave the books balanced. The treatise is the
destination; this is the order of the steps.*
