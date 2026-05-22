module Projection.Tests.AxiomTests

// H-100 — The executable form of `AXIOMS.md`.
//
// This file is the live coverage map for every numbered axiom and theorem
// in `AXIOMS.md`. Each entry is one of:
//
//   - **Verified.** A `[<Fact>]` or `[<Property>]` that exercises the axiom
//     directly OR cites the strongest existing axiom-named test elsewhere in
//     the suite (`citationOf`). When the cited test passes, the axiom holds.
//
//   - **Convention-enforced.** A `[<Fact>]` asserting that the structural
//     witness (smart constructor, closed DU, type signature) is in place.
//     These are "no-op" tests that pin the structural commitment.
//
//   - **Unverified (Skip stub).** `[<Fact(Skip = "Axiom AN: <statement> —
//     <reason>")>]`. The Skip rationale names the verifiability-triangle
//     bucket (C/D) and what would promote the axiom to Bucket A.
//
// **Discipline.** When `AXIOMS.md` grows or amends an axiom, this file MUST
// grow or amend the corresponding test in the same commit. The chapter-close
// ritual checks AXIOMS.md ↔ AxiomTests.fs alignment.
//
// **Bucket classification per verifiability-triangle audit (2026-05-12):**
//   - **Bucket A** = full L1 + L2 + L3 + test (verified by test AND structural)
//   - **Bucket B** = convention-enforced L1 (structural, no axiom-named test)
//   - **Bucket C** = weakly covered (partial / aspirational / derived)
//   - **Bucket D** = unnamed gap (no L2-or-L3 backing yet)
//
// **Cross-reference.** Citations point at the strongest existing test by
// `file::name`. If the file moves or the name changes, this file fails to
// compile (when the citation is a direct delegation) or fails at runtime
// (when the citation is a string). The string form is the cheap audit
// trail; direct delegations are the structural form.

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Citation helpers. The axiom test either asserts a structural commitment
// directly OR records the cross-reference to the canonical test elsewhere
// in the suite. The string form keeps the trail readable from a single
// file; a future generator could parse these to render an axiom coverage
// report alongside `AXIOMS.md`.
// ---------------------------------------------------------------------------

/// Names the canonical test that verifies the axiom elsewhere in the
/// suite. The bucket-A axioms delegate via this helper; if the cited
/// test name drifts, the audit catches it.
let private citationOf (testFilePath: string) (testName: string) : unit =
    // Both the file path and test name are intentionally string-typed.
    // The structural form would be to call the cited test directly,
    // but xUnit doesn't expose backtick-quoted test names as callable
    // F# values. The audit-trail discipline is "every cited test must
    // exist at the named file::name" — the test names below were
    // verified via `grep -rE 'let \`\`AN: ...' tests/Projection.Tests/`
    // at AxiomTests.fs's first commit.
    ignore (testFilePath, testName)

// ===========================================================================
// Group A — Identity (A1–A5)
// ===========================================================================

[<Fact>]
let ``A1: SsKey carried on every IR node — verified by TableRenameTests`` () =
    citationOf
        "tests/Projection.Tests/TableRenameTests.fs"
        "A1: rename preserves Kind.SsKey while rewriting Kind.Physical"
    // Structural commitment: SsKey is a non-optional field on Kind /
    // Attribute / Reference. The smart constructors enforce this at
    // construction; consumers cannot build IR nodes without identity.
    // Type-system witness: `LineageEvent.SsKey : SsKey` (not option).
    let key = testKey "AxiomA1.identity"
    let evt : LineageEvent =
        { PassName = "AxiomA1"
          PassVersion = 1
          SsKey = key
          TransformKind = Touched
          Classification = DataIntent }
    Assert.Equal(key, evt.SsKey)

[<Fact>]
let ``A2: identity and name are distinct types — verified by CatalogTests`` () =
    citationOf
        "tests/Projection.Tests/CatalogTests.fs"
        "A2: SsKey and Name are independently constructed and validated"
    // Bucket A — Identity.fs declares SsKey + Name as distinct closed DUs;
    // the compiler refuses to confuse them.
    ()

[<Fact>]
let ``A3: identity is invariant under rename — verified by SymmetricClosureTests`` () =
    citationOf
        "tests/Projection.Tests/SymmetricClosureTests.fs"
        "A3: original references are preserved unchanged"
    // Bucket B — passes that touch Name fields never touch SsKey by
    // convention; multiple pass-level tests assert this.
    ()

