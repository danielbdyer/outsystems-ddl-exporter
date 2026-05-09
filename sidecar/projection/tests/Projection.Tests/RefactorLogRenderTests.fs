module Projection.Tests.RefactorLogRenderTests

open System.Xml
open System.Xml.Linq
open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail(sprintf "%A" err)
        Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Success v -> v
    | Failure es ->
        Assert.Fail(sprintf "%A" es)
        Unchecked.defaultof<'a>

let private nameOf (s: string) : Name =
    Name.create s |> mustResultOk

let private renamedCustomerKind : Kind =
    { customer with Name = nameOf "Patron" }

let private renamedSalesModule : Module =
    { salesModule with Kinds = [ renamedCustomerKind; order; country ] }

let private targetCatalog : Catalog =
    { Modules = [ renamedSalesModule ] }

let private renderOnce () : string =
    let diff = CatalogDiff.between sampleCatalog targetCatalog |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    RefactorLogRender.toRefactorLogXml artifact

// ---------------------------------------------------------------------------
// T1 — byte-determinism on the rendered .refactorlog. The substantive
// test of chapter 3.5's substantive deliverable: identical input
// produces identical bytes across repeat invocations. Per A35 (Π's
// canonical output is a typed deterministic stream), the realization
// layer (here, XML rendering through `XmlWriter`) inherits the
// determinism by construction — pinned writer settings + sorted-by-
// OperationKey entry order + UUIDv5-derived OperationKey + pinned
// ChangeDateTime.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: RefactorLogRender.toRefactorLogXml is byte-identical across repeat invocations`` () =
    let runs = [ for _ in 1 .. 10 -> renderOnce () ]
    let head = List.head runs
    Assert.All(runs, fun s -> Assert.Equal(head, s))

// ---------------------------------------------------------------------------
// Structural — the rendered document parses back to the SSDT-native
// shape, with expected element counts and attribute values. The XML
// structure is verified by `XDocument` (built-in BCL DOM) instead of
// substring search per the no-string-concatenation discipline.
// ---------------------------------------------------------------------------

let private operationsNamespace = XNamespace.op_Implicit "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02"

let private parseDoc (xml: string) : XDocument =
    XDocument.Parse(xml, LoadOptions.PreserveWhitespace)

[<Fact>]
let ``RefactorLogRender: root element is Operations with the SSDT namespace and Version="1.0"`` () =
    let doc = renderOnce () |> parseDoc
    let root = doc.Root
    Assert.NotNull(root)
    Assert.Equal("Operations", root.Name.LocalName)
    Assert.Equal(operationsNamespace.NamespaceName, root.Name.NamespaceName)
    let versionAttr = root.Attribute(XName.op_Implicit "Version")
    Assert.NotNull(versionAttr)
    Assert.Equal("1.0", versionAttr.Value)

[<Fact>]
let ``RefactorLogRender: one-rename diff produces exactly one Operation element`` () =
    let doc = renderOnce () |> parseDoc
    let operations =
        doc.Root.Elements(operationsNamespace + "Operation")
        |> Seq.toList
    Assert.Equal(1, List.length operations)

[<Fact>]
let ``RefactorLogRender: rename Operation carries Name="Rename Refactor" and pinned ChangeDateTime`` () =
    let doc = renderOnce () |> parseDoc
    let op =
        doc.Root.Elements(operationsNamespace + "Operation") |> Seq.head
    let nameAttr = op.Attribute(XName.op_Implicit "Name")
    let changeAttr = op.Attribute(XName.op_Implicit "ChangeDateTime")
    Assert.Equal("Rename Refactor", nameAttr.Value)
    Assert.Equal("2000-01-01T00:00:00Z", changeAttr.Value)

[<Fact>]
let ``RefactorLogRender: rename Operation Key matches UuidV5-derived OperationKey`` () =
    // Cross-check the rendered Key against the emitter's typed entry —
    // they must agree because the renderer takes the entry's
    // `OperationKey` directly.
    let diff = CatalogDiff.between sampleCatalog targetCatalog |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entry =
        artifact
        |> ArtifactByKind.toMap
        |> Map.find customerKey
        |> List.head
    let xml = RefactorLogRender.toRefactorLogXml artifact
    let doc = parseDoc xml
    let op =
        doc.Root.Elements(operationsNamespace + "Operation") |> Seq.head
    let keyAttr = op.Attribute(XName.op_Implicit "Key")
    Assert.Equal(entry.OperationKey.ToString("D"), keyAttr.Value)

[<Fact>]
let ``RefactorLogRender: rename Operation has five Property children with the expected (Name, Value) pairs`` () =
    let doc = renderOnce () |> parseDoc
    let op =
        doc.Root.Elements(operationsNamespace + "Operation") |> Seq.head
    let properties =
        op.Elements(operationsNamespace + "Property")
        |> Seq.map (fun p ->
            p.Attribute(XName.op_Implicit "Name").Value,
            p.Attribute(XName.op_Implicit "Value").Value)
        |> Seq.toList
    let expected =
        [
            "ElementName",        "[dbo].[OSUSR_S1S_CUSTOMER]"
            "ElementType",        "SqlTable"
            "ParentElementName",  "[dbo]"
            "ParentElementType",  "SqlSchema"
            "NewName",            "Patron"
        ]
    Assert.Equal<(string * string) list>(expected, properties)

// ---------------------------------------------------------------------------
// Empty-diff: no rename evidence anywhere. The rendered document is
// `<Operations>` with no children — DacFx accepts this as a no-op.
// ---------------------------------------------------------------------------

[<Fact>]
let ``RefactorLogRender: identity diff produces an Operations element with no Operation children`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog |> mustOk
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let xml = RefactorLogRender.toRefactorLogXml artifact
    let doc = parseDoc xml
    let operations =
        doc.Root.Elements(operationsNamespace + "Operation")
        |> Seq.toList
    Assert.Empty(operations)

// ---------------------------------------------------------------------------
// XML hygiene — produced bytes are valid XML, declare UTF-8 explicitly,
// and use `\n` line endings (not platform-dependent CRLF).
// ---------------------------------------------------------------------------

[<Fact>]
let ``RefactorLogRender: emitted bytes start with the UTF-8 XML declaration`` () =
    let xml = renderOnce ()
    Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml)

[<Fact>]
let ``RefactorLogRender: emitted bytes use \n line endings (not CRLF)`` () =
    let xml = renderOnce ()
    Assert.DoesNotContain("\r\n", xml)
