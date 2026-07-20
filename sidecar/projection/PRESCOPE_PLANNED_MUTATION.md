# Prescope — Planned Mutation (P-PLAN: no write to a connected database is unplanned)

> **Status: design, drafted 2026-07-20.** The buildable route for one guarantee — **every byte
> written to a connected database is the realization of an approved, previewable plan; nothing
> reaches the wire that the plan did not name; and the wire is measured against the plan so an
> unplanned write is a *norm violation*, not a silent leak.** Scratch/temp objects are in scope:
> a temp table is a mutation (of tempdb, or of the user DB when it is a named staging table), so
> it must be planned like any other.
>
> **Scope guard:** this prescope concerns the **v2 application at `sidecar/projection` only**. V1
> is out of scope (it owns the production write path under R6; `ADMIRE.md`).
>
> **Grounding:** `WAVE_6_ONTOLOGY.md` (the moves §5; the DacFx seam §4; P-GATE / P-EXE §7; the
> data leg §12) + `WAVE_6_ALGEBRA.md` / `AXIOMS.md` T14–T16 + A43 (the change algebra this law
> constrains) + `EXECUTION_PLAN.md` 6.C / 6.F (the pre-flight suite and the publication route) +
> `THE_CONFIG_CONTROL_PLANE.md` (A44; `grant` = the refusal gate, `scope` = the move projection).
> Every `file:line` below is from the live source **at time of writing** — treat the line numbers
> as a pointer to re-verify, not a fact to trust (CLAUDE.md §8: a restated count here is a bug).

---

## 0. The honest finding — this is a *closure* problem, not a *greenfield* one

The instinct behind "ensure no unplanned writes ever happen" is that the engine writes carelessly.
It does not. The engine already carries, shipped and witnessed, a rich **per-plane, application-level**
write governance:

- **The two-gate authorization.** A live write needs *both* the operator's intent (`--go` / `Commit
  = true`, `MovementSpec.fs`) **and** the environment's authorization (`PROJECTION_ALLOW_EXECUTE=1`,
  checked in every write face — `Faces/Migrate.fs`, `Faces/Synthetic.fs`, `Faces/Operational.fs`).
  `MovementSpec.isLiveWrite` localizes "does this run touch the wire?" to a single predicate;
  `Transfer.Mode = DryRun | Execute` makes preview the default.
- **The consent alphabet.** `ActConsent.Act` (`Projection.Core/ActConsent.fs`) is a *closed,
  severity-ordered, SHA-256-fingerprinted* set of destructive/creative acts, derived once by
  `actsOf` and read by **both** the go-board and the engine's execute gate — the two-traversal
  discipline that makes "the blessed set and the performed set cannot drift" a structural fact.
- **The gate completeness machine.** `Preflight.GateLabel` (`Projection.Pipeline/Preflight.fs`) is a
  closed DU with a *total* `code → (exit, label)` classifier (`labelOf` ∘ `exitOf`): every refusal
  routes to a named axis or the named `UnclassifiedRefusal` default — never a silent miss. The
  connection (A1), permission (A2), and transactionality (A3) gates already live here, composed by
  `Preflight.all` / `allReporting`, refusing *before any mutation*.
- **The mode-level signoff.** `WriteSignoff` refuses by name until the destructive `WriteMode` a run
  performs is greenlit; `ApprovalWorkflow` binds a policy version to a reviewer.

What is missing is not *governance*. It is **closure** — three holes through which an *unplanned*
write can still reach a connected database, and the absence of a **single spanning law** that unifies
the per-plane pieces so the guarantee holds for *every* seam, not one verb at a time:

| # | The hole | Evidence (at time of writing) |
|---|---|---|
| **H1** | **No read-only-by-default.** Every connection opens read/write-capable; a "read" seam *can* mutate, and nothing at the connection layer stops it. | Zero `ApplicationIntent=ReadOnly` in the tree. `ConnectionResolver.openSubstrate` (`Adapters.Sql/ConnectionResolver.fs`) and the parallel openers (`ConnectionSpec.fs`, `Source.fs`, `Deploy.fs`) all open RW. The OSSYS *read* script even creates temp tables (`SnapshotScopeBinding.fs` — `#E`/`#Ent`/`#Attr`). |
| **H2** | **Scratch/temp is un-modeled.** Temp tables, session staging, and the persistent progress marker are mutations that appear in *no* plan, manifest, or consent surface. | `SurrogateCapture.fs` (`#__projection_capture`, `#__projection_keymap`), `KeymapSpill.fs` (`#projection_keymap_spill`), `Bulk.copyRowsSession` (session `#` staging), `TransferResume.fs` (`dbo.__projection_transfer_progress` — a *real* user-DB table). |
| **H3** | **The gates are imperative, not structural.** Every guarantee above is a check *a developer must remember to call*; a new write site, or a new opener, bypasses the whole apparatus and no test or compiler notices. | `Preflight`/`WriteSignoff`/`ActConsent` are pure modules invoked at call sites in `TransferRun.runCore` / the faces; there is no analyzer or type that makes an un-gated write *impossible*, the way `NoUnsafeTimeInCoreAnalyzer` makes a clock-in-Core impossible. |

