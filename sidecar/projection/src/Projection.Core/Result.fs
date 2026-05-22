namespace Projection.Core

/// A validation failure carrying a stable code, a human-readable message,
/// and optional metadata. Mirrors the shape of
/// `src/Osm.Domain/Abstractions/Result.cs::ValidationError` in the trunk so
/// error-code conventions stay aligned across the boundary.
///
/// Codes follow the trunk's `category.subject.problem` lower-dot convention,
/// e.g. `"sskey.empty"`, `"attribute.dataType.invalid"`.
type ValidationError = {
    Code     : string
    Message  : string
    Metadata : Map<string, string option>
}

/// Construction helpers for `ValidationError`. Mirrors the trunk's static
/// `Create` factories.
[<RequireQualifiedAccess>]
module ValidationError =

    let private requireNonBlank (paramName: string) (value: string) : unit =
        if System.String.IsNullOrWhiteSpace value then
            invalidArg paramName "must be provided."

    /// Build a `ValidationError` with no metadata.
    let create (code: string) (message: string) : ValidationError =
        requireNonBlank "code" code
        requireNonBlank "message" message
        { Code = code; Message = message; Metadata = Map.empty }

    /// Build a `ValidationError` with structured metadata. Per chapter
    /// 3.5 deep audit (2026-05-09): the data-structure-oriented form —
    /// the message is a *static phrase* (no interpolation); the
    /// dynamic values flow into `Metadata` as typed key-value pairs.
    let createWithMetadata
        (code: string)
        (message: string)
        (metadata: Map<string, string option>)
        : ValidationError =
        requireNonBlank "code" code
        requireNonBlank "message" message
        { Code = code; Message = message; Metadata = metadata }

    /// Attach (or replace) a single metadata entry.
    let withMetadata (key: string) (value: string option) (e: ValidationError) : ValidationError =
        if System.String.IsNullOrWhiteSpace key then e
        else { e with Metadata = e.Metadata |> Map.add key value }

    /// Replace the human-readable message while preserving code and metadata.
    let withMessage (message: string) (e: ValidationError) : ValidationError =
        requireNonBlank "message" message
        { e with Message = message }


