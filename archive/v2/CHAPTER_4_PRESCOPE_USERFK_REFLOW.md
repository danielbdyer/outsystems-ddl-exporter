# Chapter 4.2 pre-scope — User FK reflow as Policy + pass + sibling Π consumers

## §1 Scope and value

This chapter slice cashes out V2's `UserMatchingStrategy` axis, `UserRemapContext` value, and `UserFkReflow` discovery pass. It is the **V2 algebraic restatement of V1's UAT-Users pipeline** (`src/Osm.Pipeline/UatUsers/`). The cutover demand is concrete: the 300-table OutSystems 11 system threads `CreatedBy` / `UpdatedBy` user FKs through every entity, and reflowing legacy domain rows from Dev → UAT (or QA → UAT, or any source-target pair across the four-environment plan) produces orphan FK values whenever a source-environment user has no direct counterpart in the target. The cutover-window safety criterion is *every reflowed row's `CreatedBy` and `UpdatedBy` resolves to a real target-environment user* — no orphan FKs to phantom users, no silently dropped attributions.

V1 already does this, in C#: `UserMatchingEngine.Execute` (`src/Osm.Pipeline/UatUsers/UserMatchingEngine.cs:19-79`) walks `OrphanUserIds`, looks each up in a QA inventory, applies `CaseInsensitiveEmail | ExactAttribute | Regex` matching against a UAT inventory, and falls through to a configurable fallback assignment (`Ignore | SingleTarget | RoundRobin`). The orchestration is a five-step pipeline (`Steps/DiscoverUserFkCatalogStep`, `LoadQaUserInventoryStep`, `LoadUatUserInventoryStep`, `AnalyzeForeignKeyValuesStep`, `ApplyMatchingStrategyStep`, `ValidateUserMapStep`, `EmitArtifactsStep`). What V2 inherits from V1 is the **operator workflow shape** (orphan discovery + per-strategy match + fallback + audit artifact). What V2 adds is **algebraic legibility**: the strategy is a closed DU, the result is a writer-monadic value carrying lineage and diagnostics, and the consumers are sibling Π's whose commutativity is structurally testable (T11). The output of this chapter is a `UserRemapContext` value that any data-emission Π can consume to rewrite user-FK columns at emission time without each Π re-implementing the matching logic.

## §2 The four-axis problem

`Policy` today is a four-axis record (`Policy.fs:234-239`) carrying Selection / Emission / Insertion / Tightening. Each axis names a distinct dimension of operator intent: which kinds participate, which artifact families emit, how data is applied, what shape of constraint decisions gets produced. `UserMatching` is intent in exactly the same sense — it is a per-environment operator decision, supplied at promotion time, that describes how cross-environment user identity should be reconciled. It is not evidence (Profile carries the empirical user populations); it is not structure (Catalog carries the FK shape); it is the operator's choice of *how to bridge* evidence between environments.

The new `Policy` shape:

```fsharp
type Policy = {
    Selection    : SelectionPolicy
    Emission     : EmissionPolicy
    Insertion    : InsertionPolicy
    Tightening   : TighteningPolicy
    UserMatching : UserMatchingStrategy
}
```

Whether the addition is breaking is the right question to apply the closed-DU expansion empirical-test discipline to (`DECISIONS 2026-05-13` — Closed-DU expansion). The discipline says: F# exhaustiveness errors should light up only at match sites; if callers outside the variant's module need reshaping, the seam is wrong. Adding a record field does not trigger DU exhaustiveness — record-construction sites must add `UserMatching = ...`, but pattern-match sites that destructure `Policy` (today: zero sites; consumers read fields by name) are unaffected. The expansion is non-breaking by the same standard the chapter applies to closed-DU expansion: every existing call site continues to type-check after adding `UserMatching = UserMatchingStrategy.empty` to `Policy.empty` (`Policy.fs:514-518`). This is the well-positioned-seam empirical test passing for record-axis growth.

## §3 `UserMatchingStrategy` DU