The thesis of the ontology answers exactly this (`WAVE_6_ONTOLOGY.md` §0): **isomorphism turns a
discipline of vigilance into a property of structure.** The per-plane gates are the vigilance; this
prescope is the structure.

---

## 1. The physical premise — what "unplanned" costs (L0)

"Unplanned" is not an aesthetic complaint; every mutation has a physical price the operator is
entitled to have *named before it is paid* (`WAVE_6_ONTOLOGY.md` §1.1, §12.1):

1. **`sp_rename` keeps the pages; `DROP`+`ADD` deallocates them.** An unplanned "rename" that lowered
   to DROP+ADD destroys the bytes it claimed to preserve.
2. **`ALTER COLUMN` takes a schema-modification lock**; a size-of-data change rewrites every row. An
   unplanned ALTER is an unplanned outage.
3. **`NOT NULL` / narrowing is checked against the bytes on disk and aborts mid-statement.** An
   unplanned tightening half-applies.
4. **CDC writes one capture row per row-version.** An unplanned DML re-touch is permanently visible in
   `cdc.<table>_CT` — which is exactly why CDC can *rule* on the plan (§7 below).
5. **A temp table is a write.** `SELECT … INTO #t` allocates in tempdb; a named staging table
   allocates in the user DB. A "read-only" analysis that stages into a temp table has mutated a
   connected database — `SnapshotScopeBinding`'s `#E`/`#Ent`/`#Attr` do this on the read path today.
6. **`sp_cdc_enable_db` flips instance-wide state** (CLAUDE.md §4.1); `CREATE`/`DROP DATABASE`
   mutate the catalog. Unplanned catalog moves are the least reversible of all.

The law does not forbid these. It requires that each be an element of a plan the operator can see and
refuse *whole*, before the first page is touched — and, for the reversible cost (CDC), *measured*
after, so the wire is proven to match the plan.

---

## 2. The law — P-PLAN (the Planned-Mutation Law)

> **P-PLAN [law].** Let `M` be the set of mutations a run realizes against a connected database —
> DDL, DML, bulk, catalog procedure (`sp_rename`, `sp_cdc_*`, `CREATE`/`DROP DATABASE`), *and scratch*
> (temp tables, session staging, progress markers). Then:
>
> 1. **Planned.** `M ⊆ realize(plan)` — every mutation is the realization of an element of an
>    `ExecutionPlan` value computed *before* any write. `plan` is a pure function of `(Catalog ⊖
>    Catalog, Data δ, Policy)` — the transformation differential (`WAVE_6_ONTOLOGY.md` §4), extended
>    to carry its scratch and catalog moves.
> 2. **Previewable.** `plan` is renderable with **zero** wire mutation (the `DryRun` / dry-run
>    default). Preview is not a courtesy; it is the plan's *identity* — the same value the executor
>    consumes.
> 3. **Gated whole.** No element of `M` reaches the wire unless `Preflight.all plan` returns `Ok` —
>    the gate set is *complete* w.r.t. the plan's preconditions (P-GATE), and a refusal is named and
>    fail-loud, leaving the substrate untouched.
> 4. **Authorized.** The write path is entered only through an **unforgeable capability** minted by
>    the two-gate rule (operator `--go` ⊕ environment `PROJECTION_ALLOW_EXECUTE`) plus, for each
>    named destructive/creative act, its fingerprinted consent (`ActConsent` / `WriteSignoff`).
> 5. **Closed-set.** `realize(plan)` is executed by exactly **one** sanctioned executor; a mutation
>    attempted outside it does not compile (analyzer) or fails closed at the executor (its statement
>    identity is not in the plan's manifest). — *the structural half; closes H3.*
> 6. **Read-only by default.** A connection opened for a non-write role carries
>    `ApplicationIntent=ReadOnly`; write capability is an *escalation* that requires the plan. —
>    *closes H1.*
> 7. **Measured.** After apply, the realized norm equals the planned norm: `|capture(M)| = ‖plan‖`
>    per channel (P-DM / T15). An unplanned write is a norm *excess* — caught by the ruler, not
>    trusted away. — *closes the observability leg.*

**Tags.** Clauses 1–4 are **[law]** grounded in shipped witnesses (they *restate* and *span* what the
per-plane gates already prove). Clause 5's analyzer half and clause 6 are **[policy]** (V2's choice of
*how* to close the hole). Clause 7's general case (`‖δ‖ = k`, not just `0`) is **[target]**
(`EXECUTION_PLAN.md` 6.F.3-data (a)).

