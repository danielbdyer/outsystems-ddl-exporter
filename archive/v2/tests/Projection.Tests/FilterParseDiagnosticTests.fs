module Projection.Tests.FilterParseDiagnosticTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// Chapter 4.6 slice γ — Filter-parse Diagnostic emission helper.
//
// V1 reference: Osm.Smo/PerTableEmission/IndexScriptBuilder.cs:403-419
// (ParsePredicate; V1's silent-skip on parse failure). V2 lifts the same
// parse path to TSql160Parser (chapter 4.5 slice α) + chapter 4.6 slice γ
// adds Diagnostics emission for parse failures.
//
// Per chapter 4.6 open Q3 — Diagnostic shape:
//   Source = "emitter:ssdt"
//   Code = "emit.ssdt.index.filterParseFailure"
//   Severity = Warning
//   Metadata carries raw filter string + parser error count
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice γ: empty filter string yields Some None with empty diagnostics`` () =
    let result = ScriptDomBuild.tryParseFilterWithDiagnostics ""
    Assert.True (Option.isNone result.Value)
    Assert.Empty result.Entries

[<Fact>]
let ``Slice γ: whitespace-only filter yields Some None with empty diagnostics`` () =
    let result = ScriptDomBuild.tryParseFilterWithDiagnostics "   \t  "
    Assert.True (Option.isNone result.Value)
    Assert.Empty result.Entries

[<Fact>]
let ``Slice γ: valid filter yields Some expression with empty diagnostics`` () =
    let result = ScriptDomBuild.tryParseFilterWithDiagnostics "[IsActive] = 1"
    Assert.True (Option.isSome result.Value)
    Assert.Empty result.Entries

[<Fact>]
let ``Slice γ: malformed filter yields None with one Warning Diagnostic`` () =
    let result = ScriptDomBuild.tryParseFilterWithDiagnostics "NOT A VALID FILTER ((("
    Assert.True (Option.isNone result.Value)
    Assert.Single result.Entries |> ignore
    let entry = result.Entries |> List.head
    Assert.Equal (DiagnosticSeverity.Warning, entry.Severity)

[<Fact>]
let ``Slice γ: Diagnostic carries the canonical Source + Code per chapter 4.6 open Q3`` () =
    let result = ScriptDomBuild.tryParseFilterWithDiagnostics "NOT A VALID FILTER ((("
    let entry = result.Entries |> List.head
    Assert.Equal ("emitter:ssdt", entry.Source)
    Assert.Equal ("emit.ssdt.index.filterParseFailure", entry.Code)

[<Fact>]
let ``Slice γ: Diagnostic Metadata carries the raw filter string and the parser error count`` () =
    let raw = "NOT A VALID FILTER ((("
    let result = ScriptDomBuild.tryParseFilterWithDiagnostics raw
    let entry = result.Entries |> List.head
    Assert.Equal (raw, entry.Metadata.["raw"])
    // errorCount is a non-empty numeric string.
    let errorCountStr = entry.Metadata.["errorCount"]
    Assert.False (System.String.IsNullOrWhiteSpace errorCountStr)
    let errorCount = System.Int32.Parse errorCountStr
    Assert.True (errorCount > 0, sprintf "expected non-zero error count, got %d" errorCount)

[<Fact>]
let ``Slice γ: Diagnostic SsKey is None (filter-parse failure is not kind-scoped at this surface)`` () =
    let result = ScriptDomBuild.tryParseFilterWithDiagnostics "garbage )))"
    let entry = result.Entries |> List.head
    Assert.True (Option.isNone entry.SsKey)

[<Fact>]
let ``Slice γ: T1 determinism — same input yields same Diagnostics value`` () =
    let r1 = ScriptDomBuild.tryParseFilterWithDiagnostics "[Status] = N'A'"
    let r2 = ScriptDomBuild.tryParseFilterWithDiagnostics "[Status] = N'A'"
    Assert.Equal (Option.isSome r1.Value, Option.isSome r2.Value)
    Assert.Equal (r1.Entries.Length, r2.Entries.Length)

[<Fact>]
let ``Slice γ: composability — Diagnostics.bind chains parse with downstream emit decisions`` () =
    // Verify the writer composes per the Diagnostics.bind contract:
    // failing parse propagates the Warning entry through the
    // composition; successful parse keeps the entry list empty.
    let parseAndCount (raw: string) : Diagnostics<int> =
        ScriptDomBuild.tryParseFilterWithDiagnostics raw
        |> Diagnostics.bind (fun opt ->
            match opt with
            | Some _ -> Diagnostics.ofValue 1
            | None   -> Diagnostics.ofValue 0)
    let success = parseAndCount "[IsActive] = 1"
    Assert.Equal (1, success.Value)
    Assert.Empty success.Entries

    let failure = parseAndCount "malformed ((("
    Assert.Equal (0, failure.Value)
    Assert.Single failure.Entries |> ignore
