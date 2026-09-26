module Projection.Analyzers.NoUnsafeTimeInCoreAnalyzer

open System
open FSharp.Analyzers.SDK
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

/// Per CLAUDE.md "F#-pure-core / no-I/O-in-Core" load-bearing commitment + the
/// operating-disciplines entry for "Determinism is constructed, not validated":
///
///     "No `DateTime.Now`, `Random`, or I/O in Core — the boundary supplies clock
///      values; passes consume them. T1 byte-determinism holds because every choice
///      supports it."
///
/// **Recon #6 — typed-tree rewrite (the XL).** The predecessor matched the last two
/// parts of a long-id against a hand-list of forbidden `(type, member)` suffixes on
/// the UNTYPED parse tree. That is fundamentally name-shaped: it cannot catch a
/// constructor (`new Random(seed)` is `SynExpr.New`, not a member access), it cannot
/// catch `task { }` / `async { }` (the "no Task/Async in Core" half of the law had
/// ZERO coverage), and — worst — it cannot tell the project's OWN `Environment` DU
/// (`Projection.Core.Environment.Dev`, used all over `Transfer.fs`) from
/// `System.Environment`, so a suffix rule for env access would false-positive.
///
/// This rewrite walks the TYPED tree (`CheckFileResults.GetAllUsesOfAllSymbolsInFile`)
/// and bans *capabilities by resolved full name*, not strings: any symbol whose
/// declaring type resolves to `System.Random` / `System.Diagnostics.Stopwatch` /
/// `System.Environment` / `System.Threading.Tasks.Task` / `Microsoft.FSharp.Control
/// .FSharpAsync` / `System.IO.*`, plus the clock members `System.DateTime.{Now,
/// UtcNow,Today}` / `System.DateTimeOffset.{Now,UtcNow}` / `System.Guid.NewGuid`.
/// Constructors, CEs, `Random.Shared` and `new Random` all resolve to the same
/// entity, so all are caught; the project's own same-named types are not, because
/// their full name differs. The two sanctioned Core consumers are allowlisted:
/// `Bench` (the one module-level mutable + the only `Stopwatch`) and `PinnedWriting`
/// (the codec's `System.IO` writer types) — both named in CLAUDE.md.
///
/// Core consumes time/identity VALUES (a `DateTimeOffset` parameter, a `Guid` field)
/// — only the value-PRODUCING members are banned, never the types themselves.

[<Literal>]
let internal AnalyzerName : string = "Projection001NoUnsafeTimeInCore"

[<Literal>]
let internal AnalyzerShortDescription : string =
    "Detects non-deterministic time/randomness/IO/async primitives in Projection.Core."

[<Literal>]
let internal AnalyzerHelpUri : string =
    "https://github.com/danielbdyer/outsystems-ddl-exporter/blob/main/sidecar/projection/CLAUDE.md#load-bearing-commitments"

[<Literal>]
let internal MessageCode : string = "PRJ001"

/// Strip a generic-arity backtick suffix (`Task`1` → `Task`).
let private stripArity (fullName: string) : string =
    match fullName.IndexOf '`' with
    | -1 -> fullName
    | i  -> fullName.Substring(0, i)

/// The forbidden capability a resolved symbol names — a `(capability, detail)` pair
/// for the diagnostic — or `None`. Resolution is by the symbol's declaring-type full
/// name (members) or the type's own full name (entities), so a same-named project
/// type is distinguished from the BCL one.
let private forbiddenCapability (sym: FSharpSymbol) : (string * string) option =
    let owningType, memberName =
        match sym with
        | :? FSharpMemberOrFunctionOrValue as m ->
            (m.DeclaringEntity |> Option.bind (fun e -> e.TryFullName)), m.DisplayName
        | :? FSharpEntity as e ->
            e.TryFullName, ""
        | _ -> None, ""
    match owningType with
    | None -> None
    | Some raw ->
        let tfn = stripArity raw
        match tfn, memberName with
        | "System.DateTime", ("Now" | "UtcNow" | "Today")          -> Some ("DateTime", memberName)
        | "System.DateTimeOffset", ("Now" | "UtcNow")              -> Some ("DateTimeOffset", memberName)
        | "System.Guid", "NewGuid"                                 -> Some ("Guid", "NewGuid")
        | "System.Random", _                                       -> Some ("Random", memberName)  // LINT-ALLOW: the analyzer's banned-API detection table NAMES "System.Random" to FLAG it — a terminal symbol-name match, not a use of Random; no typed surface exists for a resolved-symbol-name table
        | "System.Diagnostics.Stopwatch", _                        -> Some ("Stopwatch", memberName)
        | "System.Environment", _                                  -> Some ("Environment", memberName)
        | "System.Threading.Tasks.Task", _                         -> Some ("Task", memberName)
        | "Microsoft.FSharp.Control.FSharpAsync", _                -> Some ("Async", memberName)
        | _ when tfn.StartsWith("System.IO.", StringComparison.Ordinal) ->
            Some ("System.IO", tfn.Substring "System.IO.".Length)  // the type's short name, e.g. "File"
        | _ -> None