### 2.1 Why the algebra already forces it

P-PLAN is not a new axiom bolted on — it is a **constraint on the `run` in the master equation**:

- **T16 (the Project square).** `run( emit(B ⊖ A), realize(A) ) = realize(B)` modulo residual
  (`AXIOMS.md`). The equation quantifies over `run` — *the* act of touching the substrate. P-PLAN
  says: `run` may only ever be applied to `emit(δ)` for an *approved* δ, through the sanctioned path.
  Every other write is outside the square — undefined, therefore refused.
- **T15 (norm conservation).** `‖emit(δ)‖ = ‖δ‖`, and **the CDC capture count *is* the norm**. This
  is the deep gift: "no unplanned write" becomes **measurable**. The plan predicts `‖δ‖`; the wire
  (CDC) reports `|capture|`; an unplanned write makes `|capture| > ‖plan‖` — an *isometry break*. The
  guarantee is a theorem with a ruler, not a hope. (The `‖δ‖ = 0 ⟹ 0 captures` instance is the
  shipped CDC-silence floor; P-PLAN clause 7 is its generalization.)
- **T14 (channel decomposition).** `δ = ⊕_c π_c(δ)`, `‖δ‖ = Σ_c ‖π_c δ‖`. The plan is *already*
  channel-partitioned (rename ⊥ reshape ⊥ data), so the manifest and the norm check are per-channel
  by construction (P-CH). A scratch channel is a *new orthogonal summand*, not a special case.
- **A43 (identity conservation).** `SsKey` is the conserved charge; a faithful rename induces zero
  data moves. So the plan's *coordinate* for every mutation is the identity (§3 name-spaces), never
  the physical name — the same reason renames go through the refactorlog.
- **A35/A36 (the statement stream).** `emit`'s codomain is `seq<Statement>`, and
  `Deploy.executeStream : SqlConnection -> seq<Statement> -> Task<unit>` is its realization. That
  bare `SqlConnection` in the signature *is* the ambient-authority hole (H3): anyone holding a
  connection and a stream can write. P-PLAN clause 5 replaces the bare connection with a capability
  that carries the plan.

### 2.2 The discriminating predicate (right-by-function, not right-by-name)

Per the ontology's §8 method, the test that earns P-PLAN is **not** "the migrate canary writes the
right thing" (that passes even if a second, un-manifested write also fired). It is the *adversarial*
witness — the input on which a plausibly-named-but-wrong implementation diverges:

> **P-PLAN discriminator.** Inject, into a live `Execute` run, a mutation the plan did not name — a
> stray `INSERT`, an undeclared temp table, a second `ALTER`. The correct engine **refuses or
> fails-closed** (compile error at the new call site, or executor rejection because the statement
> identity ∉ manifest, or a post-apply norm excess `|capture| > ‖plan‖`). A name-driven engine — one
> that checks "did the *planned* writes happen?" — passes, because the planned writes *did* happen;
> it is blind to the *extra* one. The discriminator tests the blindness, not the happy path.

---

## 3. The mutation alphabet — lifting `ActConsent` from one plane to all seams

