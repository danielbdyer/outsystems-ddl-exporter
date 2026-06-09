# Handoff — the Capability Survey: scope + a working prototype

*A full-throated handoff. The vision, a shipped prototype you can run today, and
the chapter that grows it to its full reach. Written to you, the agent who takes
it the rest of the way.*

---

## The vision, in the operator's words

> *"Is there a way to live-probe profile the entirety of the connected
> environments in a completely paralleled way to the permission-set connection
> profile of the actors? All the activities required by the use cases in the
> pipeline — we preflight those."*

The instrument already proves the database means what the model says. The
**capability survey** proves the other half: that every *place* the pipeline
touches is actually able to do what the pipeline will ask of it — **before** a
single live run. It is the dual of the per-actor permission profile the
command/verb hardening defined: each environment **declares** a `grant` facet in
`projection.json` (`schema+data` / `data`); the survey **probes** each one's
*actual* grant (`sys.fn_my_permissions`) and reconciles the two, across the whole
estate, all at once. A misconfigured `cloud-uat` — declares `schema+data` but the
managed login is silently missing `ALTER` — is caught at survey time, not mid-run.

## What is shipped (the prototype — run it today)

```
projection survey
```
reads `projection.json`'s environments, probes every connected one **in
parallel**, and renders the declared-vs-actual capability matrix:

```
  ▲  2 environment(s) need attention before a live run.

     cloud-uat   ✓ reachable · grant covered · CDC-tracked
     onprem-dev    no live gate (file or ephemeral)
     prod        ✕ unreachable
     staging     ▲ reachable · missing ALTER, CREATE TABLE
```

Exit 0 when every connected place can do what is asked; exit 7 (the gate class)
when one cannot — so CI gates on it. The pieces, all green:

- **`src/Projection.Pipeline/CapabilitySurvey.fs`** — the core:
  - `requiredFor : Grant -> Set<WriteAction>` — the required-capability catalog
    (the MVP, derived from the declared facet: `schema+data` ⇒
    `{Insert; Delete; Alter; CreateTable}`, `data` ⇒ `{Insert; Delete}`).
  - `reconcile : Grant -> GrantEvidence -> WriteAction list` — **pure**: the
    activities the declared grant promises that the live grant does not cover at
    database scope. The reconciliation core, witnessed by `CapabilitySurveyTests`.
  - `survey : ProjectionConfig -> Task<EnvironmentReport list>` — probes **every**
    environment concurrently (`Task.WhenAll` over the substrates); each probe is
    reachability (the open) + `Preflight.captureGrantEvidence` (the grant) +
    `ReadSide.cdcTrackedTables` (CDC). A `Bundle`/`Docker` place has no live
    address → reported not-connected, never an error.
- **`TtyRenderer.buildSurveyView`** — the voiced matrix (pure; `ViewTests`): a §3
  verdict leads, each place reads plainly (covered / missing the named activities
  / unreachable / no live gate).
- **`Preflight.coversAtDatabaseScope`** — a small public helper exposing the
  database-scope grant check (reuses the private `permissionName`).
- **`runSurvey` + the `survey` verb** in `Program.fs`.

It reuses the **verified** pre-flight probes — nothing new touches SQL that the
migrate/transfer paths didn't already prove. Verified live: a one-environment
config pointed at the warm container surveyed `reachable · grant covered`.

## The model (so you trust the shape)

The estate is already typed for this (`MovementSurface.fs`):
`Environment = { Name; Access (Bundle | Direct ref | Docker); Grant (SchemaAndData
| DataOnly) option }`. The survey is the function
`ProjectionConfig → (declared grant ⟂ actual grant) per place, in parallel`. The
activity vocabulary is `Preflight.WriteAction` (`Insert | Delete | Alter |
CreateTable`); the actual profile is `Preflight.GrantEvidence`
(`Set<(object, perm)>`, database-wide grants keyed `("", perm)`); the gate is
`Preflight.permissionViolations` / the new `coversAtDatabaseScope`. The prototype
is the smallest honest slice over all of it.

## Read-only by construction (the load-bearing posture)

