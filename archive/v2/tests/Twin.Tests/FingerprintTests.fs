module Twin.Tests.FingerprintTests

open Xunit
open Twin.Core

// ---------------------------------------------------------------------------
// THE_TWIN.md law 1 (convergence) rests on the fingerprint: deterministic,
// order-insensitive over contributions, sensitive to every input that
// changes what a materialization produces.
// ---------------------------------------------------------------------------

let private contribution (name: string) (content: string) : Fingerprint.Contribution =
    { Name = name; Content = content }

let private files =
    [ contribution "Modules/Sales/dbo.Order.sql" "CREATE TABLE [dbo].[Order] (Id INT);"
      contribution "Modules/Crm/dbo.Customer.sql" "CREATE TABLE [dbo].[Customer] (Id INT);" ]

[<Fact>]
let ``the same inputs produce the same fingerprint`` () =
    let a = Fingerprint.compute "0.1.0" "default" 7UL files
    let b = Fingerprint.compute "0.1.0" "default" 7UL files
    Assert.Equal(Fingerprint.value a, Fingerprint.value b)

[<Fact>]
let ``contribution order does not matter`` () =
    let a = Fingerprint.compute "0.1.0" "default" 7UL files
    let b = Fingerprint.compute "0.1.0" "default" 7UL (List.rev files)
    Assert.Equal(Fingerprint.value a, Fingerprint.value b)

[<Fact>]
let ``a content edit changes the fingerprint`` () =
    let a = Fingerprint.compute "0.1.0" "default" 7UL files
    let edited =
        files |> List.map (fun c ->
            if c.Name.EndsWith "Order.sql" then { c with Content = c.Content + " -- widened" } else c)
    let b = Fingerprint.compute "0.1.0" "default" 7UL edited
    Assert.NotEqual<string>(Fingerprint.value a, Fingerprint.value b)

[<Fact>]
let ``seed, scenario, and tool version each change the fingerprint`` () =
    let baseline = Fingerprint.compute "0.1.0" "default" 7UL files
    Assert.NotEqual<string>(Fingerprint.value baseline, Fingerprint.value (Fingerprint.compute "0.1.0" "default" 8UL files))
    Assert.NotEqual<string>(Fingerprint.value baseline, Fingerprint.value (Fingerprint.compute "0.1.0" "quarter-end" 7UL files))
    Assert.NotEqual<string>(Fingerprint.value baseline, Fingerprint.value (Fingerprint.compute "0.2.0" "default" 7UL files))

[<Fact>]
let ``a stored fingerprint round-trips for comparison`` () =
    let a = Fingerprint.compute "0.1.0" "default" 7UL files
    match Fingerprint.ofStored (Fingerprint.value a) with
    | Some restored -> Assert.Equal(Fingerprint.value a, Fingerprint.value restored)
    | None -> failwith "a computed fingerprint must rehydrate"

[<Fact>]
let ``a blank stored fingerprint reads as no materialization`` () =
    Assert.True((Fingerprint.ofStored "").IsNone)
    Assert.True((Fingerprint.ofStored "   ").IsNone)
