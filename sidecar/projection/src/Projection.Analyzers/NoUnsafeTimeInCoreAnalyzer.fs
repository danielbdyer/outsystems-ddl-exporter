module Projection.Analyzers.NoUnsafeTimeInCoreAnalyzer

open System
open FSharp.Analyzers.SDK
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// Per CLAUDE.md "F#-pure-core / no-I/O-in-Core" load-bearing
/// commitment + the operating-disciplines table entry for
/// "Determinism is constructed, not validated":
///
///     "No `DateTime.Now`, `Random`, or I/O in Core — the boundary
///      supplies clock values; passes consume them. T1 byte-
///      determinism holds because every choice supports it."
///
/// Grep-based Rule 27 catches obvious `DateTime.Now` literals; AST
/// detection complements where grep misfires:
///   - `open System` + `DateTime.Now` (unqualified after open).
///   - Indirection via `let x = System.DateTime.Now in x`.
///   - Reference inside a nested module's expression.
///
/// The analyzer walks the untyped AST for `SynExpr.LongIdent` /
/// `SynExpr.Ident` references whose long-id ends with the forbidden
/// member, AND whose containing file path is under `src/Projection
/// .Core/`. Sites outside Core (adapters, Pipeline, CLI, tests) are
/// allowed — Core's purity discipline doesn't extend to the boundary.

[<Literal>]
let internal AnalyzerName : string = "Projection001NoUnsafeTimeInCore"

[<Literal>]
let internal AnalyzerShortDescription : string =
    "Detects non-deterministic time/randomness primitives in Projection.Core."

[<Literal>]
let internal AnalyzerHelpUri : string =
    "https://github.com/danielbdyer/outsystems-ddl-exporter/blob/main/sidecar/projection/CLAUDE.md#load-bearing-commitments"

[<Literal>]
let internal MessageCode : string = "PRJ001"

/// Forbidden BCL members. Each entry is the **suffix** of a long-id
/// chain; the analyzer matches when the AST's last two parts equal the
/// pair. Both qualified (`System.DateTime.Now`) and short
/// (`DateTime.Now` under `open System`) forms match.
let private forbiddenSuffixes : (string * string) list =
    [
        "DateTime", "Now"
        "DateTime", "UtcNow"
        "DateTime", "Today"
        "Guid",     "NewGuid"
        "Random",   "Shared"
    ]

let private matchesForbiddenSuffix (parts: string list) : (string * string) option =
    match List.rev parts with
    | member' :: type' :: _ ->
        forbiddenSuffixes
        |> List.tryFind (fun (t, m) -> t = type' && m = member')
    | _ -> None

let private isCoreFile (fileName: string) : bool =
    // Cross-platform path discrimination: forward slashes on Linux /
    // macOS; backslashes on Windows. The path segment we look for is
    // `Projection.Core` between separators.
    let normalized = fileName.Replace('\\', '/')
    normalized.Contains "/Projection.Core/"

let private longIdToParts (lid: LongIdent) : string list =
    lid |> List.map (fun id -> id.idText)

/// Walk a single `SynExpr` and collect forbidden references inside it.
let rec private collectForbidden (expr: SynExpr) : (range * string * string) list =
    match expr with
    | SynExpr.LongIdent (_, SynLongIdent (lid, _, _), _, range) ->
        match matchesForbiddenSuffix (longIdToParts lid) with
        | Some (t, m) -> [ range, t, m ]
        | None -> []
    | SynExpr.App (_, _, funcExpr, argExpr, _) ->
        collectForbidden funcExpr @ collectForbidden argExpr
    | SynExpr.Sequential (_, _, e1, e2, _, _) ->
        collectForbidden e1 @ collectForbidden e2
    | SynExpr.LetOrUse (_, _, bindings, body, _, _) ->
        let fromBindings =
            bindings |> List.collect (fun (SynBinding (_, _, _, _, _, _, _, _, _, body, _, _, _)) -> collectForbidden body)
        fromBindings @ collectForbidden body
    | SynExpr.Lambda (_, _, _, body, _, _, _) -> collectForbidden body
    | SynExpr.Match (_, expr, clauses, _, _) ->
        let fromClauses =
            clauses
            |> List.collect (fun (SynMatchClause (_, _, body, _, _, _)) -> collectForbidden body)
        collectForbidden expr @ fromClauses
    | SynExpr.IfThenElse (cond, thenBranch, elseBranch, _, _, _, _) ->
        collectForbidden cond
        @ collectForbidden thenBranch
        @ (elseBranch |> Option.map collectForbidden |> Option.defaultValue [])
    | SynExpr.Tuple (_, exprs, _, _) ->
        exprs |> List.collect collectForbidden
    | SynExpr.Paren (inner, _, _, _) -> collectForbidden inner
    | SynExpr.Typed (inner, _, _) -> collectForbidden inner
    | SynExpr.ArrayOrList (_, elements, _) -> elements |> List.collect collectForbidden
    | SynExpr.ArrayOrListComputed (_, inner, _) -> collectForbidden inner
    | SynExpr.ComputationExpr (_, inner, _) -> collectForbidden inner
    | SynExpr.Do (inner, _) -> collectForbidden inner
    | SynExpr.For (_, _, _, _, e1, _, e2, body, _) ->
        collectForbidden e1 @ collectForbidden e2 @ collectForbidden body
    | SynExpr.ForEach (_, _, _, _, _, e, body, _) ->
        collectForbidden e @ collectForbidden body
    | SynExpr.While (_, cond, body, _) ->
        collectForbidden cond @ collectForbidden body
    | SynExpr.TryWith (tryExpr, clauses, _, _, _, _) ->
        let fromClauses =
            clauses
            |> List.collect (fun (SynMatchClause (_, _, body, _, _, _)) -> collectForbidden body)
        collectForbidden tryExpr @ fromClauses
    | SynExpr.TryFinally (tryExpr, finallyExpr, _, _, _, _) ->
        collectForbidden tryExpr @ collectForbidden finallyExpr
    | SynExpr.Record (_, _, fields, _) ->
        fields
        |> List.collect (fun (SynExprRecordField (_, _, valueExpr, _)) ->
            match valueExpr with
            | Some e -> collectForbidden e
            | None -> [])
    | _ -> []

