# Personal code-verification of contestable claims (tree ea21b13)

## V1 — topo-order-defeated (blocker → REFINED)
Claim: "TopologicalOrderPass NEVER orders any FK-bearing catalog on publish."
VERDICT: REFINED (claim too strong; real defect confirmed).
- v2's data emitters DO call the pass: BootstrapEmitter.fs:167, StaticSeedsEmitter.fs:385,
  DataEmissionComposer.fs:246/362/425/524/538/667 (`TopologicalOrderPass.runWith TreatAsCycle`).
- The pass orders FK targets before referencers (TopologicalOrderPass.fs:79-81). BUT when ANY SCC
  is unresolved it sets Mode=Alphabetical for the WHOLE catalog and returns nodes sorted by SsKey
  (TopologicalOrderPass.fs:503-556). Under `TreatAsCycle` self-loops are reported as 1-cycles the
  resolver refuses (fs:51-61); weak (nullable) cycles resolve under v5, non-weak/self do not.
- EMPIRICAL: v2-live run emitted `structural.cycleUnresolved` ×6 on the 15-entity estate → Alphabetical
  fallback → Bootstrap.sql rendered Customer (SsKey bbbb…0001) before its FK target City (cccc…0001)
  → linear execution fails FK 547 (proven). The DDL bundle is unaffected (per-table files; DacFx/SSDT
  resolve refs at build). Only the linearly-executed Data/*.sql artifact breaks.
- IMPACT: real OutSystems estates (300 tables) have many cycles → the shipped Data/Bootstrap.sql
  (which the sqlproj None's as "a separate post-publish load step the receiving team's pipeline must add")
  is not linearly executable on any estate with an unresolved cycle. Blocker for the data-lane file contract;
  the internal --go load leg may level differently (not the shipped-file path). Neither side's bootstrap
  deployed cleanly on this estate (v1 broke on physical names, EF-9).

## V2 — v1-cascade-case-bug (major v1-latent-bug → CONFIRMED)
Claim: v1 MapDeleteRule is a case-sensitive switch over OutSystems vocab that never matches the
sys-catalog vocab, so every physically-backed FK's delete rule downgrades to NO ACTION.
VERDICT: CONFIRMED.
- ForeignKeyEvidenceResolver.cs:141-143: when constraint.OnDeleteAction (sys-catalog desc:
  "CASCADE"/"SET_NULL"/"NO_ACTION") is present, feeds it to deleteRuleMapper = MapDeleteRule.
- SmoEntityEmitter.cs:177-185: switch matches only {"Cascade","Delete","Protect","Ignore","SetNull"}
  → "CASCADE"/"SET_NULL" fall to `_ => NoAction` (line 184).
- Perverse corollary: for a LOGICAL-ONLY ref (no deployed constraint) line 143 uses the MODEL code
  (attribute.Reference.DeleteRuleCode ∈ model vocab) → "Delete"→Cascade WORKS. So v1 emits delete
  rules for logical-only FKs but drops them for physically-backed ones (the common case). No config
  reaches MapDeleteRule. ON UPDATE is structurally unrepresentable (SmoForeignKeyDefinition has no field).
- This is the mechanism behind empirical EF-1. Genuine v1 bug (not a design choice).

## V2 — g5b-lane-order (major latent → CONFIRMED)
Claim: wired post-deploy :r order is alphabetical (MigrationData before StaticSeeds), inverting intended.
VERDICT: CONFIRMED (latent — fires only when both lanes present + cross-lane FK).
- Pipeline.fs:1640-1643: dataLanes = outputs.DataBundle |> Map.toList |> List.map fst |> List.sort (ALPHABETICAL),
  then filter out Bootstrap → postDeployLanes. "Data/MigrationData.sql" < "Data/StaticSeeds.sql" → MigrationData
  :r-included first. PostDeployEmitter.renderIncludes preserves caller order (PostDeployEmitter.fs:53-61).
- Static entities are reference data; migration rows may FK into them → MigrationData-first fails linearly.
- My run had only StaticSeeds (no MigrationData) so unobserved empirically; code path is unambiguous.

## V2 — ossys-user-supplemental-gap (major conditional → CONFIRMED, nuanced)
Claim: v1 default seeds dbo.User (includeUsers default true); v2 default publish seeds no user rows.
VERDICT: CONFIRMED with nuance.
- V1: includeUsers = configuration.IncludeUsers ?? true (BuildSsdtRequestAssembler.cs:213,
  PipelineRequestContextBuilder.cs:135); config/supplemental/ossys-user.json exists → dbo.User rows in bootstrap by default.
- V2: adapter never marks IsUserFk (no source match, only compiled DLLs); userRemap paths REMAP FK values
  across environments, they do not SEED user rows. A default publish emits no platform-user rows.
- NUANCE: v1's bulk user-seed is environment-blind; v2's design defers to explicit user remapping
  (transfer --reconcile <UserTable>:<emailCol>), which is CORRECT for multi-env cutover but near-inert today
  (IsUserFk unmarked, matching-strategy config removed — packet E9). Net: a v2 publish of a table with a
  mandatory CreatedBy/UpdatedBy FK to the platform user table has dangling refs out of the box unless the user
  table is separately populated. Conditional major; v1 "just works" here, v2 needs the reconcile step wired.

## Confirmed "fixed on main" (switchover-load-bearing; empirically + code verified)
- G3 refactorlog → bundle: v2-live emitted ProjectionCatalog.refactorlog + RefactorLog item in sqlproj (#671). CONFIRMED.
- G6 CREATE SCHEMA: v2-live emitted Schemas/billing.sql; deploys clean; v1 emits none (needs hand-authored schema). CONFIRMED.
- PK naming (WP-8): PK names byte-identical v1↔v2 on all 15 tables incl composite. CONFIRMED empirically.
- WP-1a HasDbConstraint live: logical-only vs backed split real on live path (both create FK_JobRun_User; parity). CONFIRMED.
- G5a remediation glob hazard: manifest.remediation.sql at bundle root, NOT Build-Removed in emitted sqlproj
  (empty only because estate clean; RemediationEmitter emits active SELECT when candidates exist). CONFIRMED latent.
