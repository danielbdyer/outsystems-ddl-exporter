module Projection.Tests.SqlIdentifierTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT

// recon #8 — `SqlIdentifier.quote` is the Core-reachable equivalent of ScriptDom's
// `Identifier.EncodeIdentifier` (which Core / OperationalDiagnostics / the
// `LogicalColumnEmission` pass cannot call). These pin the equivalence as LAW: the
// hand-written `]`-doubling quoter must agree byte-for-byte with the vendor
// primitive (reached here via SSDT's `Render.quote`) across the identifier class —
// INCLUDING `]`-bearing names, the exact case the prior inline bracket-literals got
// wrong.

[<Theory>]
[<InlineData("Customer")>]
[<InlineData("OSUSR_abc_Order")>]
[<InlineData("Foo]Bar")>]
[<InlineData("]]weird[[")>]
[<InlineData("a]b]c")>]
[<InlineData("")>]
[<InlineData("space name")>]
[<InlineData("dbo")>]
[<InlineData("已经")>]
let ``SqlIdentifier.quote agrees byte-for-byte with ScriptDom EncodeIdentifier (via Render.quote)`` (s: string) =
    Assert.Equal(Render.quote s, SqlIdentifier.quote s)

[<Fact>]
let ``SqlIdentifier.quote doubles embedded ] per T-SQL`` () =
    Assert.Equal("[Foo]]Bar]", SqlIdentifier.quote "Foo]Bar")
    Assert.Equal("[Customer]", SqlIdentifier.quote "Customer")
    Assert.Equal("[a]]b]]c]", SqlIdentifier.quote "a]b]c")

[<Fact>]
let ``SqlIdentifier.qualified brackets both segments, dot-joined, ]-escaped`` () =
    Assert.Equal("[dbo].[Order]", SqlIdentifier.qualified "dbo" "Order")
    Assert.Equal("[a]]b].[c]]d]", SqlIdentifier.qualified "a]b" "c]d")
    // Agrees with the vendor primitive applied per-segment.
    Assert.Equal(Render.quote "a]b" + "." + Render.quote "c]d", SqlIdentifier.qualified "a]b" "c]d")
