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
            invalidArg paramName (sprintf "%s must be provided." paramName)

    /// Build a `ValidationError` with no metadata.
    let create (code: string) (message: string) : ValidationError =
        requireNonBlank "code" code
        requireNonBlank "message" message
        { Code = code; Message = message; Metadata = Map.empty }

    /// Attach (or replace) a single metadata entry. A blank key is a no-op,
    /// matching the trunk's behavior.
    let withMetadata (key: string) (value: string option) (e: ValidationError) : ValidationError =
        if System.String.IsNullOrWhiteSpace key then e
        else { e with Metadata = e.Metadata |> Map.add key value }

    /// Replace the human-readable message while preserving code and metadata.
    let withMessage (message: string) (e: ValidationError) : ValidationError =
        requireNonBlank "message" message
        { e with Message = message }


/// `Result<'a>` is either `Success` carrying a value or `Failure` carrying a
/// non-empty list of validation errors. Mirrors
/// `src/Osm.Domain/Abstractions/Result.cs::Result<T>`. The list is ordered
/// (oldest error first); error aggregation across applicative composition is
/// not part of this type's contract — see the trunk's `Collect` for the
/// short-circuiting variant.
///
/// Coexists with the FSharp.Core built-in `Result<'T,'TError>` because the
/// arity differs.
type Result<'a> =
    | Success of 'a
    | Failure of ValidationError list

/// Monadic and applicative helpers for `Result<'a>`. `bind` short-circuits
/// on first failure; this matches the trunk's `Bind`/`Map`/`Ensure`/`Collect`.
[<RequireQualifiedAccess>]
module Result =

    let success (value: 'a) : Result<'a> = Success value

    /// Build a failure from a non-empty error list. Empty input is a
    /// programmer error (the trunk throws `ArgumentException`).
    let failure (errors: ValidationError list) : Result<'a> =
        match errors with
        | [] -> invalidArg "errors" "At least one validation error must be provided."
        | _  -> Failure errors

    /// Build a failure from a single error. Convenience wrapper.
    let failureOf (error: ValidationError) : Result<'a> = Failure [error]

    let isSuccess (r: Result<'a>) : bool =
        match r with
        | Success _ -> true
        | Failure _ -> false

    let isFailure (r: Result<'a>) : bool = not (isSuccess r)

    /// Project the success value or throw. Matches the trunk's `Value`
    /// property; intended for tests and call sites that have already proven
    /// success.
    let value (r: Result<'a>) : 'a =
        match r with
        | Success v -> v
        | Failure _ -> invalidOp "Cannot access value on a failed Result."

    let errors (r: Result<'a>) : ValidationError list =
        match r with
        | Success _  -> []
        | Failure es -> es

    /// Monadic bind — `m >>= f` short-circuits on `Failure`.
    let bind (f: 'a -> Result<'b>) (r: Result<'a>) : Result<'b> =
        match r with
        | Success v   -> f v
        | Failure es  -> Failure es

    /// Functor map — preserves failures untouched.
    let map (f: 'a -> 'b) (r: Result<'a>) : Result<'b> =
        match r with
        | Success v   -> Success (f v)
        | Failure es  -> Failure es

    /// If the result is a success and the predicate holds, pass through;
    /// otherwise fail with `error`. If already a failure, pass through.
    let ensure (predicate: 'a -> bool) (error: ValidationError) (r: Result<'a>) : Result<'a> =
        match r with
        | Failure _              -> r
        | Success v when predicate v -> r
        | Success _              -> Failure [error]

    /// Collapse a sequence of results into a single result holding the list
    /// of values. Short-circuits on the first failure (matches trunk
    /// `Collect`); does not aggregate errors across multiple failures.
    let collect (results: Result<'a> seq) : Result<'a list> =
        let rec loop acc remaining =
            match remaining with
            | []                      -> Success (List.rev acc)
            | Success v   :: rest     -> loop (v :: acc) rest
            | Failure es  :: _        -> Failure es
        loop [] (List.ofSeq results)

    /// Aggregating sequence collapse. Unlike `collect`, accumulates errors
    /// across the whole sequence rather than short-circuiting on the first
    /// failure — boundary adapters surface diagnostics for every malformed
    /// input, not just the first. Per session-35 — replaces the
    /// `List.fold (xs @ [x])` pattern that grew O(N²) at every call site
    /// (ReadSide attribute aggregation, FK reference aggregation, kind
    /// aggregation, `kindsWithRefs` aggregation).
    let aggregate (results: Result<'a> seq) : Result<'a list> =
        let successes = ResizeArray<'a>()
        let errors = ResizeArray<ValidationError>()
        for r in results do
            match r with
            | Success v -> successes.Add v
            | Failure es -> for e in es do errors.Add e
        if errors.Count > 0 then
            Failure (List.ofSeq errors)
        else
            Success (List.ofSeq successes)


/// Monadic infix operators for `Result<'a>`. Open this module at call sites
/// that benefit from `>>=` / `<!>` (the algebra reads more like the formal
/// system).
module ResultOperators =

    /// Bind: `m >>= f` is `Result.bind f m`.
    let inline (>>=) (r: Result<'a>) (f: 'a -> Result<'b>) : Result<'b> = Result.bind f r

    /// Map: `f <!> m` applies `f` inside a successful result.
    let inline (<!>) (f: 'a -> 'b) (r: Result<'a>) : Result<'b> = Result.map f r