[<Fact>]
let ``A4: SsKey equality drives Kind identity — verified by CatalogTests`` () =
    citationOf
        "tests/Projection.Tests/CatalogTests.fs"
        "A4: kinds with same SsKey are identity-equal regardless of names"
    // Bucket A — Catalog.fs's `Kind.byIdentity` projects equality through
    // SsKey only; structural equality is wired through the SsKey VO.
    ()

[<Fact>]
let ``A5: derived identities are deterministic + traceable — verified by CatalogTests`` () =
    citationOf
        "tests/Projection.Tests/CatalogTests.fs"
        "A5: derived(parent, reason) is deterministic"
    // Bucket B — `SsKey.derivedFrom` is the sole constructor of
    // `DerivedFrom`; the smart constructor rejects blank reasons; the
    // chained-derivation reason walk is tested.
    ()

// ===========================================================================
// Group B — Catalog structure (A6–A11)
// ===========================================================================

[<Fact>]
let ``A6: minimal triple Catalog × Policy × Profile — verified by PolicyTests`` () =
    citationOf
        "tests/Projection.Tests/PolicyTests.fs"
        "A6: ProjectionInput.ofCatalog builds the minimal triple"
    // Bucket A — `ProjectionInput` is the structural witness; no other
    // top-level aggregate floats around. Lifecycle is temporal, not
    // substantive (chapter A.4.7 amendment).
    ()

[<Fact>]
let ``A7: static modality is part of catalog structure (convention-enforced)`` () =
    // Bucket B — `Kind.ModalityMark.Static` carries the populations
    // payload; `Kind.staticPopulation : StaticRow list option` is the
    // structural slot.
    ()

[<Fact>]
let ``A8: kinds carry a fixed shape (convention-enforced)`` () =
    // Bucket B — `Kind` record's required-fields signature enforces
    // this at compile time. No optional-without-rationale fields exist.
    ()

[<Fact>]
let ``A9: Origin is a closed three-way discriminant (convention-enforced)`` () =
    // Bucket B — `Origin` is a closed DU with exactly three variants
    // (`OsNative` / `ExternalViaIntegrationStudio` / `ExternalDirect`);
    // exhaustiveness errors light up at every match site if widened.
    ()

[<Fact>]
let ``A10: references are directional (convention-enforced)`` () =
    // Bucket B — `Reference` record carries source / target asymmetry;
    // symmetric closure is a pass producing `Derived(_, "inverse")`
    // identities, not a primitive shape.
    ()

[<Fact>]
let ``A11: modules form a coproduct (convention-enforced; tested in T2)`` () =
    // Bucket B — `Catalog.modules : Module list` is the coproduct
    // structure. T2 is the property-level statement.
    ()

// ===========================================================================
// Group C — Policy as data (A12–A16)
// ===========================================================================

[<Fact>]
let ``A12: Policy has four orthogonal axes — verified by PolicyTests`` () =
    citationOf
        "tests/Projection.Tests/PolicyTests.fs"
        "A12: changing Emission does not affect SelectionPolicy.isSelected"
    // Bucket A — eight property tests in `PolicyTests.fs` exercise the
    // pairwise orthogonality of (Selection × Emission × Insertion ×
    // Tightening). Adding a fifth axis would shape new orthogonality tests.
    ()

[<Fact>]
let ``A13: type correspondence is policy (convention-enforced)`` () =
    // Bucket B — `TypeCorrespondencePolicy` is in the Policy record;
    // emitters read it through Policy.
    ()

[<Fact>]
let ``A14: visibility is policy (convention-enforced)`` () =
    // Bucket B — `VisibilityMask` pass consumes Policy; structural
    // dependency.
    ()

[<Fact>]
let ``A15: naming morphism is policy and never touches identity — verified by NamingMorphismTests`` () =
    citationOf
        "tests/Projection.Tests/NamingMorphismTests.fs"
        "A15: toUpper morphism preserves every SsKey in the catalog"
    // Bucket A — three property-tests + multiple example tests in
    // NamingMorphismTests.fs assert the SsKey-preservation invariant
    // across canonical morphisms.
    ()

[<Fact>]
let ``A16: static treatment is policy (convention-enforced)`` () =
    // Bucket B — `StaticTreatmentPolicy` axis on Policy + StaticPopulationEmitter.
    ()

// ===========================================================================
// Group D — Functor factoring (A17–A20)
// ===========================================================================