let rec private collectFromBindings (bindings: SynBinding list) : (range * string * string) list =
    bindings
    |> List.collect (fun (SynBinding (_, _, _, _, _, _, _, _, _, body, _, _, _)) ->
        collectForbidden body)

let rec private collectFromDecl (decl: SynModuleDecl) : (range * string * string) list =
    match decl with
    | SynModuleDecl.Let (_, bindings, _) -> collectFromBindings bindings
    | SynModuleDecl.Expr (expr, _) -> collectForbidden expr
    | SynModuleDecl.NestedModule (_, _, decls, _, _, _) ->
        decls |> List.collect collectFromDecl
    | _ -> []

let private collectFromModule (SynModuleOrNamespace (_, _, _, decls, _, _, _, _, _)) =
    decls |> List.collect collectFromDecl

let private buildMessage (range: range) (typeName: string) (memberName: string) : Message =
    {
        Type = AnalyzerName
        Message =
            $"Projection.Core forbids non-deterministic primitives. Found `{typeName}.{memberName}`. Per CLAUDE.md load-bearing commitments: Core is sync + deterministic; the boundary (adapters / Pipeline) supplies clock + randomness values. Move this call to an adapter and pass the value into Core via a parameter."  // LINT-ALLOW: terminal analyzer-diagnostic text-emission boundary; the `Message.Message` field IS the free-form string the FSharp.Analyzers.SDK contract surfaces to IDE/CLI consumers; segments are typed (typeName + memberName are F# compiler-service `Ident.idText` strings); no diagnostic-message DSL exists for this consumer
        Code = MessageCode
        Severity = Severity.Error
        Range = range
        Fixes = []
    }

[<CliAnalyzer(AnalyzerName, AnalyzerShortDescription, AnalyzerHelpUri)>]
let cliAnalyzer : Analyzer<CliContext> =
    fun ctx ->
        async {
            if not (isCoreFile ctx.FileName) then
                return []
            else
                let parseTree = ctx.ParseFileResults.ParseTree
                let findings =
                    match parseTree with
                    | ParsedInput.ImplFile (ParsedImplFileInput (_, _, _, _, _, modules, _, _, _)) ->
                        modules |> List.collect collectFromModule
                    | ParsedInput.SigFile _ -> []
                return
                    findings
                    |> List.map (fun (range, typeName, memberName) ->
                        buildMessage range typeName memberName)
        }

[<EditorAnalyzer(AnalyzerName, AnalyzerShortDescription, AnalyzerHelpUri)>]
let editorAnalyzer : Analyzer<EditorContext> =
    fun ctx ->
        async {
            if not (isCoreFile ctx.FileName) then
                return []
            else
                let parseTree = ctx.ParseFileResults.ParseTree
                let findings =
                    match parseTree with
                    | ParsedInput.ImplFile (ParsedImplFileInput (_, _, _, _, _, modules, _, _, _)) ->
                        modules |> List.collect collectFromModule
                    | ParsedInput.SigFile _ -> []
                return
                    findings
                    |> List.map (fun (range, typeName, memberName) ->
                        buildMessage range typeName memberName)
        }
