# Database Archetypes — the target's capability class as a first-class config disposition

> **What this is.** The codebase's assumptions about the *class* of database it writes to, made
> explicit — and a proposal to lift that class from a scattered set of implicit/hardcoded behaviors
> into **one named, reusable configuration disposition** that programmatically drives how the
> pipeline may safely interact with each target. It is the conceptual companion to
> `REVERSE_LEG_OPERATOR_PROBE_SHEET.md` Part E (the probes that *verify* a target's archetype) and an
> enrichment of the existing `Grant` / `Rendition` config facets (`MovementSurface.fs:33,48`).
>
> **Dated 2026-06-15.** Direction recorded in `DECISIONS.md` (2026-06-15 — "Database archetype as a
> config disposition"). This is a design + assumptions surface; the F# slice is sequenced in §6.

---

## §0 — The problem: there are (at least) two classes of target, and the engine already branches on them

The migration touches **two databases that are not the same kind of thing**:

1. **The on-prem SQL Server** — the database the migration team owns. It receives the **emitted SSDT
   schema** (`CREATE TABLE` / `ALTER` — full DDL) and hosts a database carrying **the same schema +
   the migrated data** (the `Logical` / **B** rendition). It is the schema home and the verification
   home. We **likely** have `CREATE TABLE` here (operator to verify — probe sheet Part E); the access
   is *similar to but not identical to* the cloud's.
2. **The managed cloud SQL** — the platform-managed production target the reverse leg loads into. The
   J5 capability spike settled its profile: a **DML-only** managed login — `SELECT/INSERT/UPDATE/
   DELETE` permitted; **no `ALTER`, no `IDENTITY_INSERT`, no `CREATE TABLE`** (the `Physical` / **A**
   rendition).

These two classes have **materially different capability profiles**, and the engine *already* behaves
differently for them — but the difference lives in three uneven places:

- a **coarse binary facet** — `Grant = SchemaAndData | DataOnly` (`MovementSurface.fs:33`), which the
  `CapabilitySurvey` reconciles against probed `fn_my_permissions` evidence;
- a **metadata facet** — `Rendition = Physical | Logical` (`MovementSurface.fs:48`), which only marks
  which rendition a place bears;
- and a layer of **hardcoded, cloud-shaped assumptions** baked into the engine because the
  estate-scale path was built against the managed-DML sink first (see §3).

The coarse `Grant` covers "may schema change vs data-only." It does **not** capture the finer
dispositions the engine genuinely forks on — whether the sink can preserve source keys, where resume
state can live, whether constraints can be bypassed, how a rollback is proven. Those are decided
*implicitly* today. The proposal: name the **archetype**, let it **expand** to the full disposition
bundle, and **verify** it against probed evidence (A44 — expressible ⇔ reachable).

---

## §1 — The archetype catalog (the capability profile per class)

| Capability / disposition | **FullRights** (on-prem, the schema+data home) | **ManagedDml** (cloud, the J5 profile) |
|---|---|---|
| DDL — `CREATE TABLE` / `ALTER` | **yes** | **no** |
| `IDENTITY_INSERT` | **yes** (verify: probe E3) | **no** (denied, error 1088) |
| **Default identity disposition** | **PreservedFromSource** — write the source key directly | **AssignedBySink** — sink mints, capture + remap |
| FK re-keying required? | **no** (keys preserved ⇒ FK values stay valid) | **yes** (every FK to a minted kind re-pointed) |
| Schema deploy (SSDT) | **yes** — `migrate-with-data` (DDL + data) | **no** — data-only `transfer` |
| **Resume checkpoint** | **sink-resident progress table** (needs `CREATE TABLE`) | **client-side NDJSON journal** (no DDL ⇒ off-box) |
| Staging | persistent server-side staging tables | `#` temp tables only |
| Constraint / trigger bypass | `NOCHECK` / `DISABLE TRIGGER` (needs `ALTER`) | none — the capture-ladder descent handles triggers |
| Wipe / refresh | `TRUNCATE` (fast; needs `ALTER`) | child-first `DELETE` (slower; the only DML-legal path) |
| Rollback channel | SQL (full — `DELETE` / `TRUNCATE` / transaction) | SQL `DELETE` + transaction (J5-proven) |
| **DMV read** (`VIEW DATABASE PERFORMANCE STATE`) | *expected* yet **independently grantable** — a least-privilege login can lack it even with full DDL/DML (observed on-prem, 2026-06-15) | not required (source counts probed via plain `SELECT`/aggregate) |
| User handling | preserve, or reconcile by business key | `ReconciledByRule` (the directory pre-exists; never re-imported) |
| Derived `Grant` facet | `SchemaAndData` | `DataOnly` |
| Typical `Rendition` | `Logical` (B) | `Physical` (A) |

The two columns are not a binary the existing `Grant` already encodes — they are a **bundle of
covarying dispositions** that the archetype names once. `FullRights` is not merely "schema+data
grant"; it is "the engine may preserve keys, checkpoint sink-side, bypass constraints, and truncate."
`ManagedDml` is the J5 verdict made reusable: "sink mints, journal off-box, descend the capture
ladder, delete to roll back."

> **Extensibility.** The catalog is a closed set today (two members) but is built to grow: a future
> target class (a read-replica, a different managed platform, a no-trigger bulk-staging instance)
> joins as a new archetype with its own profile, and every consumer that is *total over the archetype*
> (the renderer, the disposition selector, the gate set) gets the new case by construction — the
> closed-DU / `registered ⇔ executed` discipline (`CONSTELLATION.md` §9.8.9), applied to capability.

---

## §2 — What the archetype drives (the dispositions it should programmatically set)

Declaring an archetype on an environment should **expand** to these engine decisions, so the operator
states intent **once** and the pipeline derives safe interaction:

1. **The identity disposition default.** `FullRights ⇒ PreservedFromSource` (write source keys; no
   capture, no remap, no FK re-point — dramatically simpler and faster). `ManagedDml ⇒ AssignedBySink`
   (the J5 path). *(Per-table structural overrides still apply — a `ReconciledByRule` user table, a
   composite-key refusal — but the archetype sets the default the structural classifier starts from.)*
2. **The realization + resume mechanism.** `FullRights ⇒` a **sink-resident progress table** becomes
   available (a durable, queryable, mid-run checkpoint with no filename-digest coupling — the Phase-3
   address-drift and compaction concerns evaporate). `ManagedDml ⇒` the client-side NDJSON journal
   (the only option when `CREATE TABLE` is denied).
3. **The schema-deploy lane.** `FullRights ⇒ migrate-with-data` (emit + apply DDL, then load).
   `ManagedDml ⇒ transfer` (data-only; the schema is frozen).
4. **The constraint-handling strategy.** `FullRights ⇒` optionally `NOCHECK` constraints + disable
   triggers for a fast straight load. `ManagedDml ⇒` the capture-ladder descent (the only path under a
   no-`ALTER` grant).
5. **The wipe strategy.** `FullRights ⇒ TRUNCATE` (the cheap refresh). `ManagedDml ⇒` child-first
   `DELETE` (the `2·|rows|` CDC-costed path).
6. **The pre-write gate set.** The archetype declares the *expected* grant, so the `CapabilitySurvey`
   reconciles against it (§5); a `FullRights` target missing `CREATE TABLE`, or a `ManagedDml` target
   that *unexpectedly* permits `IDENTITY_INSERT`, is a **named declared-vs-actual mismatch**, not a
   silent surprise mid-load.
7. **The user-handling default.** `ManagedDml ⇒ ReconciledByRule` (the cloud owns its users — the
   Phase-2 reverse-leg re-key). `FullRights ⇒` preserve (same key space) unless reconcile is declared.

---

## §3 — The assumptions we make today (honest inventory: implicit / hardcoded → should be archetype-driven)

The estate-scale reverse-leg path was built against the **managed-DML** sink first, so several
ManagedDml assumptions are currently **baked in** rather than chosen by archetype. Naming them is half
the value of this document — each is a place the engine *assumes* the cloud profile and would behave
sub-optimally (or refuse unnecessarily) against an on-prem `FullRights` target:

| # | Current assumption (where) | True for | What the archetype unlocks for `FullRights` |
|---|---|---|---|
| H1 | The reverse-leg sink is `AssignedBySink` (the J5 default; `TransferRun.fs` reverse-leg wiring) | ManagedDml | `PreservedFromSource` — write source keys directly; **no capture/remap/FK-repoint at all** (a different, simpler engine path that the IDENTITY_INSERT grant makes correct) |
| H2 | Resume state is a **client-side NDJSON journal** *because the DML-only grant forbids the `CREATE TABLE` a sink-resident progress table would need* (`CaptureJournal.fs` docstring) | ManagedDml | a **sink-resident progress table** — durable, queryable mid-run, no filename↔digest coupling (the Phase-3 hazards do not exist on this archetype) |
| H3 | Staging is a session `#` temp table cloned from the sink (`SurrogateCapture.fs`) | ManagedDml | persistent server-side staging (re-usable across chunks; spill-friendly) |
| H4 | Constraints/triggers are handled only by the capture-ladder **descent** (no bypass; `SurrogateCapture.fs:21-46`) | ManagedDml | `NOCHECK` / disable-trigger fast lane (the `ALTER` grant the cloud lacks) |
| H5 | Wipe is child-first `DELETE` (`wipeFkOrdered`) | ManagedDml | `TRUNCATE` (the `ALTER`-gated fast refresh) |
| H6 | The grant gate proves `INSERT`/`DELETE` coverage (data load); schema rights are a separate flow | both (correctly, via `Grant`) | the archetype makes the *expected* full set declarable + verified in one place |

> None of these are bugs — each is the **correct** behavior for the ManagedDml class the path was
> proven against. The point is that they are **class-specific**, currently chosen implicitly, and an
> explicit archetype lets the engine pick the right one per target instead of assuming the cloud shape
> everywhere. The on-prem `FullRights` strategies (H1–H5 right column) are *foreclosed today* by the
> cloud-shaped defaults; the archetype is what re-opens them.

---

## §4 — The proposal: `Archetype` as a first-class config facet

Add an **`Archetype`** facet to `Environment` (sibling to `Grant` / `Rendition`,
`MovementSurface.fs:55`), declared once per place in `projection.json`, that **expands** to a
`CapabilityProfile` the pipeline reads instead of re-deciding per site:

```fsharp
/// The capability CLASS of a target — the bundle of covarying dispositions the
/// engine forks on (§1/§2). Closed so every consumer (the disposition selector,
/// the resume-mechanism chooser, the gate set, the renderer) is TOTAL over it —
/// a new target class joins by construction, never by a parallel hand-list.
[<RequireQualifiedAccess>]
type Archetype =
    | FullRights      // on-prem, DDL+DML+IDENTITY_INSERT: the schema+data home
    | ManagedDml      // a managed DML-only sink (the J5 profile): sink-mints, no DDL

/// What an archetype EXPANDS to — the disposition defaults the pipeline consumes.
/// Pure derivation; `of` is total over `Archetype`.
type CapabilityProfile =
    { Grant               : Grant                  // derived facet (subsumes the coarse one)
      IdentityDefault     : IdentityDisposition    // PreservedFromSource | AssignedBySink
      DdlPermitted        : bool                    // schema deploy + sink-resident progress
      IdentityInsert      : bool                    // PreservedFromSource viability
      ConstraintBypass    : bool                    // NOCHECK / disable-trigger
      ResumeCheckpoint    : ResumeKind              // SinkResidentTable | ClientJournal
      WipeStrategy        : WipeKind }              // Truncate | ChildFirstDelete
```

Design commitments:

- **The archetype subsumes `Grant`.** `Grant` becomes a *derived* projection of the archetype
  (`FullRights → SchemaAndData`, `ManagedDml → DataOnly`), so the existing `CapabilitySurvey` /
  `requiredFor` machinery keeps working unchanged — it now reads `archetype.Grant` instead of a hand-set
  facet. (Migration §6 keeps `Grant`-only configs valid by *inferring* the archetype from the grant.)
- **`Rendition` stays orthogonal** (it is *which rendition*, not *what class*) but the two correlate in
  practice (on-prem=Logical, cloud=Physical); the archetype does not narrow it.
- **One definition site, total consumers.** `CapabilityProfile.of : Archetype -> CapabilityProfile` is
  the single expansion; the disposition selector, the resume chooser, and the gate set read the profile
  — none re-derive "is this DML-only?" from scattered checks. This is the `ArtifactByKind` /
  `chainSteps` discipline (one definition site, structural totality) applied to target capability.
- **Reusable.** An operator declares `"archetype": "FullRights"` once on the on-prem place and
  `"archetype": "ManagedDml"` on the cloud place; every flow touching them inherits the safe-interaction
  profile. No per-flow capability flags, no per-site re-decision.

---

## §5 — Verify, don't trust: the archetype is a *declared assumption* the survey confirms (A44)

The archetype is the operator's **declaration** of a target's class. The engine must not *trust* it
blindly — it must **reconcile it against probed evidence**, exactly as the `CapabilitySurvey` already
reconciles the declared `Grant` against `fn_my_permissions` (`required ⇔ surveyed`). This is the J5
covenant generalized from a one-time spike into a standing, per-class gate:

- A declared **`FullRights`** target whose live grant **lacks `CREATE TABLE` or `IDENTITY_INSERT`**
  (probe sheet E1/E3) is a **named declared-vs-actual mismatch** — refuse before any write, because the
  engine would have chosen `PreservedFromSource` + a sink-resident checkpoint that the real grant cannot
  support.
- A declared **`ManagedDml`** target that **unexpectedly permits `IDENTITY_INSERT`** is *also* a
  surfaced mismatch (a safer-than-declared surprise, but still a divergence the operator should see —
  it means `PreservedFromSource` is *available* and the simpler path could be chosen).
- An **unverifiable** grant (least-privilege denies `fn_my_permissions`) stays **blocking** (the
  existing `GrantUnreadable` / NM-55 posture) — an unprobed archetype claim is *unverified*, not
  *confirmed*.
- **Capabilities are independent facets, not a bundle (observed 2026-06-15).** The on-prem target's
  real grant was full DDL/DML **minus `VIEW DATABASE PERFORMANCE STATE`** — so it is a `FullRights`
  archetype with one capability absent (DMV-read), degrading the DMV-based row-count/keymap-sizing
  probes to a `COUNT_BIG` scan (`REVERSE_LEG_OPERATOR_PROBE_SHEET.md` B1) without changing the
  identity/schema/resume forks. This is exactly why the `CapabilityProfile` must carry each capability
  as its **own** verified flag, not infer them from a single archetype label — a real estate hands you
  a "FullRights-minus-DMV" target, and the engine must degrade *that one probe* gracefully, not
  mis-declare the whole class.

This makes the archetype **expressible ⇔ reachable** (A44): what the operator can declare, the survey
can prove (or refuse). The J5 ledger becomes the *seed profile* for `ManagedDml` (the verified
verdicts: no ALTER, no IDENTITY_INSERT, AssignedBySink, SQL rollback); the probe sheet Part E is the
*seed verification* for `FullRights`. The disposition decision (the archetype) holds across instances
*of the same class*, so verifying it once de-risks every same-class cutover — the original J5 thesis,
now structural.

---

## §6 — Migration path (non-breaking slices)

1. **Slice A — the type + the expansion, derived from `Grant` (no behavior change).** Add `Archetype` +
   `CapabilityProfile.of`; add the optional `Environment.Archetype` field (parsed from `projection.json`,
   defaulting to **inferred from the existing `Grant`** — `SchemaAndData → FullRights`,
   `DataOnly → ManagedDml`, `None → None`). Existing configs are byte-identical: nothing reads the
   profile yet. Property test: `(infer ∘ derive-grant)` round-trips.
2. **Slice B — route the survey through the profile.** `CapabilitySurvey.requiredOf` reads
   `archetype.Grant` instead of the raw facet (identical results — the derivation is exact); add the
   declared-vs-probed **archetype** reconciliation (E1/E3 capabilities), surfaced as the new named
   mismatch. Witness: a `FullRights`-declared sink with no `CREATE TABLE` grant refuses by name.
3. **Slice C — the resume-mechanism fork (the highest-value unlock).** On a `FullRights` archetype,
   offer the **sink-resident progress table** as the resume checkpoint (gated on the verified
   `CREATE TABLE` grant); `ManagedDml` keeps the client journal. This is where the archetype first
   *changes* engine behavior — and it retires the Phase-3 address-drift/compaction hazards on the
   on-prem class.
4. **Slice D — the identity-disposition fork.** On `FullRights`, default to `PreservedFromSource`
   (IDENTITY_INSERT verified) — the *simpler* load (no capture/remap/FK-repoint). This is a large,
   high-value path that the on-prem archetype makes correct.
5. **Slices E+ — constraint bypass / TRUNCATE wipe**, each gated on the verified `ALTER` grant.

Each slice is independently shippable and witnessable; none regresses the ManagedDml path (its profile
is byte-identical to today's hardcoded behavior).

---

## §7 — What it unlocks (the one-line summary)

> The operator declares a target's **class once**; the pipeline derives — and the survey **verifies** —
> how it may safely be touched. The cloud path stays exactly as J5 proved it; the on-prem path stops
> being forced into cloud-shaped assumptions (off-box journals, sink-minting, descent-only constraint
> handling) and gains the strategies its full grant actually permits (sink-resident resume, key
> preservation, truncate-refresh). The capability class becomes a **reusable, verified, single-source
> disposition** instead of a scatter of implicit branches — and a new target class joins by adding one
> closed-DU case, total-over-the-engine by construction.

*See `REVERSE_LEG_OPERATOR_PROBE_SHEET.md` Part E for the operator probes that verify a target's
archetype, and `DECISIONS.md` (2026-06-15 — "Database archetype as a config disposition") for the
recorded direction.*