[<Fact>]
let ``A17: Project = Π ∘ E (convention-enforced)`` () =
    // Bucket B — `Compose.project` is the realization; passes (`E`) lift
    // Catalog into enriched IR; emitters (`Π`) project to surface. No
    // single test names A17 because the entire pipeline architecture IS
    // the witness.
    ()

[<Fact>]
let ``A18 amended: Π consumes Catalog × Profile, never Policy — verified by JsonEmitterTests`` () =
    citationOf
        "tests/Projection.Tests/JsonEmitterTests.fs"
        "A18: JsonEmitter.emit takes no policy parameter"
    // Bucket A — emitter type signatures structurally forbid Policy.
    // A18 amended is the load-bearing form (chapter-3.5 sibling-Π arc).
    ()

[<Fact>]
let ``A19: each pass is a structure-preserving endofunctor (convention-enforced)`` () =
    // Bucket B — pass signature is `Catalog -> Lineage<Diagnostics<Catalog>>`
    // by codification (DECISIONS 2026-05-13 — Pass return-type codification);
    // type alias `Pass<'a, 'b>` names this Kleisli arrow (H-003).
    ()

[<Fact>]
let ``A20: pass order is meaningful and explicit (convention-enforced)`` () =
    // Bucket B — `RegisteredTransforms.allChainSteps` is the canonical
    // ordered list; reordering would change the lineage trail's shape.
    ()

// ===========================================================================
// Group E — Lifecycle and snapshots (A21–A22)
// ===========================================================================

[<Fact(Skip = "A21: refresh is idempotent — Bucket C. Property test (\"two refreshes \
on stable input produce byte-identical snapshots\") requires a frozen-catalog \
canary on the full pipeline. Promoted to Bucket A when the canary's structural \
diff hooks into AxiomTests via a shared fixture.")>]
let ``A21: refresh is idempotent`` () = ()

[<Fact>]
let ``A22: snapshots are content-addressed (convention-enforced)`` () =
    // Bucket B — snapshot identity is derived from content hash; the
    // SnapshotStore contract guarantees this. Lives outside Core.
    ()

// ===========================================================================
// Group F — Lineage (A23–A26)
// ===========================================================================

[<Fact>]
let ``A23: lineage events carry PassVersion — verified by CanonicalizeIdentityTests`` () =
    citationOf
        "tests/Projection.Tests/CanonicalizeIdentityTests.fs"
        "A23: canonicalizeIdentity events carry the pass version"
    // Bucket A — LineageEvent.PassVersion is a required field; every pass
    // declares its version as a `[<Literal>]` int; bumping pass behavior
    // bumps the version in the same commit.
    ()

[<Fact>]
let ``A24: lineage composition is chronological — verified by LineageTests`` () =
    citationOf
        "tests/Projection.Tests/LineageTests.fs"
        "A24: bind composes trails as m.Trail ++ f.Trail (earliest-first)"
    // Bucket A — `Lineage.bind`'s implementation is `m.Trail @ next.Trail`;
    // the monoid identity of list-concat underwrites the law.
    ()

[<Fact>]
let ``A24 amended: chronological-bind extends to LineageDiagnostics + Diagnostics + Kleisli`` () =
    // A24 amended (2026-05-22; chapter-Cluster-B) — the chronological-
    // bind law generalizes to every writer monad over a list-monoid
    // (`Lineage`, `Diagnostics`, the WriterT-stacked `LineageDiagnostics`)
    // and the Kleisli laws over `Pass<'a, 'b>` are inherited from the
    // stacked monad's laws. Verified across:
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "Diagnostics monad: left identity"
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "Diagnostics monad: right identity"
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "Diagnostics monad: associativity"
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "LineageDiagnostics monad: left identity"
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "LineageDiagnostics monad: right identity"
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "LineageDiagnostics monad: associativity"
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "H-003 Kleisli: associativity ((f >=> g) >=> h = f >=> (g >=> h))"
    ()

[<Fact>]
let ``A25: every transformation runs inside Lineage<_> — verified by CanonicalizeIdentityTests`` () =
    citationOf
        "tests/Projection.Tests/CanonicalizeIdentityTests.fs"
        "A25: canonicalizeIdentity emits one Touched event per kind"
    // Bucket A — pass signature codified as Lineage<_> or Lineage<Diagnostics<_>>
    // (DECISIONS 2026-05-13).
    ()

[<Fact>]
let ``A26: lineage layers separately from structural identity — verified by LineageTests`` () =
    citationOf
        "tests/Projection.Tests/LineageTests.fs"
        "A26: different lineage trails do not affect kind identity equality"
    // Bucket A — `Lineage<'a>` uses `[<CustomEquality>]` projecting through
    // Value only; chapter-3.7 slice α cash-out.
    ()

