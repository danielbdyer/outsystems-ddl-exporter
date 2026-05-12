# PRODUCT AXIOMS — What V2 Must Verifiably Guarantee End-to-End

The L3 product-axiom layer. Sibling to `AXIOMS.md` (the L2 formal system) and `V2_PRODUCTION_CUTOVER.md` (the cutover plan). Code and tests cite this document by axiom ID (`L3-S1`, `L3-D1`, etc.) when verifying operator-facing properties.

This document is the canonical home for what V2 commits to *for the operator*. `AXIOMS.md` is the canonical home for the formal-system axioms that underwrite these commitments at the algebra level. The two documents are intentionally separate: `AXIOMS.md` proves V2 is internally consistent; this document proves V2 satisfies the operator's contract.

Coverage analysis (which axioms are structurally enforced at L1, convention-enforced, or unnamed gaps) is maintained in `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part IV and refreshed on each annual re-audit. Implementation campaigns that operationalize the unnamed L3 axioms are in `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part IX; chapter agents pick them up via `V2_PRODUCTION_CUTOVER.md`.

The axioms are grouped by **core concern** (the operator's mental partition of V2's responsibilities), not by formal-system theme. Five concerns plus one cross-cutting band: **schema, data, identity, diagnostics, cutover safety, cross-cutting**.

---

## Primitive notions (product-level)

- **Operator.** The principal end-user; deploys V2 artifacts to SQL Server; trusts V2 to be cutover-safe.
- **V2-driver.** The cutover ladder rung where V2 owns the production write path. Gated per (environment, artifact type) on N=10 consecutive green canary runs + operator sign-off.
- **V2-augmented.** The intermediate rung where V1 owns the production write path; V2 emits and verifies.
- **Dual-track window.** The cutover period during which V1 and V2 both run; canary asserts V1 ≈ V2 modulo named tolerances.
- **Canary.** The V1↔V2 round-trip comparison harness; blocks PR merges on unaccepted divergence.
- **Cutover-blocker (Tier 1).** A product axiom whose failure prevents V2-driver mode.
- **Strongly desired (Tier 2).** A product axiom whose failure is operator-painful but workable.
- **Nice-to-have (Tier 3).** A product axiom whose failure is cosmetic.

---

## Group S — Schema (L3-S1 through L3-S10)

**L3-S1. SSDT emit is byte-deterministic.** Given identical `osm_model.json` + `Catalog` + `Policy.empty` + `Profile.empty`, `SsdtDdlEmitter.emitSlices` produces identical per-table `.sql` files byte-for-byte across reruns, processes, machines, OSes, and time.

  *Underwriting.* T1 (partial — cross-platform extensions not covered).
  *Failure mode.* Operator's diff-based change detection misfires; immutable artifact stores reject re-uploads of supposedly-identical content.
  *Tier.* 1 (cutover blocker).

