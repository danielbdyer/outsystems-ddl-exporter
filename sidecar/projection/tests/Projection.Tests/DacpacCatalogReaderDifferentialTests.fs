module Projection.Tests.DacpacCatalogReaderDifferentialTests

open System.IO
open Xunit
open Microsoft.SqlServer.Dac
open Microsoft.SqlServer.Dac.Model
open Projection.Core
open Projection.Adapters.Dacpac

// ---------------------------------------------------------------------------
// Differential test for the DACPAC catalog adapter (chapter 3, session 27 —
// first substantive slice of the canary chapter).
//
// The contract: V2's `Projection.Adapters.Dacpac.CatalogReader.parse`
// consumes DACPAC bytes (zip-of-XML produced by SSDT or by
// DacpacEmitter when it lands at slice 5+) and produces a V2 `Catalog`.
//
// **T1 amended (2026-05-23) — the algebraic equality.** For binary
// Π's, T1 holds at the projection language's normal form: the
// loaded representation under DacFx's parser. This test enforces
// that contract by:
//   1. building a TSqlModel via DacFx's `AddObjects(script)`;
//   2. emitting DACPAC bytes via `BuildPackage(stream, model, metadata)`;
//   3. parsing those bytes via the read-side adapter;
//   4. asserting the parsed Catalog equals the hand-built expected one.
// Byte-equality is NOT asserted (timestamps embedded in Origin.xml).
//
// **Slice-1 scope.** One Module ("Pipeline" placeholder per axis 7),
// one Kind, two Attributes (PK + nullable Name). No FKs, no indexes,
// no modality. Subsequent slices extend.
// ---------------------------------------------------------------------------

let private mkKey s = SsKey.original s |> Result.value
let private mkName s = Name.create s |> Result.value

let private pipelineModuleKey = mkKey "OS_MOD_Pipeline"
let private userKindKey       = mkKey "OS_KIND_Pipeline_User"
let private userIdAttrKey     = mkKey "OS_ATTR_Pipeline_User_Id"
let private userNameAttrKey   = mkKey "OS_ATTR_Pipeline_User_Name"

/// Build a DACPAC byte[] from one or more T-SQL scripts. DacFx's
/// `AddObjects` consumes script text; `BuildPackage` writes to a stream.
let private buildDacpacBytes (scripts: string list) : byte[] =
    use model = new TSqlModel(SqlServerVersion.Sql160, TSqlModelOptions())
    for script in scripts do
        model.AddObjects(script)
    let metadata = PackageMetadata()
    metadata.Name <- "PipelineFixture"
    metadata.Description <- "Slice-1 hermetic fixture for the DACPAC read-side adapter."
    metadata.Version <- "1.0.0"
    use stream = new MemoryStream()
    DacPackageExtensions.BuildPackage(stream, model, metadata)
    stream.ToArray()

// Slice-1 fixture: single User table with PK Id and nullable Name.
let private slice1Scripts : string list =
    [
        """
CREATE TABLE [dbo].[User]
(
    [Id]   INT          NOT NULL,
    [Name] NVARCHAR(200) NULL,
    CONSTRAINT [PK_User] PRIMARY KEY ([Id])
);
"""
    ]

let private expectedSlice1Catalog : Catalog =
    let userKind : Kind =
        { SsKey    = userKindKey
          Name     = mkName "User"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "User" }
          Attributes = [
              { SsKey        = userIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "Id"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
              { SsKey        = userNameAttrKey
                Name         = mkName "Name"
                Type         = Text
                Column       = { ColumnName = "Name"; IsNullable = true }
                IsPrimaryKey = false
                IsMandatory  = false }
          ]
          References = []
          Indexes    = [] }
    { Modules = [
        { SsKey = pipelineModuleKey
          Name  = mkName "Pipeline"
          Kinds = [ userKind ] } ] }

[<Fact>]
let ``differential: slice-1 DACPAC bytes parse into the expected V2 Catalog (T1 amended — loaded form)`` () =
    let bytes = buildDacpacBytes slice1Scripts
    let result = CatalogReader.parse (CatalogReader.DacpacBytes bytes)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got Result.Failure with %d error(s): %A"
                errors.Length
                errors)
    | Success actual ->
        Assert.Equal<Catalog>(expectedSlice1Catalog, actual)

[<Fact>]
let ``T1 amended (loaded form): same triple round-trips identically through DacFx parse`` () =
    // Build twice and parse both; the loaded representation is invariant
    // even though the bytes may differ (timestamps in Origin.xml).
    let firstBytes  = buildDacpacBytes slice1Scripts
    let secondBytes = buildDacpacBytes slice1Scripts
    let firstParse  = CatalogReader.parse (CatalogReader.DacpacBytes firstBytes)
    let secondParse = CatalogReader.parse (CatalogReader.DacpacBytes secondBytes)
    match firstParse, secondParse with
    | Success a, Success b -> Assert.Equal<Catalog>(a, b)
    | _ ->
        Assert.Fail(
            sprintf
                "Both parses should succeed; got %A and %A"
                firstParse
                secondParse)