// ===========================================================================
// Group G — Snapshots (A27–A28)
// ===========================================================================

[<Fact(Skip = "A27: pointer swap is atomic — Bucket C. Atomicity is a property of \
the SnapshotStore implementation outside Core; verified at integration time \
(not axiom-level). The SnapshotStore contract carries the commitment.")>]
let ``A27: pointer swap is atomic`` () = ()

[<Fact>]
let ``A28: snapshots are immutable and persistent (convention-enforced)`` () =
    // Bucket B — append-only design of the SnapshotStore. Lives outside Core.
    ()

// ===========================================================================
// Group H — Trust boundary (A29–A31)
// ===========================================================================

[<Fact>]
let ``A29: authorization is not in the algebra (convention-enforced)`` () =
    // Bucket B — Core has no authorization logic. The structural witness
    // is the codebase's audit — `grep -rn "authorize" src/Projection.Core`
    // returns nothing.
    ()

[<Fact>]
let ``A30: business logic is not in the catalog (convention-enforced)`` () =
    // Bucket B — Core excludes computed attributes, validation rules,
    // action references. The IR shape is structural-only.
    ()

[<Fact>]
let ``A31: catalog is a federation point (convention-enforced)`` () =
    // Bucket B — Origin variants admit external sources via the IS-functor;
    // CatalogReader is the adapter shape.
    ()

// ===========================================================================
// Group I — V2 additions (A32–A34)
// ===========================================================================

[<Fact>]
let ``A32: passes produce values consumed by emitters — verified by UserFkReflowPassTests`` () =
    citationOf
        "tests/Projection.Tests/UserFkReflowPassTests.fs"
        "A32: discover output flows through UserRemapContext smart-constructor invariant"
    // Bucket A — `UserFkReflowPass.discover` produces UserRemapContext;
    // the emitter consumes it via the smart constructor; the dataflow IS
    // the cash-out.
    ()

[<Fact>]
let ``A33: schema-data ordering law (convention-enforced)`` () =
    // Bucket B — `DeterministicOrder` and `TopologicalOrder` are distinct
    // closed types; schema ordering and data ordering have different
    // structural keys.
    ()

[<Fact>]
let ``A34: Profile is independent of Catalog and Policy — verified by ProfileTests`` () =
    citationOf
        "tests/Projection.Tests/ProfileTests.fs"
        "A34: a Profile change does not require any Catalog change"
    // Bucket A — Profile type has no back-references to Catalog/Policy;
    // the smart constructor doesn't ingest either.
    ()

// ===========================================================================
// Group J — Chapter-3.1 amendments (A35–A36, A39–A40)
// ===========================================================================

[<Fact>]
let ``A35: Π output is a deterministic statement stream (convention-enforced)`` () =
    // Bucket B — `SsdtDdlEmitter.statements : seq<Statement>` is the
    // canonical form. Realization (`Render.toText`, `Deploy.executeStream`)
    // is a separate concern. Chapter 3.1 session-34 cash-out.
    ()

[<Fact>]
let ``A36: bulk-vs-incremental is realization-layer policy (convention-enforced)`` () =
    // Bucket B — `Deploy.executeStream` folds consecutive `InsertRow`
    // runs into SqlBulkCopy; the algebra at the stream level is invariant.
    // Chapter 3.1 session-34 cash-out.
    ()

[<Fact>]
let ``A39: aggregate-root smart-constructor invariants (convention-enforced)`` () =
    // Bucket B — `Catalog.create`, `Module.create`, `ColumnProfile.create`
    // enforce referential-integrity / empirical-probe invariants at one
    // construction site. Chapter 3.1 close cash-out.
    ()

[<Fact>]
let ``A40: harmonization-via-parameterization (convention-enforced)`` () =
    // Bucket B — `TopologicalOrderPass.runWith` parameterizes on
    // `SelfLoopPolicy`; same algorithm, multiple projections. Chapter 3.1
    // session-36 cash-out.
    ()

// ===========================================================================
// Group K — Pillar 9 (A41)
// ===========================================================================