The masterwork's R3 sketch (`VISION_REVIEW.md:114-119`) lists four variants. V1's empirical experience (`UserMatchingEngine.cs:33-67` and `UserMatchingOptions.cs:7-19`) confirms the cardinality: V1 ships three primary strategies (`CaseInsensitiveEmail`, `ExactAttribute`, `Regex`) and one orthogonal fallback dimension (`Ignore | SingleTarget | RoundRobin`). V2 collapses V1's `Regex` into the more general `ManualOverride` (since regex is operator-supplied transformation, structurally indistinguishable from operator-supplied mapping for V2's algebraic purposes), folds `ExactAttribute` into `BySsKey` (the V2-native identity attribute), and treats fallback as one strategy variant instead of an orthogonal dimension.

```fsharp
type UserId = UserId of int
type SourceUserId = SourceUserId of UserId
type TargetUserId = TargetUserId of UserId
type Email = Email of string

[<RequireQualifiedAccess>]
module UserId =
    let value (UserId v) : int = v

[<RequireQualifiedAccess>]
module Email =
    let private emailEmpty =
        ValidationError.create "email.empty" "An email cannot be blank."
    let create (value: string) : Result<Email> =
        if System.String.IsNullOrWhiteSpace value then Result.failureOf emailEmpty
        else Result.success (Email (value.Trim()))
    let value (Email v) : string = v

type UserMatchingStrategy =
    | ByEmail
    | BySsKey
    | ManualOverride of Map<SourceUserId, TargetUserId>
    | FallbackToSystemUser of fallback: TargetUserId * primary: UserMatchingStrategy
```

The composition shape — `FallbackToSystemUser of fallback: TargetUserId * primary: UserMatchingStrategy` — is the deliberate choice over a list-of-rules wrapper. Argument: V1's empirical pipeline uses fallback as a *post-hoc* layer on top of one primary strategy; nesting structurally encodes "try the primary; on miss, attribute to the system user." The list-of-rules alternative invites composability the operator workflow does not actually need, and `BySsKey | ByEmail` ordering would be a third variant (`OrTried of UserMatchingStrategy * UserMatchingStrategy`) the IR-grows-under-evidence discipline says should not exist until a real consumer demands it. Smart constructors apply where invariants exist: `Email.create` rejects blank input; `UserMatchingStrategy.empty = ByEmail` is the sensible default mirroring V1's `CaseInsensitiveEmail = 0` enum default (`UserMatchingOptions.cs:9`). `ManualOverride Map.empty` is structurally valid (a degenerate override map is a no-op).

Per-variant semantics:

- **`ByEmail`** — match source user by email to target user with same email. V1's `CaseInsensitiveEmail` strategy. Failure mode: identical email in two environments belonging to logically different humans; or environment-divergent email format. V1 normalizes via `OrdinalIgnoreCase` and `Trim()` (`UserMatchingEngine.cs:84-86, 97`); V2 does the same via `Email.create`'s normalization.
- **`BySsKey`** — match by V1 SSKey GUID carried as the user's `OssysOriginal` SsKey. The most identity-stable strategy when both environments inherit from a shared OSSYS origin. Failure mode: target environment lacks the source's user (legitimate; users are environment-resident). V1 has no exact-SsKey strategy; this is the V2-native cleanup since V2 already carries `SsKey` as identity (A4).
- **`ManualOverride`** — operator-supplied per-user mapping. Always works; operator-burdensome. V1's `UserMapLoader.Load` (`src/Osm.Pipeline/UatUsers/UserMapLoader.cs:7-80`) parses a CSV with `SourceUserId,TargetUserId,Rationale`; V2 inherits the schema at the boundary and the boundary adapter constructs `Map<SourceUserId, TargetUserId>`.
- **`FallbackToSystemUser`** — for source users with no target counterpart, attribute to a designated system user. The primary strategy runs first; misses route to the fallback. Useful as the final safety net.