**The survey reads; it never writes — and that is not a limitation, it is the
point.** A survey you can run against PROD without touching it is worth far more
than one that mutates to find out. The whole existing pre-flight suite already
made this choice, and the survey inherits it.

**What is fully profilable read-only (no write, ever):**

| Question | Read-only probe |
|---|---|
| Reachable? / CONNECT? / login valid? | the connection open (not a write) |
| Permissions — does the login hold INSERT/DELETE/ALTER/CREATE? | `sys.fn_my_permissions(NULL,'DATABASE')` / `(obj,'OBJECT')` — SQL Server computes the **effective** set (grants − denies, roles, ownership chains). The permission oracle. |
| CDC tracked? | `sys.tables.is_tracked_by_cdc` / `sys.databases.is_cdc_enabled` |
| DB writeable, or read-only mode? | `DATABASEPROPERTYEX(db,'Updateability')` |
| Space / filegroup / DB state | `sys.database_files` / `sys.databases.state_desc` |
| Will a narrowing/tightening succeed against existing data? | a **`COUNT`** — and the codebase already does exactly this (`Preflight.tighteningPreflight` SELECTs the NULL count; it never attempts the `ALTER` to find out) |

So the *permission and readiness profile* — everything the survey needs — is
catalog + DMV + `fn_my_permissions` reads. `captureGrantEvidence`,
`connectionPreflight`, `cdcTrackedTables`, `tighteningPreflight` are **all**
read-only today. The only write in the pipeline is the actual run, gated behind
`--execute` + `PROJECTION_ALLOW_EXECUTE`.

**The one thing read-only cannot certify** is the *execution of a specific write*
under runtime/data-dependent conditions a permission check can't see — a trigger
or row-level-security policy that rejects the row, a schema-bound dependency, a
transient lock. `fn_my_permissions` says the permission is held; the runtime might
still refuse. The only 100%-certain test there is the operation itself.

**The "write test" idiom, and why it is not truly free.** The non-destructive
form is the transaction-rollback probe (`BEGIN TRAN … <write> … ROLLBACK`) — it
persists no data. **But it is not side-effect-free:** it takes locks, it fires
triggers, and crucially **IDENTITY/SEQUENCE values do not roll back** — the seed
advances past the rollback. That perturbs exactly the thing the engine is most
careful to conserve (A43 — identity is the conserved charge). So even the "safe"
write test mutates the target's identity counters.

