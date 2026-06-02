module Projection.Tests.PreflightTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline

// 6.B.1 — pure (DB-free) witnesses for the Decision↔Data pre-flight. The
// LiveProfiler null-count evidence is the cache; the tightening overlay is the
// Decision. `dataViolatesTightening` flags each EnforceNotNull column whose
// source data carries NULLs — the coupling that would otherwise crash the
// two-phase load mid-write. The Docker witness (TransferCanaryTests) drives the
// same decision against a live source via `tighteningPreflight`.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private attrKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_BTEST_ATTR" [ s ] |> mustOk
let private kindKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_BTEST_KIND" [ s ] |> mustOk

let private cacheWith (kindK: SsKey) (nullCounts: (SsKey * int64) list) : EvidenceCache =
    let ck : CachedKind =
        { KindKey      = kindK
          RowCount     = 10L
          NullCounts   = Map.ofList nullCounts
          Columns      = []
          ColumnsByKey = Map.empty }
    { Kinds = Map.ofList [ kindK, ck ] }

[<Fact>]
let ``6.B.1: EnforceNotNull on a NULL-bearing column is a tightening violation`` () =
    let noteK = attrKey "Note"
    let tK = kindKey "Ticket"
    let cache = cacheWith tK [ noteK, 3L ]
    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton noteK }
    match Preflight.dataViolatesTightening cache overlay with
    | [ v ] ->
        Assert.Equal<SsKey>(noteK, v.AttributeKey)
        Assert.Equal<SsKey>(tK, v.KindKey)
        Assert.Equal(3L, v.NullCount)
    | other -> Assert.Fail(sprintf "expected exactly one violation, got %A" other)

[<Fact>]
let ``6.B.1: EnforceNotNull on a column with zero NULLs is not a violation`` () =
    let noteK = attrKey "Note"
    let cache = cacheWith (kindKey "Ticket") [ noteK, 0L ]
    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton noteK }
    Assert.Empty(Preflight.dataViolatesTightening cache overlay)

[<Fact>]
let ``6.B.1: a NULL-bearing column NOT tightened is not a violation`` () =
    let noteK = attrKey "Note"
    let cache = cacheWith (kindKey "Ticket") [ noteK, 5L ]
    // Empty overlay — the operator did not tighten this column.
    Assert.Empty(Preflight.dataViolatesTightening cache DecisionOverlay.empty)

[<Fact>]
let ``6.B.1: violations are deterministic across attributes (sorted by identity)`` () =
    let tK = kindKey "Ticket"
    let a = attrKey "Alpha"
    let b = attrKey "Beta"
    let cache = cacheWith tK [ a, 1L; b, 2L ]
    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.ofList [ a; b ] }
    let names =
        Preflight.dataViolatesTightening cache overlay
        |> List.map (fun v -> SsKey.rootOriginal v.AttributeKey)
    Assert.Equal<string list>(List.sort names, names)