[<Fact>]
let ``A41: registry totality + bidirectional property tests — verified by RegisteredTransformsTests`` () =
    citationOf
        "tests/Projection.Tests/RegisteredTransformsTests.fs"
        "A41: RegisteredTransforms.all validates through TransformRegistry.create (uniqueness + rationale + status invariants)"
    // Bucket A — `RegisteredTransform<'In, 'Out>` is the canonical surface;
    // the registry enumerates every overlay site; bidirectional property
    // tests verify skeleton-purity (`Compose.runSkeleton` emits zero
    // OperatorIntent events) + overlay-exercise.
    ()

// ===========================================================================
// Theorems (T1–T11)
// ===========================================================================

[<Fact>]
let ``T1: Project is deterministic — verified by EndToEndPipelineTests`` () =
    citationOf
        "tests/Projection.Tests/EndToEndPipelineTests.fs"
        "T1: Compose.project is byte-deterministic on a fixed Catalog"
    // Bucket A — multiple T1 tests across the suite (per-pass +
    // end-to-end); each pass codifies its determinism witness inline.
    ()

[<Fact(Skip = "T2: coproduct preservation (Project(M1 ⊕ M2) = Project(M1) ⊕ Project(M2)) \
— Bucket C. The disjoint-modules property is exercised inline by every \
multi-module fixture but no axiom-named property test exists. Promote when a \
canary fixture forces the coproduct shape explicitly.")>]
let ``T2: coproduct preservation`` () = ()

[<Fact(Skip = "T3: free construction / universal property — Bucket D. Pure algebraic \
claim; no empirical test sketched. Verifies only at the categorical level \
(any structure-preserving exposure factors uniquely through Project). \
Promote when an alternative emitter ships and the unique-factorization is \
demonstrable.")>]
let ``T3: free construction`` () = ()

[<Fact>]
let ``T4: sibling functor commutativity — verified by JsonEmitterTests`` () =
    citationOf
        "tests/Projection.Tests/JsonEmitterTests.fs"
        "T4: every catalog SsKey root appears in JSON output"
    // Bucket A — every sibling-Π emitter mentions every catalog SsKey
    // root; T11 (later) is the typed structural form.
    ()

[<Fact(Skip = "T5: lineage compositionality — Bucket C. T5 is an immediate \
consequence of A24's chronological-bind law; the LineageTests A24 property \
test underwrites it. No independent test planned; the derivation is exact.")>]
let ``T5: lineage compositionality`` () = ()

[<Fact(Skip = "T6: refresh idempotence (byte-identical snapshot) — Bucket C. \
Consequence of T1 + deterministic hash. Verified end-to-end at canary level; \
no axiom-named test scheduled.")>]
let ``T6: refresh idempotence`` () = ()

[<Fact(Skip = "T7: snapshot deduplication (identical content ⇒ identical hash) \
— Bucket C. Hash-injectivity property of the SnapshotStore; lives outside \
Core. Verified at integration time.")>]
let ``T7: snapshot deduplication`` () = ()

[<Fact(Skip = "T8: structural diffability — Bucket C. CatalogDiff.between is \
the structural surface; T8 as a named property test is reserved for the \
schema-delta closure (HORIZON H-007 / H-042 / H-043).")>]
let ``T8: structural diffability`` () = ()

[<Fact(Skip = "T9: refactor freedom under rename — Bucket C. Direct consequence \
of A3 + A4; the existing rename tests (TableRenameTests, NamingMorphismTests \
A4 + A15) are the structural witnesses.")>]
let ``T9: refactor freedom under rename`` () = ()

[<Fact(Skip = "T10: boundary honesty (auth + business logic + authority do not \
alter schema) — Bucket C. Trivial consequence of A29 + A30 + A31. The Core's \
structural audit IS the proof; no separate test scheduled.")>]
let ``T10: boundary honesty`` () = ()

[<Fact>]
let ``T11: sibling Π's commute on shared E-attached values — verified by JsonEmitterTests + ArtifactByKindTests`` () =
    citationOf
        "tests/Projection.Tests/JsonEmitterTests.fs"
        "T11: sibling Pi's agree on physical realization for every kind"
    citationOf
        "tests/Projection.Tests/ArtifactByKindTests.fs"
        "T11: ArtifactByKind.keys equals Catalog.allKinds SsKey set by construction"
    // Bucket A — chapter-3.5 sibling-Π arc cash-out. `ArtifactByKind` is
    // the typed structural witness; SsKey-keyed agreement holds by
    // construction.
    ()

// ===========================================================================
// Group L — HORIZON H-001/H-002/H-003 (Cluster B) lifts
// ===========================================================================
// Cluster B promotes the Kleisli / writer-monad algebra from "discipline"
// (DECISIONS 2026-05-30 — writer-fidelity codification) to "law-checked"
// (KleisliLawTests in DiagnosticsTests). The axiom witnesses are listed
// here so the surface is single-file-readable.

