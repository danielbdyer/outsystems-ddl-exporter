module Projection.Tests.SliceApplyTests

open Xunit
open Projection.Core
open Projection.Targets.Json
open Projection.Pipeline
open Projection.Tests.Fixtures

// Slice 7 — the data-portability APPLY/RESET emission. `SliceApplyRun.emit` is
// PURE (catalog × golden × deleteScope → T-SQL), so the whole apply/reset core
// — including the delete-scope SAFETY — is testable without a database.
//
// Fixture: Country ← User ← Order, where ONLY Order carries the `REGION`
// column. A reset delete-scope on `REGION` must therefore add the
// `WHEN NOT MATCHED BY SOURCE … DELETE` arm to Order ALONE — the pulled-in
// parents (User, Country) carry no REGION, so `DeleteScopePolicy.resolveFor`
// yields no arm for them. Bounded blast radius, by construction.

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %s" (es |> List.map (fun e -> e.Code) |> String.concat ", "))

let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_SA" parts |> mustOk

let private col (keyParts: string list) (name: string) (isPk: bool) : Attribute =
    { Attribute.create (mkKey keyParts) (mkName name) Integer with
        Column = ColumnRealization.create name false |> Result.value
        IsPrimaryKey = isPk
        IsMandatory = true }

let private textCol (keyParts: string list) (name: string) : Attribute =
    { Attribute.create (mkKey keyParts) (mkName name) Text with
        Column = ColumnRealization.create name false |> Result.value
        IsMandatory = true }

let private mkKind (keyParts: string list) (name: string) (table: string) (attrs: Attribute list) (refs: Reference list) : Kind =
    { SsKey = mkKey keyParts
      Name = mkName name
      Origin = Native
      Modality = []
      Physical = mkTableId "dbo" table
      Attributes = attrs
      References = refs
      Indexes = []
      Description = None
      IsActive = true
      Triggers = []
      ColumnChecks = []
      ExtendedProperties = [] }

let private countryKey = mkKey [ "Country" ]
let private userKey    = mkKey [ "User" ]
let private orderKey   = mkKey [ "Order" ]

let private countryKind =
    mkKind [ "Country" ] "Country" "OSUSR_SA_COUNTRY"
        [ col [ "Country"; "ID" ] "ID" true ] []

let private userKind =
    mkKind [ "User" ] "User" "OSUSR_SA_USER"
        [ col [ "User"; "ID" ] "ID" true; col [ "User"; "COUNTRY_ID" ] "COUNTRY_ID" false ]
        [ { Reference.create (mkKey [ "User"; "CountryRef" ]) (mkName "CountryRef") (mkKey [ "User"; "COUNTRY_ID" ]) countryKey with
              ConstraintState = ConstraintState.TrustedConstraint } ]

let private orderKind =
    mkKind [ "Order" ] "Order" "OSUSR_SA_ORDER"
        [ col [ "Order"; "ID" ] "ID" true
          col [ "Order"; "USER_ID" ] "USER_ID" false
          textCol [ "Order"; "REGION" ] "REGION" ]
        [ { Reference.create (mkKey [ "Order"; "UserRef" ]) (mkName "UserRef") (mkKey [ "Order"; "USER_ID" ]) userKey with
              ConstraintState = ConstraintState.TrustedConstraint } ]

let private catalog : Catalog =
    { Modules =
        [ { SsKey = mkKey [ "Module" ]; Name = mkName "M"
            Kinds = [ countryKind; userKind; orderKind ]; IsActive = true; ExtendedProperties = [] } ]
      Sequences = [] }

let private golden : GoldenDataset =
    { Version = 1
      Entities =
        [ { Entity = "Country"; Rows = [ Map.ofList [ mkName "ID", "10" ] ] }
          { Entity = "User";    Rows = [ Map.ofList [ mkName "ID", "100"; mkName "COUNTRY_ID", "10" ] ] }
          { Entity = "Order"
            Rows = [ Map.ofList [ mkName "ID", "1000"; mkName "USER_ID", "100"; mkName "REGION", "West" ] ] } ] }

let private occurrences (needle: string) (haystack: string) : int =
    if needle = "" then 0
    else (haystack.Length - haystack.Replace(needle, "").Length) / needle.Length

[<Fact>]
let ``slice-apply additive emit produces a MERGE artifact and no DELETE arm`` () =
    let sql = (SliceApplyRun.emit catalog golden None |> mustOk).ToUpperInvariant()
    Assert.Contains("MERGE", sql)
    Assert.Contains("OSUSR_SA_ORDER", sql)
    Assert.DoesNotContain("NOT MATCHED BY SOURCE", sql)   // additive — never prunes

[<Fact>]
let ``slice-reset delete-scope adds the DELETE arm to the root kind ALONE`` () =
    let scope : DeleteScopePolicy = { Terms = [ { Column = "REGION"; Value = "West" } ] }
    let sql = (SliceApplyRun.emit catalog golden (Some scope) |> mustOk).ToUpperInvariant()
    // The authoritative prune appears — but exactly once: only Order carries
    // REGION, so User/Country are never pruned (the bounded blast radius).
    Assert.Contains("NOT MATCHED BY SOURCE", sql)
    Assert.Equal(1, occurrences "NOT MATCHED BY SOURCE" sql)

[<Fact>]
let ``slice-apply refuses a golden whose entity is absent from the target (schema parity)`` () =
    let alien : GoldenDataset =
        { Version = 1; Entities = [ { Entity = "Nonexistent"; Rows = [ Map.ofList [ mkName "ID", "1" ] ] } ] }
    match SliceApplyRun.emit catalog alien None with
    | Ok _     -> Assert.Fail "expected a schema-parity refusal for an unknown entity"
    | Error es -> Assert.Equal("slice.schemaParity", (List.head es).Code)

[<Fact>]
let ``slice-apply refuses a golden column absent from the target (schema parity)`` () =
    let extraCol : GoldenDataset =
        { Version = 1
          Entities = [ { Entity = "Country"; Rows = [ Map.ofList [ mkName "ID", "10"; mkName "GHOST", "x" ] ] } ] }
    match SliceApplyRun.emit catalog extraCol None with
    | Ok _     -> Assert.Fail "expected a schema-parity refusal for an unknown column"
    | Error es -> Assert.Equal("slice.schemaParity", (List.head es).Code)
