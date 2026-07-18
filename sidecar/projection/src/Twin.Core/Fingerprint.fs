namespace Twin.Core

open Projection.Core

/// THE TWIN — the fingerprint (THE_TWIN.md §laws, law 1: convergence).
///
/// The hash of everything a materialization depends on: the estate files,
/// the resolved configuration, the evidence artifacts, the scenario, the
/// seed, and the tool version. Equality means `twin up` has nothing to do
/// — the no-op fast path that makes the one-click loop honest. The value
/// is stored inside the twin database (`[twin].[__state]`) so the twin
/// describes itself; comparison never consults hidden local state.
///
/// Determinism: SHA-256 over a canonical UTF-8 rendering — files sorted
/// by lower-cased path, each contribution length-prefixed (the SsKey
/// codec's discipline: no delimiter ambiguity, no escaping). Pure by
/// construction; the same inputs render the same bytes on any host.
type Fingerprint = private Fingerprint of string

[<RequireQualifiedAccess>]
module Fingerprint =

    /// One named contribution to the fingerprint — a file, a config
    /// document, an evidence artifact. `Name` is the stable identifier
    /// (path or role); `Content` the full text it contributes.
    type Contribution = {
        Name    : string
        Content : string
    }

    let private sha256Hex (bytes: byte[]) : string =
        use sha = System.Security.Cryptography.SHA256.Create()
        System.Convert.ToHexString(sha.ComputeHash bytes).ToLowerInvariant()

    let private field (s: string) : string =
        System.String.Concat(string s.Length, ":", s)  // LINT-ALLOW: length-prefixed canonical-form field (the SsKey-codec discipline); the canonical form IS a string, no AST

    /// Hash one contribution's content. Exposed so callers can log the
    /// per-file digest in a status report without re-deriving the recipe.
    let contentHash (content: string) : string =
        sha256Hex (System.Text.Encoding.UTF8.GetBytes content)

    /// Compute the fingerprint over the given contributions plus the
    /// run parameters that change what a mint produces.
    let compute
        (toolVersion: string)
        (scenario: string)
        (seed: uint64)
        (contributions: Contribution list)
        : Fingerprint =
        let canonical =
            contributions
            |> List.sortBy (fun c -> c.Name.ToLowerInvariant())
            |> List.map (fun c -> System.String.Concat(field (c.Name.ToLowerInvariant()), field (contentHash c.Content)))  // LINT-ALLOW: length-prefixed canonical-form pair; see module doc
            |> String.concat ""
        let rendered =
            System.String.Concat(field toolVersion, field scenario, field (string seed), field canonical)  // LINT-ALLOW: length-prefixed canonical-form assembly; see module doc
        Fingerprint (sha256Hex (System.Text.Encoding.UTF8.GetBytes rendered))

    /// The hex text — what `[twin].[__state]` stores and status compares.
    let value (Fingerprint v) : string = v

    /// Rehydrate a stored fingerprint (from `[twin].[__state]`) for
    /// comparison. Zero validation beyond non-blank: a foreign value
    /// simply never equals a computed one, which reads as "the twin
    /// holds an unknown materialization" — the honest verdict.
    let ofStored (v: string) : Fingerprint option =
        if System.String.IsNullOrWhiteSpace v then None else Some (Fingerprint (v.Trim().ToLowerInvariant()))