**L3-S2. DACPAC round-trip equality.** For Catalog C, `DacpacEmitter.emit C` produces a `.dacpac`; DacFx deserializes and `ReadSide.toCatalog` reconstructs C'; `Catalog.equivalent C C'` holds modulo the DacFx-erasure predicate (A37 candidate, named at chapter-3.4 close).

  *Underwriting.* T1 binary-normal-form variant + T11; A37 erasure declaration.
  *Failure mode.* Operator imports DACPAC into Visual Studio SSDT and finds missing index names; reproducible-build assumption breaks.
  *Tier.* 1.

**L3-S3. SSDT and DACPAC sibling-Π agreement.** Every Kind by SsKey in the Catalog appears in both `SsdtDdlEmitter` output and `DacpacEmitter` output. Keysets agree exactly (T11 structural); per-kind contents agree on identity-keyed structure modulo DacFx normalization.

  *Underwriting.* T11 + A18-amended.
  *Failure mode.* SSDT lists a table; DACPAC omits it. Operator's deploy via DacFx leaves a phantom table. (Cannot happen with current `ArtifactByKind` structural enforcement.)
  *Tier.* 1.

**L3-S4. Trigger definitions are complete.** Every Trigger in `Catalog.Triggers` with `IsDisabled = false` appears in emitted DDL as a `CREATE TRIGGER` statement with identical `Definition` text. Disabled triggers emit with disabled state preserved.

  *Underwriting.* — (no L2 axiom; gated on Phase A.0' Trigger IR lift).
  *Failure mode.* V2 emits DDL omitting triggers; data-validation triggers silently disappear; constraint checks applications relied on are gone.
  *Tier.* 1.

**L3-S5. Sequence definitions are complete.** Every Sequence in `Catalog.Sequences` appears in emitted DDL as a `CREATE SEQUENCE` statement with StartValue, Increment, Min, Max, IsCycleEnabled, CacheMode preserved.

  *Underwriting.* — (gated on Phase A.0' Sequence IR lift).
  *Failure mode.* Applications depending on sequence-generated IDs fail at cutover; sequences missing from target schema.
  *Tier.* 1.

**L3-S6. DEFAULT values round-trip.** Every Attribute with `DefaultValue` present produces a column definition with `DEFAULT` clause in DDL; round-tripped Catalog restores `DefaultValue`.

  *Underwriting.* — (gated on Phase A.0' DEFAULT IR lift).
  *Failure mode.* Column defaults silently disappear at cutover; subsequent INSERTs that relied on defaults produce NULLs or fail.
  *Tier.* 1.

**L3-S7. Computed columns render correctly.** Every Attribute with `IsComputed = true` and `ComputedDefinition` renders as a computed-column DDL; round-trip restores computed state and definition.

  *Underwriting.* — (gated on Phase A.0' Computed IR lift).
  *Failure mode.* Computed columns become regular nullable columns at cutover.
  *Tier.* 1.

**L3-S8. CHECK constraints are emitted.** Every `ColumnCheck` in `Kind.ColumnChecks` appears as a CHECK constraint in emitted DDL. Orphaned checks (naming nonexistent columns) fail at validation, not emit time.

  *Underwriting.* — (gated on Phase A.0' CHECK IR lift).
  *Failure mode.* Business-logic constraints disappear; applications relying on database-side validation start accepting invalid data.
  *Tier.* 1.

**L3-S9. ExtendedProperties round-trip.** Every ExtendedProperty on Module/Kind/Attribute/Index appears in SSDT extended-property comments and round-trips via DacFx.

  *Underwriting.* — (gated on Phase A.0' ExtendedProperties IR lift).
  *Failure mode.* Documentation embedded as extended properties (MS_Description and custom keys) disappears; institutional knowledge lost.
  *Tier.* 2.

**L3-S10. Catalog-coordinate identity.** Every cross-database FK (`TableId.Catalog` differs between source and target Kind) is emitted unchanged or rejected with structured error. No silent downgrade to same-database.

  *Underwriting.* — (gated on Phase A.0' Catalog-coordinate IR lift).
  *Failure mode.* Multi-database architectures silently lose cross-DB constraints.
  *Tier.* 2.

---

## Group D — Data (L3-D1 through L3-D10)

**L3-D1. CDC silence on idempotent redeploy.** Deploy `StaticSeedsEmitter` + `MigrationDependenciesEmitter` + `BootstrapEmitter` output to a production-shaped SQL Server with CDC enabled. Redeploy unchanged. Assert zero records inserted into `cdc.change_tables` for every CDC-tracked table.

  *Underwriting.* T1 (partial — formal axiomatization pending Campaign A.3 promotion).
  *Failure mode.* Every redeploy triggers spurious CDC change events; downstream ETL interprets these as real data changes and corrupts replicas.
  *Tier.* 1. **Highest-leverage single deliverable per `V2_DRIVER.md`.**

**L3-D2. Static-seed rows are deterministically ordered.** Given a static-entity definition with N rows, `StaticSeedsEmitter` produces a MERGE statement applying rows in canonical order (by PK if available; by content hash if not). Re-running with the same Catalog produces identical MERGE statement.

  *Underwriting.* T1 + D12 canonical-sort discipline.
  *Failure mode.* Two V2 runs produce different MERGE statements for the same data; diffs become noisy; signal lost.
  *Tier.* 1.

**L3-D3. MigrationDependencies PK assignment is deterministic.** Given a `MigrationDependencyContext` with auto-PK rows, the emitter produces MERGE statements where every PK value is a literal constant. For the same input set, re-emission produces identical PK literals.

  *Underwriting.* T1 + D5 (PK pre-computed at emit time).
  *Failure mode.* Operator runs migration twice across environments; different IDs assigned; referential integrity across environments breaks.
  *Tier.* 1.

**L3-D4. Topological order preserves FK validity.** `StaticSeedsEmitter` and `BootstrapEmitter` emit rows so every FK reference appears after the referenced table's rows are inserted. No FK violation during incremental insert.

  *Underwriting.* A25 + A33 + `TopologicalOrderPass`.
  *Failure mode.* Deploy fails midway with FK constraint violations; operator cannot tell which insert order was wrong.
  *Tier.* 1.

**L3-D5. Cycles are broken via user-supplied allowlist.** When a cycle exists in the FK graph, `TopologicalOrderPass` either rejects with structured error or breaks via the `allowedCycles` config entry. Cycles not in allowlist produce a structured validation error rather than nondeterministic ordering.

  *Underwriting.* A33 + A40 + `SelfLoopPolicy`.
  *Failure mode.* V2 silently picks an order for a cycle that fails at FK-enforcement time; operator cannot reproduce because the order depends on hash iteration.
  *Tier.* 2.

**L3-D6. User FK reflow maps every UserFk.** `UserFkReflowPass` discovers all Attributes marked `IsUserFk = true`, maps each source User ID to target User ID via `UserMatchingStrategy`, and produces a `UserRemapContext` covering every UserFk in the target environment. Missing mappings fail at validation.

  *Underwriting.* Chapter 4.2 (totality axiom not yet formally stated).
  *Failure mode.* Rows reference orphan user IDs in target; FK constraints fail or (worse) data resolves to the wrong user.
  *Tier.* 1.

**L3-D7. StaticData fixture JSON is schema-validated.** `StaticDataLoader` parses operator-supplied static-data JSON, validates column existence + type compatibility, and rejects unknown tables/columns with structured errors. Rows emitted in declaration order within each table.

  *Underwriting.* — (gated on Phase A.3).
  *Failure mode.* Operator writes static-data JSON referencing a renamed column; V2 silently drops it; production deploys with incomplete seed data.
  *Tier.* 2.

**L3-D8. MigrationDependencies JSON is schema-validated.** `MigrationDependencyLoader` parses JSON, resolves kind keys to Catalog kinds, validates column existence, rejects unknown columns/types. Rows emitted with auto-assigned PKs in canonical order.

  *Underwriting.* Phase A.3 + D5.
  *Failure mode.* Same as L3-D7 plus PK collisions if the auto-PK pass observes a stale MAX(Id).
  *Tier.* 2.

**L3-D9. Bootstrap emitter covers all remaining rows.** `BootstrapEmitter` (when `EmissionPolicy = AllRemaining`) discovers every row in the profile-scanned database not covered by `StaticSeedsEmitter` or `MigrationDependenciesEmitter`. Set-union property: static + migration + bootstrap covers full profile population. No row emitted twice; no row omitted.

  *Underwriting.* Chapter 4.1.B partition-assertion (closest analog).
  *Failure mode.* Rows emitted twice → deploy fails on PK violation. Rows omitted → production database missing data.
  *Tier.* 2.

**L3-D10. Emission policy gates work correctly.** Config-driven `emission.staticSeeds`, `emission.migrationDependencies`, `emission.bootstrap` toggles produce the expected MERGE statements. Toggling any subset off produces output minus that emitter's rows; toggling all off produces zero data emission.

  *Underwriting.* — (gated on Phase A.2).
  *Failure mode.* Operator's intent (e.g., "schema-only emit") is silently ignored; data ends up in the wrong environment.
  *Tier.* 2.

---

## Group I — Identity (L3-I1 through L3-I10)

**L3-I1. SsKey is stable under physical rename.** For any Catalog C, rename spec R, `TableRename.run R C = C'`, every Kind's SsKey is identical in C and C' (only Physical TableId differs).

  *Underwriting.* A1.
  *Failure mode.* Rename breaks FK joins, refactor logs, audit trails — every downstream consumer that resolves by identity gets a different identity for the same row.
  *Tier.* 1. **Shipped 2026-05-12 in commit `502592f` + Slice 1 PhysicallyRenamed at `9d578cc`.**