/// Validation combinators for aggregate-root smart constructors.
/// Compresses recurring `groupBy + filter > 1 + map error` and
/// `if Set.contains then [] else [error]` shapes into named primitives.
///
/// **Algebraic content.** Validation errors aggregate (per the
/// `Result<'a, ValidationError list>` shape) — these primitives
/// produce ValidationError lists that the smart constructor
/// concatenates via `@` and inspects via `List.isEmpty`. No new
/// algebra; the combinators name the recurring shapes.
///
/// **Why this primitive earns its place.** The `Catalog.create` smart
/// constructor carried 70+ LOC of duplicate-key-detection boilerplate
/// across module / kind / sequence partitions; the recurring shape
/// (`groupBy + List.filter (>1) + List.map (fst >> error)`) ran at
/// 5+ sites with identical structure. Lifting to `duplicateKeyErrors`
/// collapses the boilerplate to one-line calls without changing
/// behavior.
[<RequireQualifiedAccess>]
module Validation =

    /// For a list of items, return one `ValidationError` per duplicated
    /// key. `keySelector` extracts the identity-bearing field;
    /// `msgOf` builds the per-duplicate-key human message. The error's
    /// code is shared across the duplicates (one code, many messages).
    ///
    /// **Algebra.** Equivalent to `xs |> List.groupBy keySelector
    /// |> List.choose (fun (k, group) -> if List.length group > 1
    /// then Some (ValidationError.create code (msgOf k)) else None)`.
    /// Stable order: keys appear in the order of first occurrence in
    /// the input.
    ///
    /// **Worked example (Catalog.create — chapter-Cluster-B compression).**
    /// Module / Kind / Sequence duplicate-key checks share this shape;
    /// the primitive collapses each from ~10 LOC of inline boilerplate
    /// to a single line.
    let duplicateKeyErrors
        (code: string)
        (msgOf: 'k -> string)
        (keySelector: 'a -> 'k)
        (items: 'a list)
        : ValidationError list =
        items
        |> List.groupBy keySelector
        |> List.choose (fun (key, group) ->
            if List.length group > 1 then
                Some (ValidationError.create code (msgOf key))
            else None)


/// `Result<'a>` is a type alias for `FSharp.Core.Result<'a, ValidationError list>`.
/// Chapter-3.6 cash-out of the user's "be bold" directive (2026-05-09):
/// the prior custom DU (`Success of 'a | Failure of ValidationError list`) is
/// replaced by the FSharp.Core canonical type so:
///   - `FsToolkit.ErrorHandling`'s `result {}` / `taskResult {}` /
///     `validation {}` CEs work natively
///   - F# convention alignment (Ok / Error are the canonical case names)
///   - Future devs immediately recognize the shape
///   - Coexists with `Result<'a, EmitError>` (also FSharp.Core's two-arity)
///     used by `ArtifactByKind` / `Emitter<'element>` / `DiffOf<'value>`;
///     F# resolves by arity (one type arg = ours; two = explicit error type).
///
/// The error list is ordered (oldest first); aggregating composition uses
/// `Result.aggregate` (own helper) or `validation { }` (FsToolkit).
///
/// Convention: `Result.success` / `Result.failure` / `Result.failureOf`
/// helpers below remain the V2 construction surface for parity with the
/// trunk's `Result.cs::Result<T>` API. Direct `Ok v` / `Error es`
/// construction also works; both forms are equivalent.
type Result<'a> = Microsoft.FSharp.Core.Result<'a, ValidationError list>

/// V2 helpers for `Result<'a>`. The FSharp.Core `Result` module provides
/// `bind` / `map` / `mapError` / `defaultValue` natively; this module
/// extends with V2-specific convenience (`success` / `failure` / `value`
/// / `errors` / `aggregate` / `collect` / `ensure` / `isSuccess` /
/// `isFailure`) that mirror the trunk's `Result<T>` API.
[<RequireQualifiedAccess>]
module Result =

    let success (value: 'a) : Result<'a> = Ok value

    /// Build a failure from a non-empty error list. Empty input is a
    /// programmer error.
    let failure (errors: ValidationError list) : Result<'a> =
        match errors with
        | [] -> invalidArg "errors" "At least one validation error must be provided."
        | _  -> Error errors

    /// Build a failure from a single error.
    let failureOf (error: ValidationError) : Result<'a> = Error [error]

    let isSuccess (r: Result<'a>) : bool =
        match r with
        | Ok _    -> true
        | Error _ -> false

    let isFailure (r: Result<'a>) : bool = not (isSuccess r)

    /// Project the success value or throw. Intended for tests and call
    /// sites that have already proven success.
    let value (r: Result<'a>) : 'a =
        match r with
        | Ok v    -> v
        | Error _ -> invalidOp "Cannot access value on a failed Result."

    let errors (r: Result<'a>) : ValidationError list =
        match r with
        | Ok _     -> []
        | Error es -> es

    /// Monadic bind. Delegates to FSharp.Core's `Result.bind`. Provided
    /// here for argument-order parity with V2's prior surface (`f` first,
    /// then `r` via pipeline). **Perf note (pillar 7):** `inline` so the
    /// F# compiler specializes at call sites; zero call overhead vs the
    /// inline FSharp.Core form.
    let inline bind (f: 'a -> Result<'b>) (r: Result<'a>) : Result<'b> =
        Microsoft.FSharp.Core.Result.bind f r

    /// Functor map. Delegates to FSharp.Core's `Result.map`. **Perf note:**
    /// `inline` per `bind`.
    let inline map (f: 'a -> 'b) (r: Result<'a>) : Result<'b> =
        Microsoft.FSharp.Core.Result.map f r

    /// If the result is a success and the predicate holds, pass through;
    /// otherwise fail with `error`. If already a failure, pass through.
    let ensure (predicate: 'a -> bool) (error: ValidationError) (r: Result<'a>) : Result<'a> =
        match r with
        | Error _              -> r
        | Ok v when predicate v -> r
        | Ok _                 -> Error [error]

    /// Collapse a sequence of results into a single result holding the list
    /// of values. Short-circuits on the first failure.
    let collect (results: Result<'a> seq) : Result<'a list> =
        let rec loop acc remaining =
            match remaining with
            | []                     -> Ok (List.rev acc)
            | Ok v        :: rest    -> loop (v :: acc) rest
            | Error es    :: _       -> Error es
        loop [] (List.ofSeq results)

    /// Aggregating sequence collapse. Unlike `collect`, accumulates errors
    /// across the whole sequence rather than short-circuiting on the first
    /// failure — boundary adapters surface diagnostics for every malformed
    /// input. (FsToolkit's `validation { }` is the applicative-style
    /// alternative when composing field-by-field.)
    ///
    /// **Perf note (pillar 7):** O(N) over input; pre-sized accumulators
    /// (ResizeArray) avoid the prior O(N²) `xs @ [x]` pattern that the
    /// audit Big-O Tier-1 finding flagged at chapter-3.1 close. Output
    /// is an immutable list; mutation is encapsulated.
    let aggregate (results: Result<'a> seq) : Result<'a list> =
        let successes = ResizeArray<'a>()  // LINT-ALLOW: O(N) accumulator inside a pure aggregator; output is immutable list
        let errors = ResizeArray<ValidationError>()  // LINT-ALLOW: same — paired error accumulator
        for r in results do
            match r with
            | Ok v     -> successes.Add v
            | Error es -> for e in es do errors.Add e
        if errors.Count > 0 then
            Error (List.ofSeq errors)
        else
            Ok (List.ofSeq successes)


/// Monadic infix operators for `Result<'a>`. Open this module at call sites
/// that benefit from `>>=` / `<!>` (the algebra reads more like the formal
/// system).
module ResultOperators =

    /// Bind: `m >>= f` is `Result.bind f m`.
    let inline (>>=) (r: Result<'a>) (f: 'a -> Result<'b>) : Result<'b> = Result.bind f r

    /// Map: `f <!> m` applies `f` inside a successful result.
    let inline (<!>) (f: 'a -> 'b) (r: Result<'a>) : Result<'b> = Result.map f r
