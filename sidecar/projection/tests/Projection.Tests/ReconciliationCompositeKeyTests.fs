module Projection.Tests.ReconciliationCompositeKeyTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// Slice 5 — declared/inferred COMPOSITE natural keys drive reuse-vs-insert.
// `ReconciliationStrategy.MatchByColumns` matches a Source row to a pre-
// existing Sink row by the TUPLE of several columns' values, so a transfer
// into a populated target REUSES the existing surrogate (idempotent re-apply,
// no duplicate) instead of inserting.

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %s" (es |> List.map (fun e -> e.Code) |> String.concat ", "))

let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_RC" parts |> mustOk
let private personKey = mkKey [ "Person" ]

let private row (rowTag: string) (id: string) (first: string) (last: string) : StaticRow =
    { Identifier = mkKey [ "Person"; "Row"; rowTag ]
      Values = Map.ofList [ mkName "ID", id; mkName "FIRST", first; mkName "LAST", last ] }

[<Fact>]
let ``MatchByColumns reuses the sink surrogate on a composite natural-key match`` () =
    let strategy = ReconciliationStrategy.MatchByColumns [ mkName "FIRST"; mkName "LAST" ]
    let source = [ row "s1" "1" "Ada" "Lovelace"; row "s2" "2" "Grace" "Hopper" ]
    let sink   = [ row "k1" "42" "Ada" "Lovelace"; row "k2" "43" "Alan" "Turing" ]
    let result = Reconciliation.reconcileKind personKey (mkName "ID") strategy source sink
    // Ada (source surrogate 1) reuses the existing sink surrogate 42.
    match SurrogateRemapContext.tryFindAssigned personKey (SourceKey.ofString "1") result.Remap with
    | Some (AssignedKey v) -> Assert.Equal("42", v)
    | None -> Assert.Fail "Ada should reuse sink surrogate 42 on the composite key"
    // Grace (source surrogate 2) has no composite match in the sink → Unmatched (insert).
    Assert.True(result.Unmatched |> List.exists (fun (_, sk) -> match sk with SourceKey s -> s = "2"))

[<Fact>]
let ``MatchByColumns does not match on a partial composite key`` () =
    // Same FIRST, different LAST — the whole tuple keys, so NO reuse.
    let strategy = ReconciliationStrategy.MatchByColumns [ mkName "FIRST"; mkName "LAST" ]
    let source = [ row "s1" "1" "Ada" "Lovelace" ]
    let sink   = [ row "k1" "42" "Ada" "Byron" ]
    let result = Reconciliation.reconcileKind personKey (mkName "ID") strategy source sink
    Assert.True((SurrogateRemapContext.tryFindAssigned personKey (SourceKey.ofString "1") result.Remap).IsNone)
    Assert.True(result.Unmatched |> List.exists (fun (_, sk) -> match sk with SourceKey s -> s = "1"))