**The design rule, therefore:** the survey is read-only, full stop. A
write-test "deep probe" (transaction-rolled-back) is an **explicit, opt-in** mode
for the rare case where an operator needs runtime-conditional certainty — never
the default, and it ships with the identity-seed caveat in its own copy.
Read-only profiling of all the *necessary* information is not only achievable, it
is the correct posture; a write test answers a *different* question ("will this
exact write succeed right now") and costs a side effect to ask.

## What this prototype does NOT yet do (the chapter ahead)

The prototype reconciles the **coarse** declared facet against the **database-scope**
grant. The full reach the vision points at:

1. **The per-use-case obligations matrix, reified.** `requiredFor` is derived
   from the coarse `grant` facet today. The real catalog is finer: each protein
   (`THE_USE_CASE_ONTOLOGY.obligations.md`'s G0/G2/G6 rows — the Add→Publish→
   Insert→Measure→Verify→Record chains) requires specific activities of specific
   *roles* (Source read vs Sink write). Reify it as
   `requiredBy : UseCase -> SubstrateRole -> Set<Capability>` and survey against
   the **union** of what the configured *flows* actually exercise — "preflight all
   the activities the use cases require," exactly. This subsumes the coarse facet.
2. **Object/schema scope, not just database.** `captureGrantEvidence` probes
   `sys.fn_my_permissions(NULL, 'DATABASE')` today (the OPEN-2 / P1 survey-gate).
   Object-scope grants (`ALTER on dbo.Order`) are the refinement the scaffold is
   explicitly waiting on — and the survey is where the real permission vocabulary
   gets confirmed against a managed login.
3. **Make it the mandatory G0 pre-flight.** `Preflight.all` is the mandatory
   composition with *zero callers* (the obligations doc's master gap). The survey
   is the natural home: a run's pre-flight is "survey the two places this flow
   touches" — wire it into MC/MX/TR so no live run proceeds against an
   under-capable place.
4. **More axes.** CDC is reported; reachability is binary. The richer survey adds:
   the grant-probe failure mode (vs unreachable), the connection latency, the
   declared `access` vs actual (a `direct` place that won't open), the user-map
   completeness for a re-key flow. Each is one more parallel probe + one more
   matrix column.
5. **The actor-as-login distinction.** Today "the actor" is the login on the
   connection. If a flow runs as a *different* principal than the survey, the
   `EXECUTE AS` / impersonation axis surfaces — name it when a consumer needs it.

## The slice plan (sequenced by leverage)

- **S1 — the prototype** *(this handoff; shipped, green)*: parallel probe +
  declared-vs-actual reconciliation + the matrix + the `survey` verb.
- **S2 — flow-driven required capabilities**: reify `requiredBy` from the
  configured flows (which roles each flow exercises, which activities each
  needs); survey against the real union, not the coarse facet. *Pure core +
  ontology reification; no new probe.*
- **S3 — survey as the mandatory pre-flight (G0)**: wire the two-place survey into
  the run verbs' pre-flight, replacing the ad-hoc per-leg `connectionPreflight`/
  `permissionPreflight` with the unified survey. *Closes the master gap.*
- **S4 — object-scope grants + the permission-vocabulary survey** (OPEN-2 / P1):
  probe object/schema scope; confirm the managed-login vocabulary. *Needs a real
  UAT login — the capability survey the scaffold was always gated on.*
- **S5 — the richer axes**: grant-probe-failed vs unreachable, user-map
  completeness, latency. *Each a parallel probe + a column.*

## Open questions (decisions owed before S2/S4)

1. **Coarse facet vs per-use-case union — when does S2's `requiredBy` earn its
   place?** Today the coarse facet is a faithful MVP. S2 is justified when a flow
   needs *less* than its target's full facet (a data-only flow against a
   schema+data target should not demand ALTER) — i.e. when the survey would
   otherwise over-refuse. That is the trigger; until then the coarse facet holds
   (two-consumer / IR-grows-under-evidence).
2. **Survey as gate vs survey as readback.** S1 is a readback (exit 7 advisory).
   S3 makes it a hard gate. The R6 discipline (V2 owns no production write path
   during dual-track) suggests the gate is *advisory until the per-pair flip* —
   confirm with the operator before S3 hard-fails a run.
3. **Object-scope probe cost.** `sys.fn_my_permissions(NULL,'OBJECT')` per object
   is N round-trips; the EvidenceCache discovery-then-derive pattern
   (`DECISIONS 2026-05-19`) is the template — one survey query per place, derive
   per object. Hold the Big-O audit discipline.

## How to continue

- **Read order:** this letter → `THE_CLI.md` §6 (the `access`/`grant` permission
  axis — the actor profile) → `THE_USE_CASE_ONTOLOGY.obligations.md` (G0/G2/G6 —
  what each use case requires) → `src/Projection.Pipeline/Preflight.fs` (the probe
  + gate machinery) → `CapabilitySurvey.fs` (the prototype).
- **Disciplines that bind:** the probes are read-only — no event moves, no
  schema touched; pure-Core holds (the reconciliation is pure; the I/O is the
  boundary `survey`); IR grows under evidence (S2's `requiredBy` lands when a
  flow needs less than the coarse facet, not before); the voice register renders
  the matrix (it already does).
- **Running it:** `projection survey` against a `projection.json` with a `direct`
  environment; or unit-witness the core (`CapabilitySurveyTests`). Tests:
  `scripts/test.sh focus "CapabilitySurvey"` (pure) — and read
  `DECISIONS 2026-06-09 (Agent test-execution protocol)` first.

The hardening defined the actors' permission profile; the survey makes it
answerable across the whole estate, in parallel, before the run. The prototype
proves the arc end to end. Grow it to the obligations matrix and make it the
mandatory pre-flight, and the instrument will refuse to start against a place
that cannot do what it is about to be asked.

— *the outgoing agent, 2026-06-09*
