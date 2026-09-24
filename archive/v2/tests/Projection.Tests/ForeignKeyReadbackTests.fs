module Projection.Tests.ForeignKeyReadbackTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// E2 (debrief G4) — the cross-schema FK readback classifier. `SCHEMA_NAME()`
// returns NULL for a dropped schema or a `VIEW DEFINITION`-restricted account;
// `ForeignKeyReadback.classify` turns such a row into a NAMED diagnostic + skip,
// closing the no-silent-drop boundary axiom for the FK read leg (previously an
// opaque `GetString` cast failure / blank-and-drop). Pure Core (recon #20), so
// the discipline is witnessed without a live substrate.
// ---------------------------------------------------------------------------

module FK = ForeignKeyReadback

let private resolved =
    FK.classify (Some "dbo") (Some "Orders") (Some "CustomerId")
                (Some "dbo") (Some "Customers") (Some "Id") false

[<Fact>]
let ``E2: a fully-resolved FK row reconstructs, carrying its coordinates and trust`` () =
    match resolved with
    | FK.Reconstructable c ->
        Assert.Equal("dbo", c.SourceSchema)
        Assert.Equal("Orders", c.SourceTable)
        Assert.Equal("Customers", c.TargetTable)
        Assert.False(c.IsNotTrusted)
    | FK.Unreadable reason -> failwithf "expected Reconstructable, got Unreadable: %s" reason

[<Fact>]
let ``E2: the trust flag rides through reconstruction`` () =
    match FK.classify (Some "dbo") (Some "O") (Some "c") (Some "dbo") (Some "C") (Some "Id") true with
    | FK.Reconstructable c -> Assert.True(c.IsNotTrusted)
    | FK.Unreadable reason -> failwithf "expected Reconstructable, got Unreadable: %s" reason

[<Fact>]
let ``E2: an unreadable cross-schema FK surfaces a diagnostic, not a silent drop`` () =
    // The referenced schema is NULL (the G4 case): a dropped schema or a
    // missing VIEW DEFINITION grant. The row must NOT silently vanish — it
    // surfaces a named reason identifying the referenced side and the cause.
    let classification =
        FK.classify (Some "dbo") (Some "Orders") (Some "CustomerId")
                    None (Some "Customers") (Some "Id") false
    match classification with
    | FK.Unreadable reason ->
        Assert.Contains("referenced schema", reason)
        Assert.Contains("VIEW DEFINITION", reason)
        // The diagnostic names the visible source endpoint so the operator
        // can locate the FK (discriminating: a bare "skipped" would pass a
        // weaker assertion).
        Assert.Contains("dbo.Orders.CustomerId", reason)
    | FK.Reconstructable _ -> failwith "expected Unreadable for a NULL referenced schema"

[<Fact>]
let ``E2: a NULL parent schema names the parent side`` () =
    match FK.classify None (Some "Orders") (Some "CustomerId") (Some "dbo") (Some "Customers") (Some "Id") false with
    | FK.Unreadable reason -> Assert.Contains("parent schema", reason)
    | FK.Reconstructable _ -> failwith "expected Unreadable for a NULL parent schema"

[<Fact>]
let ``E2: a blank (whitespace) schema is unreadable, not kept as empty`` () =
    // Defends against the prior treat-as-empty path that let "" through to a
    // downstream resolution failure: a whitespace coordinate is unreadable.
    match FK.classify (Some "dbo") (Some "Orders") (Some "CustomerId") (Some "   ") (Some "Customers") (Some "Id") false with
    | FK.Unreadable _ -> ()
    | FK.Reconstructable _ -> failwith "expected Unreadable for a blank referenced schema"