**L3-I2. OssysOriginal SsKeys survive extraction.** Every Kind with `SsKey = OssysOriginal(guid)` in the input osm_model.json round-trips through V2 with identical guid. (Bound: JSON projection lossiness documented in A1's amendment; unconditional once SnapshotRowsets lands.)

  *Underwriting.* A1 + bound.
  *Failure mode.* V2 invents new identity for a kind V1 already had identity for; cross-version diffs break.
  *Tier.* 1.

**L3-I3. SsKey synthesis is deterministic.** For Kinds with `SsKey = Synthesized(source, basis)`, the synthesis function is pure. Same Catalog + module name + entity name + attributes produce identical Synthesized SsKey across reruns. No randomness, no `Guid.NewGuid`.

  *Underwriting.* T1.
  *Failure mode.* Cross-run identity instability; every re-extraction looks like a different schema.
  *Tier.* 1.

**L3-I4. RefactorLog records rename history.** Every rename (via `TableRenamePass` or via cross-version diff in `CatalogDiff.between`) produces a `RefactorLog` entry with from/to identity, reason tag, and (optional) timestamp. Composing `diff(V0→V1) + diff(V1→V2)` produces a transitive `diff(V0→V2)` modulo loss-free paths.

  *Underwriting.* T9 (refactor freedom under rename — close but not specific to the emission contract).
  *Failure mode.* SSDT's refactor-log mechanism doesn't recognize the rename; DacFx treats it as drop+create; data lost.
  *Tier.* 2.

**L3-I5. V1Mapped SsKeys enable legacy identity tracking.** For any V1 SS_Key GUID in an extracted model, V2 can construct `SsKey = V1Mapped(v1Guid, namespace)` and track it through the pipeline. `DerivedFrom` chains connect splits/merges back to originals.

  *Underwriting.* A1 four-variant DU.
  *Failure mode.* Legacy identity tracking breaks; V1↔V2 audit trails diverge.
  *Tier.* 2.

**L3-I6. Identity is stable across schema evolution.** Given Catalog C_v0 and post-evolution Catalog C_v1, every Kind that persists across versions has an SsKey such that `Kind_v0.SsKey` relates to `Kind_v1.SsKey` via `RefactorLog`. Persistent kinds are not re-keyed.

  *Underwriting.* A1 + A4.
  *Failure mode.* Schema-evolution dashboard treats every kind as new; rename history disappears.
  *Tier.* 2.

**L3-I7. Name changes don't affect SsKey.** Renaming a Kind's `Name` from "Order" to "SalesOrder" updates `Kind.Name` (presentation) but not `Kind.SsKey` (identity) or `Kind.Physical.Table` (physical name — those axes are independent).

  *Underwriting.* A2 + A3 + A15.
  *Failure mode.* Rename of presentation Name accidentally re-keys the Kind; downstream identity lookups break.
  *Tier.* 1.

**L3-I8. SsKey uniqueness within Catalog.** No two Kinds in a Catalog have the same SsKey. Enforced by `Catalog.create` smart constructor.

  *Underwriting.* A39.
  *Failure mode.* Identity collisions; `Catalog.tryFindKind` returns the wrong Kind.
  *Tier.* 1.

**L3-I9. Attribute identity persists under column rename.** Each Attribute within a Kind carries its own SsKey. When a column is renamed, its `Attribute.SsKey` is stable.

  *Underwriting.* A1 generalization (gated on chapter 3.2 SnapshotRowsets).
  *Failure mode.* Column rename re-keys the Attribute; per-column audit trails break.
  *Tier.* 2.

**L3-I10. Cross-catalog reference identity is consistent.** When Catalog coordinates include per-database scope (`Kind.TableId.Catalog`), every Reference to a Kind in a different catalog preserves its Catalog identity. Cross-catalog FKs don't collapse to same-database.

  *Underwriting.* — (gated on Phase A.0' Catalog-coordinate IR lift).
  *Failure mode.* Multi-database FKs silently degrade; cross-DB constraints lost.
  *Tier.* 2.

---

## Group X — Diagnostics (L3-X1 through L3-X10)

**L3-X1. Every pass decision is emitted as a LineageEvent.** Every decision made by any pass produces exactly one LineageEvent in the output Lineage. Silence = no decision made (named `KeepReason` variant says so).

  *Underwriting.* A25.
  *Failure mode.* V2 makes a decision that doesn't appear in audit trail; operator cannot reconstruct V2's reasoning.
  *Tier.* 1.

**L3-X2. Opportunity routing is complete and correct.** Every opportunity-producing strategy routes its finding to `DecisionLogEmitter` (Tier 1: always), `OpportunitiesEmitter` (Tier 2: operator-focused), and `ValidationsEmitter` (Tier 3: auditor-focused). Each channel receives correctly-formatted entries.

  *Underwriting.* `Routing.route` code-prefix dispatch (totality is convention-enforced).
  *Failure mode.* Operator sees a finding in DecisionLog but not in Opportunities; misses the cue to act.
  *Tier.* 1.

**L3-X3. Opportunity severity matches consequence.** Orphaned FKs are `Severity = High` (data loss risk); nulls-in-mandatory are `Medium`; unique-index duplicates are `High`. Re-sorting by severity produces a stable, operator-meaningful ordering.

  *Underwriting.* Discrete-rationale DUs absorb continuous evidence (DECISIONS 2026-05-13).
  *Failure mode.* Operator triages by severity; acts on low-severity items while critical findings sit in backlog.
  *Tier.* 2.

**L3-X4. Lineage trail is earliest-first.** When passes compose (A → B → C), LineageEvents from A appear before B and before C in the final trail.

  *Underwriting.* A24.
  *Failure mode.* Audit trail reads in reverse chronology; debugging harder than it should be.
  *Tier.* 1.

**L3-X5. Validation failures include remediation guidance.** Every validation failure entry includes a `RemediationSuggestion` field naming the operator action. Suggestions are actionable without requiring V2 source reading.

  *Underwriting.* — (gated on chapter 4.3).
  *Failure mode.* Operator reads "validation failed" but has no idea what to do.
  *Tier.* 2.

**L3-X6. Detection pass triggers are traceable.** If a detection pass produces an opportunity, Lineage includes source evidence (which table, column, FK target) such that an operator can navigate from the opportunity back to the schema element.

  *Underwriting.* A23 (events carry SsKey).
  *Failure mode.* Opportunity says "orphan FK" but doesn't name which one in a 300-table schema.
  *Tier.* 2.

**L3-X7. Silent V1 drops are documented.** When V1 carried a capability V2 doesn't (e.g., a metadata field V2's adapter doesn't carry), the absence is logged as `Severity = Info` in Validations — not omitted.

  *Underwriting.* — (overlaps with L3-Boundary-NoSilentDrop; gated on chapter 3.2 + 4.3).
  *Failure mode.* V2 silently drops fields V1 had; operator never knows.
  *Tier.* 2.

**L3-X8. Tolerance taxonomy decisions are recorded.** Every divergence between V1 and V2 output that matches a named tolerance entry is logged with the tolerance tag in DecisionLog. Non-tolerances block the PR.

  *Underwriting.* R6 + Tolerance taxonomy.
  *Failure mode.* Operator cannot audit which V1↔V2 differences were accepted; cannot tell silent drift from explicit allowlist.
  *Tier.* 2.

**L3-X9. Config validation errors are structured.** When unified config fails to parse or validate, the error includes (path, reason, suggestion). Operator can fix the config without reading JSON schema.

  *Underwriting.* D9 (secret-free by construction; structured errors mandated).
  *Failure mode.* Operator gets `JsonException: unexpected token` and has no idea where in their 200-line config the problem is.
  *Tier.* 2.

**L3-X10. Multi-environment policy diffs are signaled.** When policy diverges between environments (different `UserMatchingStrategy` per env, different `EmissionPolicy`), Lineage includes a policy-delta entry naming what diverged.

  *Underwriting.* — (gated on chapter 4.1.A M4).
  *Failure mode.* Operator deploys with different policy in UAT than PROD without noticing.
  *Tier.* 2.

---

## Group C — Cutover safety (L3-C1 through L3-C10)

**L3-C1. Canary asserts V1 ≈ V2 modulo named tolerances.** When both V1 and V2 emit artifacts from the same source, the canary compares outputs byte-for-byte on SSDT and set-for-set on data. Every divergence either matches a Tolerance taxonomy entry or fails the canary.

  *Underwriting.* R6.
  *Failure mode.* Silent divergence passes the canary; V2 ships with bugs that don't surface until production.
  *Tier.* 1.

**L3-C2. Fallback ladder is enforced at T-30 and T-15.** At T-30 pre-cutover, if any precondition is unmet, drop to V2-augmented or V1-only per the gate. At T-15, if canary flake >10% or tolerance churn is uncontrolled, drop to V1-only. V1 stays warm through cutover+30 regardless.

  *Underwriting.* T-30/T-15 gates per DECISIONS 2026-05-22.
  *Failure mode.* Operator ships V2 despite a yellow canary; production breaks.
  *Tier.* 1.

**L3-C3. V2 owns no production write path during dual-track.** During V2-augmented, V2 emits but doesn't deploy. V1 owns the deploy. Canary reads V1-deployed schema via `ReadSide`, compares to V2's expected Catalog, asserts agreement.

  *Underwriting.* R6.
  *Failure mode.* Split-brain — both V1 and V2 writing to production; reconciliation impossible.
  *Tier.* 1.

**L3-C4. Per-environment-per-artifact-type V2-driver transition.** V2-driver transition is gated per (env, artifact type) pair — not global. Each pair has its own N=10 green-run counter + operator sign-off.

  *Underwriting.* R6.
  *Failure mode.* Operator over-commits to V2-driver before evidence is in.
  *Tier.* 1.

**L3-C5. Canary green for ≥30 generated cases and ≥4 production cases.** Before declaring canary green on an axis, run the property test on ≥30 synthetic catalogs (FsCheck) and on ≥4 real production catalogs.

  *Underwriting.* Chapter 3.4 canary tier discipline.
  *Failure mode.* Synthetic-only canary misses production-specific edge cases.
  *Tier.* 1.

**L3-C6. Idempotent-redeploy holds across all CDC-tracked tables.** (Co-located with L3-D1; named here as a cutover-safety axiom because it gates the cutover ladder.)

  *Underwriting.* T1 + topological-sort idempotence.
  *Failure mode.* Redeploy triggers spurious CDC events; downstream ETL corruption.
  *Tier.* 1.

**L3-C7. Canonical ordering prevents determinism regression.** Config validation applies canonical sort to user-supplied collections at parse time. Two configs with the same entries in different orders produce identical output.

  *Underwriting.* D12.
  *Failure mode.* Operator reorders config; output diff is non-empty; cannot tell signal from noise.
  *Tier.* 1.

**L3-C8. No credentials in unified config.** Static analyzer + parser rule forbids `connectionString` / `password` / `accessToken` properties anywhere in the Config type tree. Connection strings sourced from env vars or separate non-checked-in files.

  *Underwriting.* D9.
  *Failure mode.* Operator checks credentials into git; secret-rotation cost; possible breach.
  *Tier.* 1.

**L3-C9. Dry-run completeness before cutover.** ≥1 full end-to-end dry-run on a production-shaped environment completes successfully and produces artifacts passing the canary before cutover begins.

  *Underwriting.* V2_DRIVER phase ladder.
  *Failure mode.* Cutover hits novel scenarios in production that no dry-run exercised.
  *Tier.* 1.

**L3-C10. V1 sunset begins only after cutover+30 + one schema-evolution cycle.** V1 archived only after cutover+30 elapsed AND all four environments have run V2 emissions for ≥1 full schema-evolution cycle AND canary green throughout AND operator confirmation.

  *Underwriting.* R6 + V2_DRIVER.
  *Failure mode.* V1 archived too early; rollback impossible.
  *Tier.* 1.

---

## Group CC — Cross-cutting (L3-CC1 through L3-CC6)

**L3-CC1. Π contract is universal.** Every emitter has the signature `Catalog -> Result<ArtifactByKind<'element>, EmitError>` where `ArtifactByKind` enforces "every Catalog kind appears in the output keyset" at compile time.

  *Underwriting.* T11 + Stage 0 S0.B.
  *Failure mode.* An emitter could be added that silently drops a kind from its output. (Cannot happen with current structural enforcement.)
  *Tier.* 1.

**L3-CC2. Pass contract is universal.** Every pass has the signature `Catalog -> Policy -> Profile -> Lineage<'output>` or `Catalog -> Policy -> Profile -> Lineage<Diagnostics<'output>>`. A18-amended: no pass consumes Policy from inside its implementation.

  *Underwriting.* Pass return-type codification (DECISIONS 2026-05-13).
  *Failure mode.* A pass could be added that doesn't emit lineage; audit trail incomplete.
  *Tier.* 1.

**L3-CC3. Lineage is monotonic under composition.** When passes compose (A then B then C), the Lineage from the composition is `A.lineage ++ B.lineage ++ C.lineage` — earliest-first concatenation. No reordering, no loss, no duplication.

  *Underwriting.* A24.
  *Failure mode.* Lineage reordering breaks audit trail chronology.
  *Tier.* 1.

**L3-CC4. IR fidelity is complete for production workloads.** Every schema concept in V1's `OsmModel` that appears in the operator's four production environments is representable in V2's `Catalog` and survives the emit→read→reconstruct round-trip. Concepts deliberately excluded are documented with rationale.

  *Underwriting.* — (Phase A.0' workstream operationalizes this).
  *Failure mode.* Silent feature loss at cutover. **Tier-1 unnamed axiom; Campaign A.2 target.**
  *Tier.* 1.

**L3-CC5. Performance regression is caught before merge.** Every hot-path function is gated by the statistical perf baseline. Perf regression beyond μ + 5σ blocks CI. Baseline updates explicit and paired with a DECISIONS amendment.

  *Underwriting.* Bench-driven-optimization protocol.
  *Failure mode.* Perf regression ships; cutover-day latency exceeds tolerance.
  *Tier.* 1.

**L3-CC6. Domain-first naming is enforced at review time.** Every new type/function/file/module name passes the four-question naming analysis. Performance-of-compliance and domain-blind-naming failure modes caught at review.

  *Underwriting.* DECISIONS 2026-05-10 pillar 8.
  *Failure mode.* Names drift from domain concepts; ubiquitous-language consistency degrades.
  *Tier.* 1 (operationally; lint partial).

---

## Group Boundary — Unnamed candidates (pending formalization)

The audit's gap-hunt surfaced four Tier-1 unnamed axioms that should be formalized as part of Campaign A (per `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part IX). Listed here as **candidates**; will become full L3 axioms when Campaign A lands. Each carries no L2 underwriting today; that's the work.

**L3-Boundary-AtomicEmission (candidate).** When V2 emit fails partway through, the output directory is left untouched — no half-written files. `Compose.write` is transactional: write-to-temp, fsync, atomic-rename. On any failure, clean up temps and leave the output directory unchanged.

  *Failure mode.* Operator hits midnight failure scenario; deletes output dir to retry; discovers the SSDT project they thought was clean was actually a mix of v1.0 and v1.1 files.
  *Tier.* 1.

**L3-Boundary-NoSilentDrop (candidate).** Every concept Phase A.0' enumerates as "lifted into IR" gets one of two outcomes: (a) a typed `Catalog` field, OR (b) a `Diagnostic.Severity = Error` at the adapter boundary. No silent passthrough.

  *Failure mode.* V1 has a CHECK constraint V2 silently doesn't represent; production schema drifts from V1's; operator never knows.
  *Tier.* 1.

**L3-Boundary-ManifestMatchesDisk (candidate).** Post-`Compose.write`, every manifest entry corresponds to a file on disk; every file on disk has a manifest entry.

  *Failure mode.* File-write failed partway; manifest lists files that don't exist; operator's reconciliation tool gives false confidence.
  *Tier.* 1.

**L3-Idempotence-OnRedeploy (candidate).** Re-running V2 emit + re-running the deploy produces a target DB state byte-equivalent to running once. Subsumes L3-D1 (CDC silence) at the deploy boundary.

  *Failure mode.* See L3-D1 — spurious CDC events corrupt downstream ETL.
  *Tier.* 1.

Plus ~10 Tier-2 candidates from the audit's gap-hunt (Q1-Q30; see `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part VIII for full statements with failure modes). These will be formalized through Campaign C.

---

## Group Lifecycle — Not yet operationalized

`AXIOMS.md` A6-amended names Lifecycle as one of V2's three substantive aggregates (Catalog + Policy + Lifecycle). The temporal axis is **named but not operationalized** — the `Lifecycle` type doesn't exist yet. Product axioms in this group are pending the structural foundation.

Expected axioms once Lifecycle ships (placeholders, not yet stated):
- **L3-L1.** Schema evolution is replayable: given a sequence of Catalog versions C_0, C_1, ..., C_n, V2 can reproduce any C_i from C_0 plus the chain of evolutions.
- **L3-L2.** Refactor-log history is monotonic: appending a new evolution to history doesn't alter prior history.
- **L3-L3.** Per-environment timeline is independent: dev's evolution log is independent of UAT's.

When Lifecycle lands, this section moves from placeholder to formal axioms.

---

## V2 Amendments

(Reserved for amended originals. Pattern mirrors `AXIOMS.md`'s V2 Amendments section.) None today. The first amendment lands when an L3 axiom is renamed, narrowed, or split as a consequence of campaign execution.

---

## Cross-references

- `AXIOMS.md` — L2 formal-system axioms (A1–A40 + T1–T11) that underwrite this surface.
- `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` — coverage map (which L3 axioms are Bucket A/B/C/D), campaigns to address Bucket D, methodology.
- `V2_PRODUCTION_CUTOVER.md` — cutover plan; campaigns from the audit operationalize as Phase A workstreams here.
- `VISION.md` — strategic frame; the *why* of the cutover.
- `V2_DRIVER.md` — KPI ladder and destination.
- `CLAUDE.md` — load-bearing commitments + reading order.

---

## Maintenance

This document grows when:
1. A new L3 axiom is identified (typically through audit, operator feedback, or campaign-execution surprise).
2. An unnamed-axiom candidate from Group Boundary is operationalized; it moves into its appropriate concern group.
3. Lifecycle ships and the placeholder Group Lifecycle axioms become real.

This document is **constitutional**, not analytical. Per-axiom analysis (current coverage status, test pointers, structural underwriting in L1) lives in `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`. When the coverage shifts (a Bucket-D axiom moves to Bucket A after Campaign A), the change is recorded in the audit doc, not here. Here, axioms either exist or they don't.

Amendments use the pattern: append a dated note under the axiom; never rewrite the original statement.