`ActConsent.Act` is the model to generalize. It is already closed, fingerprinted, and two-traversal —
but scoped to the **peer transfer** plane, as its own source admits (`ActConsent.fs`: `DeleteScope` is
"*in the DU for closure; the peer materialized path never emits it — the emission lane owns
delete-scope*"). That comment *is* the per-plane seam. P-PLAN's manifest spans it.

The spanning taxonomy — every mutation the six write-path files can realize, mapped to its move
(`WAVE_6_ONTOLOGY.md` §5/§12.3), its physical realization, its **current** governance, and the hole:

| Mutation | Move | Realized by (write-path `file`) | Physical | Governed today by | Hole |
|---|---|---|---|---|---|
| `CreateTable` / `AlterAdd` | Add | `Deploy.executeStream` → `executeBatch` | new pages | Preflight; DacFx seam (declarative) | — |
| `AlterColumn` (widen/narrow) | Reshape | `MigrationRun` `executeSegments` | SCH-M lock; maybe rewrite | Preflight tightening (6.B.1); atomic envelope | — |
| `sp_rename` (table/col) | Rename | `MigrationRun.fs` (`sp_rename` + ext-prop rebind) | pages kept | refactorlog; A43 | — |
| `Drop*` | Remove | refused unless `--allow-drops` | dealloc | `WriteSignoff` `Drops`; `UndeclaredDestructiveChange` | — |
| `Insert` / bulk | Insert/Move | `Bulk.copyRows` / `copyRowsSinkMinted` | rows + CDC | `ActConsent` (transfer); signoff | schema-plane insert (seeds) not in `Act` |
| `Merge` (change-detect) | Update/Unchanged | `Deploy.executeStream` `Merge` | rows + CDC | CDC-silence; `DataVerification` | norm-measure general case ⬚ (6.F.3) |
| `Delete` (wipe / scope) | Delete | `TransferRun.fs` (`DELETE FROM`) | rows + CDC | `ActConsent.Wipe/DeleteScope`; signoff | delete-scope gate ⬚ (§12.7 P-DEL-SCOPE) |
| `IdentityInsert` | Reidentify | `SurrogateCapture` / `Bulk` | keys | `ActConsent.IdentityInsert` | — |
| `sp_cdc_enable_db/table` | (instrument) | `Faces/Canary.fs` | instance state | `--allow-cdc`; `CdcTrackedSink` gate | not a first-class `Act` |
| `CREATE`/`DROP DATABASE` | (lifecycle) | `Deploy.fs` (`createDatabase`, `reapDatabase`) | catalog | — (canary/scratch lifecycle) | **un-consented catalog move** |
| **`#` temp / `SELECT INTO #t`** | **Scratch** | `SurrogateCapture`, `KeymapSpill`, `Bulk.copyRowsSession`, `SnapshotScopeBinding` | tempdb | — | **H2 — un-modeled** |
| **`__projection_transfer_progress`** | **Scratch (durable)** | `TransferResume.fs` | user-DB table | `ADD`-guarded create | **H2 — user-DB furniture in no plan** |

The two right-hand columns are the backlog. The alphabet's *closure* is the point: adding a scratch
arm is a compiler event (a new DU case forces its manifest entry, its consent arm, its teardown, its
norm contribution) — the same forcing `ActConsent.Act`'s closure already gives the transfer plane.

---

## 4. The architecture — seven layers, each seated on an existing pattern

Hexagonal, per the supreme discipline (`DECISIONS.md` §⭐.5): the **plan is a pure value in Core**;
the **executor and openers are the adapter boundary** (`LINT-ALLOW-FILE-MUTATION`); the **analyzer**
is the compiler-service guard. No layer invents a mechanism the repo does not already bless. **No free
monad** (deferred, `DECISIONS.md` active-deferrals): the plan is the *concrete* typed statement stream
(A35) plus one interpreter — not an effect DU with a generic interpreter.

### L1 — The Plan (pure value; `Projection.Core`)

The `ExecutionPlan` reifies the transformation differential (`WAVE_6_ONTOLOGY.md` §4) as an
*immutable, channel-partitioned, norm-bearing manifest*. It already half-exists: `MigrationRun.preview`
computes the change-manifest at plan time by channel, and `Transfer.Mode.DryRun` carries the built
`DataLoadPlan`. P-PLAN gives them a common carrier:

```fsharp
// Projection.Core — proposed sketch, not landed
type MutationId       = // stable identity of one planned mutation (channel × coordinate × move)
type PlannedMutation  = { Id: MutationId; Move: Move; Channel: Channel
                          Coordinate: SsKey            // identity-first (A43), never the physical name
                          Scratch: ScratchScope option // Some for a temp/staging mutation (L5)
                          PredictedNorm: Norm }        // this mutation's contribution to ‖δ‖ (T15)
type ExecutionPlan    = private { Mutations: PlannedMutation list; Manifest: Set<MutationId> }
    // private ctor + smart ctor: the "house derive-macro" (CLAUDE.md §6). A plan is only mintable by
    // the planner; Manifest is derived, so it cannot drift from Mutations.
```

The `Manifest` is what clause 5 checks against and what `ActConsent.actsOf` reads for the consent
axis — the *same* value, two traversals (the discipline `ActConsent` already proves).

### L2 — The Gate (`Preflight.all plan`; reuse, do not reinvent)

Register P-PLAN's refusals as **new arms of the existing `Preflight.GateLabel`** — the agent survey's
explicit warning: do not invent a parallel exit scheme. Proposed additions:

- `UndeclaredScratch` → routes `*.scratch.undeclared` (a temp/staging object not in the plan's scratch
  manifest). Exit class: the argument/contract axis (like `ReconciliationMismatch`, exit 2) — it is a
  *plan* defect, caught before any write.
- `WriteOnReadOnlyConnection` → routes `*.connection.readOnlyIntent` (an attempted mutation on a
  connection opened read-only). Exit class: the destructive-failure axis (9) — the code tried to
  write where it declared it would only read.
- `NormExceeded` (post-apply) → routes `*.norm.exceeded` (`|capture| > ‖plan‖`). Exit class 9.

`Preflight.all` already short-circuits in order and is thunked; adding gates is additive. The
connection (A1) and permission (A2) gates already refuse before any write; P-PLAN adds the *scratch*
and *read-only-intent* preconditions to the same composition.

### L3 — The Sanctioned Executor + the write capability (closes H3, the closed-set clause)

Today the six write-path files each open connections and route through `Deploy.execute*` / `Bulk.copy*`.
The choke point already *nearly* exists — `Deploy` and `Bulk` are where DDL/DML and bulk funnel. P-PLAN
makes the choke point *the only door*, and gives the door a lock:

```fsharp
// Projection.Adapters.Sql (boundary) — proposed sketch
type WriteGrant = private WriteGrant of ExecutionPlan * writeConnection: SqlConnection
    // Unforgeable (private ctor). Mintable ONLY by `authorize`, which requires:
    //   • Preflight.all plan = Ok           (L2)
    //   • the two-gate rule (--go ⊕ PROJECTION_ALLOW_EXECUTE)   (existing)
    //   • ActConsent / WriteSignoff cleared for every named act (existing, two-traversal)
    // and opens the ONE write-capable connection (everything else is ReadOnly — L4).
val authorize : ExecutionPlan -> Task<Result<WriteGrant, GateRefusal>>
val apply      : WriteGrant -> Task<Receipt>
    // The single mutating entry point. Executes ONLY statements whose MutationId ∈ plan.Manifest
    // (fail-closed on a stray statement — the discriminator §2.2); brackets in the atomic/resumable
    // envelope (P-EXE is substantially shipped — MigrationRun's M21 compensating-undo by default,
    // read-back-verified as ExecutionRolledBack / PartialWriteUnrecovered, + M22's opt-in atomic BEGIN TRAN
    // envelope for local full-access; TransferResume the resumable envelope on the data plane; the general
    // atomic guarantee under managed logins stays survey-gated, 6.C.2); allocates declared scratch (L5);
    // verifies the norm (L7); returns a Receipt.
```

`apply` is the *interpreter* of A35's stream; `executeBatch`/`executeSegments`/`executeStream`/
`copyRows`/`deployDacpac` become its *private* backends, no longer public write doors. **CQS made
structural:** reads go through `ReadSide` (already the read façade); writes go through `apply` — and
the analyzer (L6) forbids any third path. The DacFx delegation is honored: for the schema channel,
`apply` hands DacFx the declarative artifact and records "DacFx will apply refactorlog then ALTERs" as
a *planned, delegated* mutation — planned-by-artifact, still in the manifest, still normed.

### L4 — Read-only by default (closes H1)

Every opener (`ConnectionResolver.openSubstrate` and the parallels in `ConnectionSpec`, `Source`,
`Deploy`, plus the CLI faces) gains a role tag. The gold-standard `SqlConnectionStringBuilder`
(already used at `Deploy.fs`) sets `ApplicationIntent = ReadOnly` for every role except the write
grant's own connection. This is defense-in-depth *below* the type layer: even if a write statement
escaped L3, the driver/replica refuses it. The CLI verb register already splits `Go` vs `ReadOnly`
(`Shell.fs`); L4 makes that split reach the connection string, not just the dispatch table.

*Caveat named:* `ApplicationIntent=ReadOnly` is honored by AG replicas and is advisory on a
stand-alone primary — so L4 is *belt*, and L3's capability + L6's analyzer are *suspenders*. The
guarantee rests on the type + analyzer; the read-only intent is the cheap physical backstop that
also documents intent at the connection.

### L5 — The Scratch Scope (temp tables as first-class planned mutations — the explicit ask)

A `ScratchScope` is a *declared, named, lifecycle-bracketed* temp allocation. Every scratch object the
run will create (`#__projection_capture`, `#projection_keymap_spill`, the session bulk-staging table,
the OSSYS read scan's `#E`/`#Ent`/`#Attr`, the durable `__projection_transfer_progress`) is listed in
the plan's scratch manifest and torn down by a bracket that *runs even on failure* (P-EXE for scratch):

```fsharp
// Projection.Core (the descriptor) + Adapters.Sql (the bracket)
type ScratchKind  = SessionTemp        // #t  — auto-dropped on connection close (preferred, cheapest)
                  | NamedStaging of SsKey   // user-DB staging — explicit DROP required
                  | DurableMarker of SsKey  // e.g. __projection_transfer_progress — planned furniture
type ScratchScope = { Name: string; Kind: ScratchKind; Norm: Norm }  // Norm = 0 for session temp
// apply allocates each scope inside `use`/try-finally so teardown is guaranteed; a leaked temp on the
// warm container is exactly the accumulation CLAUDE.md §4.2 warns about — the bracket structural-fixes it.
```

The elegant consequence: the 6.C.1 permission pre-flight, which today probes write capability with "a
no-op round-trip against a temp object" (`EXECUTION_PLAN.md` 6.C.1), becomes a **declared
`SessionTemp` scratch mutation** — even the *safety check's own write* is planned. Nothing the engine
does to a database is outside the plan, including the way it checks whether it is allowed to write.

Policy: prefer `SessionTemp` (norm 0, self-cleaning); `NamedStaging`/`DurableMarker` only when a
declared scope demands it, always with an explicit teardown or an `ADD`-guarded idempotent create.

### L6 — The Analyzer (structural closure; the sibling of `NoUnsafeTimeInCoreAnalyzer`)

The clock-in-Core ban is the exact template (`Projection.Analyzers/NoUnsafeTimeInCoreAnalyzer.fs`,
`PRJ001`): an `FSharp.Analyzers.SDK` analyzer that walks the **typed** tree, bans **by resolved full
name**, is **path-scoped**, and **allowlists** named exceptions. The sibling — `PRJ00x`,
`NoAmbientWriteAnalyzer`:

- **Bans by full name**, outside the sanctioned executor module: `Microsoft.Data.SqlClient.SqlCommand.Execute*`,
  `SqlConnection.Open*`, `SqlBulkCopy` (ctor + `WriteToServer*`), and `DacServices.Deploy`.
- **Path-scope + allowlist:** permitted *only* in the executor seam (`Adapters.Sql` `apply`/`authorize`
  and its private `Deploy`/`Bulk` backends) and the connection openers. Every other file — including a
  future new write site — is a **compile error**.
- **Scratch clause:** a `SELECT … INTO #…` / `CREATE TABLE #…` node outside a `ScratchScope`
  allocation is `PRJ00x` too.

This is what turns clauses 5–6 from discipline into structure: the ambient-authority hole
(`SqlConnection` + stream = a write, A35) cannot be reopened without the compiler objecting. Pair it
with the existing test-based audit (CLAUDE.md §5: purity is "analyzer + audit") for the cases the
analyzer's full-name resolution cannot reach.

### L7 — The Measure (the observability leg; `|capture| = ‖plan‖`)

After `apply`, the executor reads `cdc.<table>_CT` (the `cdcCaptureCount` read side already exists,
`ReadSide.fs`; `sp_cdc_scan` forces a pass, `Deploy.fs`) and asserts, per channel, that the realized
capture count equals the plan's `PredictedNorm` (T15 isometry). Excess ⇒ `NormExceeded` (L2), fail-loud.
This is what makes P-PLAN a *theorem with a ruler*: an unplanned write that slipped every prior layer
still shows up as a norm violation the operator sees. The `‖δ‖ = 0` case is the shipped CDC-silence
canary; the general `‖δ‖ = k` case is `EXECUTION_PLAN.md` 6.F.3-data (a), on which this rides.

---

## 5. The discriminating predicates (the L3-layer witnesses; §8 method)

Each predicate is the *adversarial* witness — the input where a plausibly-named-but-wrong
implementation diverges — not a restatement of the name. This is the test surface the slices land.

| Predicate | The law | Why the happy path is insufficient | Witness / Trigger |
|---|---|---|---|
| **P-PLAN-CLOSED** | a mutation not in `plan.Manifest` never reaches the wire | "the planned writes happened" is blind to an *extra* write | inject a stray `INSERT` into an `Execute` run → executor fail-closes (id ∉ manifest) **and** L7 norm excess; a new raw write site → `PRJ00x` compile error |
| **P-SCRATCH-BRACKET** | a declared scratch object is torn down even on failure, and an undeclared one is refused | "the temp was created and used" ignores the orphan a mid-run crash leaves | force a failure between allocate and drop → tempdb has no `#__projection_*` orphan; a `SELECT INTO #t` outside a scope → `UndeclaredScratch` refusal |
| **P-RO-DEFAULT** | a read-role connection cannot mutate | "the read verb read correctly" never tries to write | attempt a DML on a read-role connection → driver/gate refuses (`WriteOnReadOnlyConnection`); the OSSYS scan's `#E` temp is a *declared* `SessionTemp`, not an ambient write |
| **P-NORM-MATCH** | `|capture| = ‖plan‖` per channel | "data round-trips" passes on a full reload capturing `2·|table|` | idempotent redeploy captures 0 (silence, shipped); a `k`-row delta captures exactly `k` (6.F.3); an injected extra write trips `NormExceeded` |
| **P-CONSENT-SPAN** | every named destructive/creative act across *all* seams is consented, not just the transfer plane | `ActConsent` green on the transfer plane says nothing about a schema-seed insert or a catalog `DROP DATABASE` | extend `actsOf` to the schema + scratch + catalog seams; a `DROP DATABASE` with no blessing → `ConsentWithheld` |

---

## 6. The buildable route (slices; sequenced; acceptance witnesses)

Numbered to slot under `EXECUTION_PLAN.md` as **6.J — Planned Mutation** (a spanning-axis sub-wave,
sibling to 6.C's pre-flights and 6.F's publication). Each slice is pure-first, ships its witness, and
closes one clause. Sequencing respects the ontology's own rule — **the structural closure (S3/S5) must
not ship before the plan value (S1) it enforces exists**, exactly as live `migrate --execute` sat
behind its pre-flights.

- **S1 — `ExecutionPlan` + manifest + norm (Core, pure).** Reify the plan value carrying the
  channel-partitioned `PlannedMutation` list, the derived `Manifest`, the scratch scopes, and the
  `PredictedNorm`. Fold `MigrationRun.preview` + `DataLoadPlan` into it (no new emission — one carrier
  for the two that exist). *Acceptance:* `` ``plan: the manifest is derived from the mutation list and cannot be set independently (no-drift)`` ``. **~M.**
- **S2 — The `WriteGrant` capability + `authorize` (boundary).** Private ctor; mint only past
  `Preflight.all` ⊕ two-gate ⊕ consent. Wire the *existing* gates in; add no new door. *Acceptance:*
  `` ``authorize: a plan that fails any pre-flight yields no WriteGrant (the write path is unreachable)`` ``. **~M.**
- **S3 — The sanctioned executor `apply` + fail-closed manifest check.** Fold `Deploy.execute*` /
  `Bulk.copy*` / `deployDacpac` / `MigrationRun` writes behind `apply grant`; reject a statement whose
  id ∉ manifest. *Acceptance (the P-PLAN-CLOSED discriminator):* `` ``apply: a statement absent from the plan manifest is refused, not executed (fail-closed)`` ``. **~M.**
- **S4 — `ScratchScope` + read-only-default (closes H1/H2).** Declare every temp/staging/marker object
  in the plan; bracket teardown; tag openers with roles; set `ApplicationIntent=ReadOnly` for read
  roles; convert the 6.C.1 temp-probe and the OSSYS `#E/#Ent/#Attr` scan to declared scopes.
  *Acceptance:* `` ``scratch: a forced mid-run failure leaves no temp orphan; an undeclared temp is refused`` `` + `` ``read-role connection: a DML attempt is refused before it reaches the wire`` ``. **~L.**
- **S5 — `NoAmbientWriteAnalyzer` (`PRJ00x`) (closes H3).** The `NoUnsafeTime` sibling: ban
  `SqlCommand.Execute*` / `SqlConnection.Open*` / `SqlBulkCopy` / `DacServices.Deploy` and undeclared
  `#`-temp outside the executor+opener allowlist. *Acceptance:* `` ``analyzer: a raw ExecuteNonQuery outside the sanctioned executor is a PRJ00x error`` ``. **~M.** *This is the slice that makes the guarantee structural — consider shipping it early as a fence even before S1–S4 fully fold, scoped to "no new write door."*
- **S6 — The norm measure (L7; P-DM general case).** Post-apply `|capture| = ‖plan‖` per channel,
  building on the shipped CDC-silence floor + 6.F.3. *Acceptance:* `` ``norm: applying the plan captures exactly ‖plan‖ rows; an injected extra write trips NormExceeded`` ``. **~M.**
- **S7 — `actsOf` spanning (P-CONSENT-SPAN).** Extend the consent derivation from the transfer plane to
  the schema-seed, catalog (`DROP DATABASE`, `sp_cdc_enable`), and scratch seams so the two-traversal
  covers every named act. *Acceptance:* `` ``consent: a DROP DATABASE with no blessing is ConsentWithheld, same as an unblessed wipe`` ``. **~M.**

**What lands alongside the code:** an `AXIOMS.md` entry for **P-PLAN** relating it to T15/T16/T14/A43
and P-GATE/P-EXE, with its `AxiomTests.fs` discriminating witnesses (every AXIOMS change ships its
witness in the same commit — CLAUDE.md §5); a `DECISIONS.md` entry recording the closure discipline
and the three decisions owed (§7); and a `matrix-status.sh` axis so the guarantee self-reports its rung
(L1 witness → L2 all-seams → L3 in the green apply canary), the way every other axis does.

---

## 7. Decisions owed (resolve before the dependent slice opens)

Named with their gate, per the ontology's discipline (a plan that hides its forks is not a plan):

1. **Read-only-default rollout (gates S4).** Do read-role connections get `ApplicationIntent=ReadOnly`
   *universally* (every opener, one flag), or *per-role* (only the seams proven read-only, leaving the
   ambiguous ones RW under the analyzer's watch)? Universal is stronger but risks a read seam that
   *does* stage a temp (the OSSYS scan) failing until its scope is declared. **Recommendation:**
   per-role first (S4 declares the scan's scope, then flips it), universal once every read seam's
   scratch is declared. *This is a real fork — the OSSYS read path mutates tempdb today.*
2. **Is `__projection_transfer_progress` "planned infrastructure" or a consented act? (gates S7.)** It
   is a durable user-DB table the resumable envelope needs. Is its create a `DurableMarker` scratch
   scope (planned, norm-0, no per-run consent), or a first-class `Act` requiring a blessing? **Lean:**
   `DurableMarker` — it is engine furniture, not an operator-facing data change — but name it so the
   consent surface does not surprise the operator.
3. **Analyzer scope: fence-only or full-fold? (gates S5 timing.)** Ship `PRJ00x` early as a *fence*
   ("no *new* write door outside the current six files"), or only after S3 folds the six behind `apply`
   (so the allowlist is one module, not six)? **Lean:** fence early (it is cheap and stops the hole
   widening), tighten the allowlist as S3 folds.

---

## 8. What this reuses — and explicitly does not reinvent

The agent survey's warning, honored: register in the existing machines; do not build parallel ones.

- **Refusals** register in `Preflight.GateLabel` + `classify` — *not* a new exit scheme.
- **Consent** extends `ActConsent.actsOf`'s two-traversal — *not* a new blessing surface.
- **The plan** folds `MigrationRun.preview` + `DataLoadPlan` — *not* a third emission.
- **Authorization** reuses the two-gate `--go ⊕ PROJECTION_ALLOW_EXECUTE` + `MovementSpec.isLiveWrite`
  — *not* a new env var.
- **The analyzer** is the `NoUnsafeTimeInCoreAnalyzer` template — *not* a new tool.
- **The measure** is T15 / the CDC-silence canary generalized — *not* a new ruler.
- **The pattern vocabulary** is the house set: private-constructor module (the capability), typed
  statement stream + one interpreter (A35 — *not* a free monad, which stays deferred), CQS via
  `ReadSide` / `apply`, hexagonal Core→Adapter direction.

P-PLAN is therefore not new machinery; it is the **spine** that makes the machinery the engine already
has into a single, structural, measurable guarantee — the vigilance the per-plane gates practice,
turned into a property of the types and the compiler. Hold the spine.

---

*— Drafted for the receiving agent. Open a slice by reading its clause (§2), its move
(`WAVE_6_ONTOLOGY.md` §5/§12.3), its discriminating predicate (§5), and the layer it seats on (§4) —
then the route (§6). The four are one chain.*