## §4 `UserRemapContext` and the discovery pass

`UserRemapContext` is what the pass produces and emitters consume. Its shape:

```fsharp
type RemapDiagnostic =
    | NoEmail of source: SourceUserId
    | EmailDidNotMatch of source: SourceUserId * email: Email
    | SsKeyDidNotMatch of source: SourceUserId * key: SsKey
    | OverrideMissing of source: SourceUserId
    | NoFallbackConfigured of source: SourceUserId

type UserRemapContext = {
    Mapping     : Map<SourceUserId, TargetUserId>
    Unmatched   : Set<SourceUserId>
    Diagnostics : RemapDiagnostic list
}

[<RequireQualifiedAccess>]
module UserRemapContext =
    let empty : UserRemapContext =
        { Mapping = Map.empty; Unmatched = Set.empty; Diagnostics = [] }
    let isFullyMapped (c: UserRemapContext) : bool = Set.isEmpty c.Unmatched
```

The masterwork's sketch types `UserRemapContext` as `Map<SsKey, Map<SourceUserId, TargetUserId>>` (`AXIOMS.md:486-489`, `VISION.md:169`). The outer `SsKey` keys the user-kind itself; in V2, the user kind is uniquely identified by its SsKey, but a single deployment has exactly one user kind, so the outer Map degenerates. The simpler shape above (flat `Map<SourceUserId, TargetUserId>`) is what consumers actually use. If a future deployment has multiple user kinds, the outer Map re-emerges under the IR-grows-under-evidence discipline; until then, the flat form pays its weight.

`UserPopulation` lives in `Profile`, added under IR-grows-under-evidence (the discovery pass is the first consumer):

```fsharp
type UserAttributes = {
    UserId : SourceUserId  // or TargetUserId, contextually
    SsKey  : SsKey
    Email  : Email option
}
type UserPopulation = {
    Users : UserAttributes list
}
```

Per A34, Profile carries no back-references; `UserAttributes.SsKey` is just an SsKey value. `Profile` gains two fields (`SourceUsers : UserPopulation` and `TargetUsers : UserPopulation`) — Profile is the natural home because user populations are *empirical* evidence (what users actually exist in each environment), not structural (the user kind exists once in Catalog).

The discovery pass:

```fsharp
[<RequireQualifiedAccess>]
module UserFkReflowPass =
    [<Literal>]
    let version : int = 1
    [<Literal>]
    let private passName : string = "userFkReflow"

    let discover
        (sourceUsers : UserPopulation)
        (targetUsers : UserPopulation)
        (strategy    : UserMatchingStrategy)
        : Lineage<Diagnostics<UserRemapContext>> = ...

    let run
        (catalog : Catalog)
        (policy  : Policy)
        (profile : Profile)
        : Lineage<Diagnostics<UserRemapContext>> =
        discover profile.SourceUsers profile.TargetUsers policy.UserMatching
```

The pass body is pure (no I/O — population shaping happens in the boundary adapter). Algorithm: iterate source users in `SsKey`-sorted order; for each, recursively apply the strategy:
1. `ByEmail` — look up in the target population's email index; produce `Some target` or fail with `EmailDidNotMatch` (or `NoEmail` if source has no email).
2. `BySsKey` — look up in target's SsKey index; produce `Some target` or fail with `SsKeyDidNotMatch`.
3. `ManualOverride map` — `Map.tryFind sourceId map`, producing `Some target` or `OverrideMissing`.
4. `FallbackToSystemUser (fallback, primary)` — recurse into `primary`; on success, return; on failure, return `Some fallback` (annotated as fallback-applied via lineage).

Each matched user emits one `LineageEvent` with `TransformKind = Annotated "matched-by-<strategy>"`. Each unmatched user emits one `Warning` `DiagnosticEntry` with `Code = "userFkReflow.<diagnostic-variant>"`. The aggregate is wrapped in `Lineage<Diagnostics<UserRemapContext>>` per the pass return-type codification.