/// The sanctioned per-file exceptions named in CLAUDE.md: `Bench` is the one
/// module-level mutable and the only `Stopwatch` consumer (the measurement
/// substrate); `PinnedWriting` holds the codec's `System.IO` stream-writer types.
let private isSanctioned (fileName: string) (capability: string) : bool =
    let f = fileName.Replace('\\', '/')
    match capability with
    | "Stopwatch" -> f.EndsWith("/Bench.fs", StringComparison.Ordinal)
    | "System.IO" -> f.EndsWith("/PinnedWriting.fs", StringComparison.Ordinal)
    | _           -> false

let private isCoreFile (fileName: string) : bool =
    // Cross-platform path discrimination; the segment is `Projection.Core` between
    // separators. Sites outside Core (adapters, Pipeline, CLI, tests) are allowed.
    (fileName.Replace('\\', '/')).Contains "/Projection.Core/"

let private buildMessage (range: range) (capability: string) (detail: string) : Message =
    let what = if String.IsNullOrEmpty detail || detail = capability then capability else $"{capability}.{detail}"  // LINT-ALLOW: terminal analyzer-diagnostic capability text; the FSharp.Analyzers.SDK Message field IS a free-form string (sibling of the :109 marker), no diagnostic-message DSL exists for this consumer
    {
        Type = AnalyzerName
        Message =
            $"Projection.Core forbids non-deterministic / impure primitives. Found `{what}`. Per CLAUDE.md load-bearing commitments: Core is sync + deterministic + I/O-free; the boundary (adapters / Pipeline) supplies clock + randomness values and owns I/O and Task/Async. Move this to an adapter and pass values into Core via parameters."  // LINT-ALLOW: terminal analyzer-diagnostic text-emission boundary; the `Message.Message` field IS the free-form string the FSharp.Analyzers.SDK contract surfaces to IDE/CLI consumers; segments are typed (capability + detail are resolved-symbol strings); no diagnostic-message DSL exists for this consumer
        Code = MessageCode
        Severity = Severity.Error
        Range = range
        Fixes = []
    }

/// The shared detection over the TYPED tree — used by both the CLI and editor
/// analyzers (both contexts carry a `FSharpCheckFileResults`).
let private findings (fileName: string) (checkResults: FSharpCheckFileResults) : Message list =
    if not (isCoreFile fileName) then
        []
    else
        checkResults.GetAllUsesOfAllSymbolsInFile()
        |> Seq.choose (fun (su: FSharpSymbolUse) ->
            match forbiddenCapability su.Symbol with
            | Some (capability, detail) when not (isSanctioned fileName capability) ->
                Some (buildMessage su.Range capability detail)
            | _ -> None)
        |> Seq.toList

[<CliAnalyzer(AnalyzerName, AnalyzerShortDescription, AnalyzerHelpUri)>]
let cliAnalyzer : Analyzer<CliContext> =
    fun ctx -> async { return findings ctx.FileName ctx.CheckFileResults }

[<EditorAnalyzer(AnalyzerName, AnalyzerShortDescription, AnalyzerHelpUri)>]
let editorAnalyzer : Analyzer<EditorContext> =
    fun ctx ->
        async {
            return
                match ctx.CheckFileResults with
                | Some checkResults -> findings ctx.FileName checkResults
                | None              -> []
        }
