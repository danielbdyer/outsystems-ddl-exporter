module Projection.Tests.CompareTests

open Xunit
open Projection.Core
open Projection.Pipeline

// `Fixtures.mustOk` is `let private` (B6); local copy.
let private mustOk = function Ok x -> x | Error e -> failwithf "expected Ok, got %A" e

let private emptyCat : Catalog = Catalog.create [] [] |> mustOk
let private op (label: string) (cat: Catalog) : Compare.Operand =
    { Label = label; Catalog = cat; Profile = None }

[<Fact>]
let ``NM-71 compare: identical catalogs are compatible, zero schema norm, and render a roll-up`` () =
    let r = Compare.compute (op "A" emptyCat) (op "B" emptyCat)
    Assert.True(r.SchemaDelta.IsSome)
    Assert.Equal(0, Compare.schemaNorm r)
    Assert.True(Compare.isCompatible r)
    Assert.NotEmpty(Compare.render r)

[<Fact>]
let ``NM-71 compare: the JSON lens carries the operand labels and the compatible verdict`` () =
    let json = Compare.toJsonString (Compare.compute (op "envA" emptyCat) (op "envB" emptyCat))
    Assert.Contains("envA", json)
    Assert.Contains("envB", json)
    Assert.Contains("compatible", json)

[<Fact>]
let ``NM-71 compare: a pure emit operand has no data evidence (dealbreakers advisory-silent)`` () =
    let r = Compare.compute (op "A" emptyCat) (op "B" emptyCat)
    Assert.False(r.DataEvidenceAvailable)
    Assert.Empty(r.DataDealbreakers)

[<Fact>]
let ``NM-71 compare: the verb parses to Intent.Compare carrying both operand refs`` () =
    let cfg = ProjectionConfig.parse "" |> mustOk
    match Command.parse cfg [ "compare"; "@runA"; "@runB" ] with
    | Ok (Intent.Compare args) -> Assert.Equal<string list>([ "@runA"; "@runB" ], args)
    | other -> Assert.Fail(sprintf "expected Intent.Compare, got %A" other)