## §5 Consumer integration — the data triumvirate

`StaticSeedsEmitter` does not need `UserRemapContext` (static lookup data — Currency, Country, etc. — is rarely user-attributed; even when a static row carries `CreatedBy`, the model treats it as historical metadata, not a live FK). `MigrationDependenciesEmitter` and `BootstrapEmitter` both consume it. Each Π's signature gains `Profile` (per A18 amended; Π consumes `Catalog × Profile` subsets), and the `UserRemapContext` is a value attached to the EnrichedCatalog via the discovery pass (per A32).

The integration shape: at emit time, when emitting a row for a kind with one or more user-FK columns, the emitter looks up each `CreatedBy` / `UpdatedBy` value in `UserRemapContext.Mapping` and rewrites the value. If the lookup fails (the source user is in `Unmatched`), the emitter has three options: skip the row, use a sentinel, or raise a diagnostic and skip. V1's behavior (`UserMatchingResult.cs` + `EmitArtifactsStep.cs`) is "diagnostic + skip"; V2 inherits.

How does the emitter know which columns are user-FKs? Three candidates:

1. **Catalog refinement: `IsUserFk` flag on `Reference`.** Add a single boolean to `Reference` (`Catalog.fs:132-138`). The OSSYS adapter sets it when `Reference.TargetKind` resolves to the platform user kind. This is structurally clean and follows the IR-grows-under-evidence pattern (the discovery pass is the second consumer that needs the distinction; the first is the user-FK discovery itself, V1's `DiscoverUserFkCatalogStep`).
2. **Heuristic on FK target kind name.** Fragile across environments (the user table may be named differently in different deployments).
3. **Heuristic on column name.** Fragile and operator-overridable.

The right answer is (1). It is one boolean field, the OSSYS adapter resolves it deterministically (it knows the user kind by its OSSYS-platform-native SsKey, available via `extension_id` lookup), and V1's `ModelUserSchemaGraphFactory.GetSyntheticUserForeignKeys` (`src/Osm.Pipeline/UatUsers/ModelUserSchemaGraphFactory.cs`) demonstrates the same resolution at the V1 boundary. Field shape:

```fsharp
type Reference = {
    SsKey           : SsKey
    Name            : Name
    SourceAttribute : SsKey
    TargetKind      : SsKey
    OnDelete        : ReferenceAction
    IsUserFk        : bool   // new
}
```

Closed-DU expansion empirical-test holds: every existing match site over `Reference` continues to compile; every existing constructor site needs to add `IsUserFk = false`. The boundary adapter resolves the flag once.

## §6 Lineage / Diagnostics

Per the pass return-type codification, `UserFkReflowPass.run` returns `Lineage<Diagnostics<UserRemapContext>>`. Lineage events: one per matched user, `Annotated` with `"matched-by-<strategy>"` detail (e.g., `"matched-by-ByEmail"`, `"matched-by-FallbackToSystemUser"`). The PassName is `"userFkReflow"`; the SsKey on the event is the source user's SsKey; consumers reading the trail can answer "which strategy resolved this user's identity?" structurally without parsing.

Diagnostics entries: one `Warning` per unmatched user, with `Source = "userFkReflow"`, `Code = "userFkReflow.<reason>"` (e.g., `userFkReflow.emailDidNotMatch`, `userFkReflow.ssKeyDidNotMatch`, `userFkReflow.overrideMissing`, `userFkReflow.noFallbackConfigured`), `Message` carrying the source user identifier and the strategy that failed, and `Metadata = Map.ofList ["sourceUserId", "<id>"; "strategy", "<strategy-label>"]`. The opportunity stream lights up unmatched users for operator review before promotion. This mirrors `ForeignKeyPass.opportunityEntry` (`Passes/ForeignKeyPass.fs:130-211`) — heterogeneous emission across keep-reasons.

## §7 Slice-by-slice breakdown

Seven slices in dependency order. Each is one-session-shippable.

**Slice 1: `UserMatchingStrategy` DU + identity types + smart constructors.** Goal: types compile; constructors return `Result<'a>`. Test: `UserMatchingStrategyTests.fs` — property tests for `Email.create` (rejects blank, normalizes whitespace), example test for `UserMatchingStrategy.empty`. File: `src/Projection.Core/Policy.fs` (extend with new types in the Policy file, keeping the axis colocated with its DU). LOC: ~80 (types + smart constructors). Acceptance: tests green; `Policy.empty` extends with `UserMatching = ByEmail`.

**Slice 2: `UserPopulation` and `UserAttributes` in Profile.** Goal: Profile carries source/target user populations; `Profile.empty` extends. Test: `ProfileTests.fs` — `Profile.empty` has empty populations; A34 unaffected (Profile still references no Catalog/Policy types). File: `src/Projection.Core/Profile.fs`. LOC: ~50. Acceptance: every existing test in `ProfileTests` continues to pass; new tests cover empty-default and population shape.

**Slice 3: `UserRemapContext` shape + smart constructor + module accessors.** Goal: `UserRemapContext` value type with `empty`, `isFullyMapped`, `unmatchedCount`. Test: smart-constructor invariants (`Mapping.Keys ∩ Unmatched = ∅` — disjoint; structural commitment via construction validation per `AXIOMS.md` operational principle). File: `src/Projection.Core/Policy.fs` (or new `src/Projection.Core/UserRemap.fs` if Policy.fs is too crowded; argue for the latter at the second consumer). LOC: ~60. Acceptance: invariant tests green.

**Slice 4: `UserFkReflowPass.discover` minimal slice — `ByEmail` only.** Goal: pass walks source population, indexes target by email, produces `UserRemapContext` with mapping + unmatched + lineage events + diagnostics for misses. Other strategies throw `NotSupportedException` (or return an empty result with an `Error` diagnostic — depending on whether we treat "unsupported" as type-system gap or runtime decision). Test: `UserFkReflowPassTests.fs` — empty populations produce empty result with empty trail and empty diagnostics; one-source-one-matching-target produces one mapping; one-source-no-target produces one unmatched + one Warning. Property test: `T1: discover is deterministic on (sourceUsers, targetUsers, strategy)`. File: `src/Projection.Core/Passes/UserFkReflowPass.fs`. LOC: ~150. Acceptance: tests green; pass returns `Lineage<Diagnostics<UserRemapContext>>` with the codified shape.

**Slice 5: Add `BySsKey`, `ManualOverride`, `FallbackToSystemUser`.** Goal: full strategy DU coverage. Test: per-strategy worked example; composition test (`FallbackToSystemUser (target, ByEmail)` returns email matches first, then fallback for misses; lineage events distinguish). Property test: `FallbackToSystemUser` produces `Set.isEmpty Unmatched`. File: `src/Projection.Core/Passes/UserFkReflowPass.fs`. LOC: ~80 added. Acceptance: every variant has a worked test; `FallbackToSystemUser` is empirically a final-resort layer.

**Slice 6: IR refinement — `IsUserFk` flag on `Reference`.** Goal: `Reference` carries the flag; OSSYS adapter sets it; closed-DU empirical-test holds (every match site over `Reference` continues to compile; every constructor site explicitly chooses). Test: `CatalogTests.fs` — `Reference` constructor; `OsmCatalogReaderDifferentialTests.fs` — adapter resolves the flag for known user kinds. File: `src/Projection.Core/Catalog.fs`; `src/Projection.Adapters.Osm/CatalogReader.fs`. LOC: ~30 type + ~50 adapter. Acceptance: full test suite green; differential test resolves `IsUserFk` for OSSYS-native user references.

**Slice 7: Wire into MigrationDependenciesEmitter and BootstrapEmitter; multi-environment property test.** Goal: each emitter consumes `UserRemapContext` and rewrites user-FK column values. Property test (per Appendix E §E.3 `policyOrthogonal`): same `(sourceUsers, strategy)` against four different `targetUsers` produces four `UserRemapContext` values whose `Mapping.Keys` agree on the source side; differences live entirely in the `TargetUserId` values. File: `src/Projection.Targets.SSDT/MigrationDependenciesEmitter.fs`, `BootstrapEmitter.fs`. LOC: ~100 per emitter; ~40 multi-env test. Acceptance: emitters green; T11 (sibling Π's commute on shared E-attached values) holds for `UserRemapContext`.

## §8 Test strategy

Tier-1 pure property tests dominate because the pass is pure. The space:

- **Determinism (T1).** Same `(sourceUsers, targetUsers, strategy)` triple → same `UserRemapContext` byte-for-byte. FsCheck arbitrary populations.
- **`ByEmail` correctness.** All source users with email-matched targets are mapped; emails normalize via `Email.create`'s `Trim()`.
- **`BySsKey` correctness.** All source users whose SsKey appears in target are mapped.
- **`ManualOverride` overrides.** When `ManualOverride map` is the strategy, `Mapping = map` modulo source users not in the source population.
- **`FallbackToSystemUser` exhaustiveness.** Every source user is either in `Mapping` (primary matched) or has the fallback target. `Set.isEmpty Unmatched`.
- **A34: pass independence from Catalog and Policy axes other than `UserMatching`.** Same `Profile`, varying `Selection`/`Emission`/`Insertion`/`Tightening` — same `UserRemapContext`. (Encodes the orthogonality claim.)
- **Multi-environment commutativity.** `(sourceUsers, ByEmail)` against four distinct `targetUsers` populations yields four `UserRemapContext` values; the source-keyset of `Mapping` agrees across all four (modulo per-environment unmatched).
- **Diagnostic completeness.** Every unmatched source user produces exactly one `Warning` entry; the entry's metadata carries the source user identifier.
- **Per-strategy golden examples.** One curated `(sourceUsers, targetUsers, strategy)` triple per variant → one expected `UserRemapContext`. xUnit example tests; not property-driven.

## §9 V1 differential

V1's `UserMatchingResult` (`src/Osm.Pipeline/UatUsers/UserMatchingResult.cs`) shape — `(SourceUserId, TargetUserId?, Strategy, Explanation, UsedFallback)` — is the oracle. The differential test fixture loads a representative scenario (V1's existing test fixtures under `tests/Osm.Pipeline.Tests/UatUsers/UserMatchingEngineTests.cs` are the source population), runs both V1's `UserMatchingEngine.Execute` and V2's `UserFkReflowPass.discover` against equivalent inputs, projects both outputs to `Map<SourceUserId, TargetUserId option>`, and asserts equality. V1's `ExactAttribute` strategy maps to V2's `BySsKey` only when V1's configured attribute is `SsKey`; V1's `Regex` collapses into V2's `ManualOverride` (operator-supplied transformation) — these two lossy mappings are documented as `Skip` test stubs at the differential layer, naming the deliberate divergence (the "make V2 different where it should be different" discipline).

## §10 AXIOMS.md amendment for A32

A32's enforcement clause currently reads:

> *Property test.* `A32: discovered value visible to emitter`. Concretely for UAT-Users when implemented: discovery pass produces the same `UserRemapContext` regardless of which Π consumes it; both Π's agree on identity correspondences (a special case of T4).

After this chapter ships, the amendment text:

> **A32 amended (chapter 4.2 close).** The reserved space cashes out: `UserMatchingStrategy` DU lives in `Policy.fs`; `UserRemapContext` value lives at `Projection.Core` (or in `Policy.fs` colocated with the strategy axis); `UserFkReflowPass.discover` lives at `Projection.Core/Passes/UserFkReflowPass.fs`; both `MigrationDependenciesEmitter` and `BootstrapEmitter` consume the context. The property test `A32: discovered value visible to emitter` is implemented; T11's specialization for sibling Π's commuting on `UserRemapContext` is implemented as the multi-environment property test in slice 7.

The amendment lands in AXIOMS.md at chapter close, not at chapter open.

## §11 Risks

- **Identity ambiguity.** Two source users with the same email is the `ByEmail` failure mode V1 acknowledges (`UserMatchingEngine.cs` `BuildLookup` uses a `List<UserIdentifier>` per email key, accepting collisions). V2 must decide: first-match wins (deterministic but arbitrary), all-collisions-unmatched (safe but operator-burdensome), or first-match-with-Warning-emit (V2's approach; warns on collision but proceeds). Smart constructor on `UserPopulation.create` could detect duplicates and reject; argued against because environment data legitimately carries duplicate emails. The Warning is the right surface.
- **Performance.** N² matching for 100K-user populations is fatal. The pass must build a dictionary-keyed index (`Map<Email, TargetUserId>` for `ByEmail`; `Map<SsKey, TargetUserId>` for `BySsKey`) once per discover-call, not per source user. F# `Map` is O(log n); `Dictionary` in performance-critical paths if `Map` proves too slow at the 100K scale (premature; defer until a real benchmark says otherwise).
- **Operator UX for `ManualOverride`.** V1's CSV schema (`SourceUserId,TargetUserId,Rationale`; `UserMapLoader.cs:7-80`) is the inherited shape. V2's boundary adapter parses the CSV at `Projection.Adapters.UserMap` (new adapter; thin wrapper around `UserMapLoader` semantics in F#). Validation: duplicate source IDs reject (V1 rejects on duplicate, line 60-63); blank target means "no override" (allowed). The CSV path is operator-supplied via host-shell config; V2 Core sees only the resolved `Map<SourceUserId, TargetUserId>`.
- **The V1↔V2 strategy mismatch.** V1's `Regex` strategy has no V2 equivalent. The differential test must Skip-stub it explicitly (deliberate divergence, not reachability gap). If a real cutover deployment depends on Regex matching, the trigger fires and V2 grows a `RegexMatch of pattern: string * attribute: string` variant under closed-DU expansion.
- **Profile-population schema drift.** `UserPopulation.Users` is a list of `UserAttributes`; if a future strategy demands a richer attribute (phone, external ID), the type grows. Smart constructor on `UserAttributes.create` exists as the natural extension point.

## §12 Files inventory

Files modified:
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Policy.fs` — add `UserMatchingStrategy`, `UserId`, `SourceUserId`, `TargetUserId`, `Email`, extend `Policy` record, extend `Policy.empty`.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Profile.fs` — add `UserAttributes`, `UserPopulation`, extend `Profile` record, extend `Profile.empty`, extend `Profile.isEmpty`.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Catalog.fs` — add `IsUserFk : bool` to `Reference`.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs` — resolve `IsUserFk` from OSSYS-native user kind identity.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/AXIOMS.md` — A32 amendment text at chapter close.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/DECISIONS.md` — chapter-4.2 entries (UserMatching as fifth axis; UserRemapContext shape; discovery-pass codification).

Files created:
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/UserRemap.fs` — `UserRemapContext`, `RemapDiagnostic` (only if Policy.fs grows beyond comfort; else colocated).
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Passes/UserFkReflowPass.fs` — discovery pass.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.UserMap/UserMapLoader.fs` (or in `Adapters.Osm`) — CSV adapter for `ManualOverride` operator file.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/UserMatchingStrategyTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/UserRemapContextTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/UserFkReflowPassTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/UserFkReflowDifferentialTests.fs` (V1 oracle).

Files touched only for emitter integration (Slice 7):
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/MigrationDependenciesEmitter.fs` (when chapter 4.1 has shipped it).
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/BootstrapEmitter.fs` (likewise).

### Critical Files for Implementation

- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Policy.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Profile.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Catalog.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Passes/UserFkReflowPass.fs
- /home/user/outsystems-ddl-exporter/src/Osm.Pipeline/UatUsers/UserMatchingEngine.cs