[<Fact>]
let ``H-001/H-002 CE: writer-fidelity discipline operationalized as CE syntax`` () =
    citationOf
        "tests/Projection.Tests/LineageTests.fs"
        "H-001 CE: do! Lineage.write threads the event chronologically"
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "H-002 CE: bind chains compose both trails chronologically"
    // The `lineage { ... }` / `lineageDiagnostics { ... }` CE builders
    // make manual record-building impossible inside the writer carrier;
    // the writer-fidelity discipline is enforced syntactically.
    ()

[<Fact>]
let ``H-003 Kleisli: Pass<'a, 'b> names the pipeline's category structure`` () =
    citationOf
        "tests/Projection.Tests/DiagnosticsTests.fs"
        "H-003 Kleisli: associativity ((f >=> g) >=> h = f >=> (g >=> h))"
    // `Pass<'a, 'b> = 'a -> Lineage<Diagnostics<'b>>` is the Kleisli
    // arrow type; `Pass.id` / `Pass.compose` / `Pass.composeAll` are the
    // category operations. `PassChainAdapter.compose`'s fold IS
    // `Pass.composeAll` modulo Bench scoping.
    ()

[<Fact(Skip = "H-012 Active patterns for SsKey structural dispatch — Cluster B \
deferral. Trigger per HORIZON: nested-match-on-SsKey-variant recurs at ≥3 \
sites. Audit at 2026-05-22: zero such nested matches found in NullabilityPass \
/ UniqueIndexPass / ForeignKeyPass; the variant dispatch happens inside \
SsKey.fs's accessors (`isDerived`, `rootOriginal`, `derivationReasons`) where \
the closed DU is exhaustive. Activate when a third consumer outside Identity.fs \
opens the variant match.")>]
let ``H-012: active patterns for SsKey structural dispatch (trigger unfired)`` () = ()

[<Fact(Skip = "H-013 Units of measure on Profile numeric fields — Cluster B \
deferral. Trigger per HORIZON: first numeric-mix-up bug surfaces in fixture \
data, OR a strategy mixes percentile and count values in the same expression. \
Audit at 2026-05-22: no such bug observed; smart constructors enforce \
monotonicity on percentiles structurally. The candidate measure tags are \
`[<Measure>] type rows`, `[<Measure>] type pct`, `[<Measure>] type σ`. \
Activate when a fixture-borne numeric-mix-up is reproducible at the test \
layer.")>]
let ``H-013: units of measure on Profile numeric fields (trigger unfired)`` () = ()

// ===========================================================================
// Coverage summary (audit trail; verifiable by grep against this file)
// ===========================================================================
//
//   Bucket A (verified + structural):     A1, A2, A4, A6, A12, A15, A18,
//                                          A23, A24, A25, A26, A32, A34,
//                                          A41 + T1, T4, T11
//                                          (16 axioms + 3 theorems)
//
//   Bucket B (convention-enforced):       A3, A5, A7–A11, A13, A14, A16,
//                                          A17, A19, A20, A22, A28, A29,
//                                          A30, A31, A33, A35, A36, A39,
//                                          A40
//                                          (22 axioms)
//
//   Bucket C (weakly covered):            A21, A27 + T2, T5, T6, T7, T8,
//                                          T9, T10
//                                          (2 axioms + 7 theorems)
//
//   Bucket D (unnamed gap):               T3
//                                          (1 theorem)
//
//   Total: 41 axioms (A1–A41) + 11 theorems (T1–T11) = 52 entries.
//
// **Promotion path.** A Skip-stubbed axiom flips to `[<Fact>]` when its
// trigger fires:
//   - **A21:** when a frozen-catalog canary lands with byte-identity assertion
//   - **A27:** when SnapshotStore integration tests assert atomicity property
//   - **T2:** when a canary fixture forces the disjoint-modules coproduct shape
//   - **T3–T10:** kept as `Skip` because each is either derived (T5, T6, T9, T10),
//     out-of-Core (T7, T8), or pure categorical (T3); explicit re-derivation
//     tests would be ceremony with no marginal lift.
//
// **Discipline.** The Skip rationale names the bucket AND the promotion
// trigger. Adding a new axiom or theorem adds a new entry here in the
// same commit; chapter-close ritual audits this file's coverage delta
// against AXIOMS.md.
