namespace Twin.Runtime
// LINT-ALLOW-FILE: the Twin's estate-file glob matcher + file reader — compiles
//   operator glob patterns (`*.json`) to `Regex` for path matching. Glob is the
//   ONE use-case where `Regex` is the gold-standard primitive (glob semantics
//   ARE a regex dialect — `*` → `.*`, escaped literals between); considered a
//   manual char-walk matcher, rejected as reimplementing the regex engine for
//   no gain. The mutable locals build the pattern in one pass; file I/O is
//   inherently side-effecting. Boundary-confined.

open System.IO
open Projection.Core
open Twin.Core

/// THE TWIN — estate file resolution (Twin.Runtime).
///
/// Resolves the `estate` section's patterns against the repository root
/// and reads the matched files into the pure `EstateDefinition`. The
/// glob dialect is deliberately small — `**` (any directory depth), `*`
/// (any run within a segment), `?` (one character) — matched against
/// forward-slash relative paths, case-insensitively (the repo may be
/// authored on Windows). Enumeration order never matters downstream:
/// `EstateDefinition.create` sorts.
[<RequireQualifiedAccess>]
module EstateFiles =

    let private readFailure (path: string) (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.estate.readFailed"
            "An estate file could not be read."
            (Map.ofList [ "path", Some path; "detail", Some detail ])

    let private rootMissing (root: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.estate.rootMissing"
            "The repository root does not exist."
            (Map.ofList [ "path", Some root ])

    /// Translate the small glob dialect to an anchored regex over
    /// forward-slash relative paths.
    let private globToRegex (pattern: string) : System.Text.RegularExpressions.Regex =
        let sb = System.Text.StringBuilder()
        sb.Append '^' |> ignore
        let mutable i = 0
        while i < pattern.Length do
            match pattern.[i] with
            | '*' when i + 1 < pattern.Length && pattern.[i + 1] = '*' ->
                // `**/` or trailing `**` — any depth (including none).
                if i + 2 < pattern.Length && pattern.[i + 2] = '/' then
                    sb.Append "(?:.*/)?" |> ignore
                    i <- i + 3
                else
                    sb.Append ".*" |> ignore
                    i <- i + 2
            | '*' ->
                sb.Append "[^/]*" |> ignore
                i <- i + 1
            | '?' ->
                sb.Append "[^/]" |> ignore
                i <- i + 1
            | c ->
                sb.Append (System.Text.RegularExpressions.Regex.Escape (string c)) |> ignore
                i <- i + 1
        sb.Append '$' |> ignore
        System.Text.RegularExpressions.Regex(
            sb.ToString(),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ||| System.Text.RegularExpressions.RegexOptions.CultureInvariant)

    /// The repo-relative forward-slash path of `file` under `root`.
    let private relativePath (root: string) (file: string) : string =
        Path.GetRelativePath(root, file).Replace('\\', '/')

    /// Every file under `root` matching the glob, as (relativePath, file).
    let private matchPattern (root: string) (pattern: string) : (string * string) list =
        let regex = globToRegex pattern
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        |> Seq.map (fun f -> relativePath root f, f)
        |> Seq.filter (fun (rel, _) -> regex.IsMatch rel)
        |> Seq.toList

    let private readFile (rel: string, full: string) : Result<EstateFile> =
        try Result.success { RelativePath = rel; Content = File.ReadAllText full }
        with ex -> Result.failureOf (readFailure rel ex.Message)

    /// Resolve one explicit path (a static-data lane) — the file must exist.
    let private readExplicit (root: string) (rel: string) : Result<EstateFile> =
        let full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))
        if File.Exists full then readFile (rel.Replace('\\', '/'), full)
        else
            Result.failureOf
                (ValidationError.createWithMetadata
                    "twin.estate.laneMissing"
                    "A configured static-data lane does not exist in the repository."
                    (Map.ofList [ "path", Some rel ]))

    /// Resolve the estate definition from the repository root per the
    /// config's estate section. Table scripts come from the glob; static
    /// lanes are explicit paths (order preserved by the definition's
    /// path sort — name lanes so their order sorts correctly, or keep
    /// one lane).
    let resolve (root: string) (estate: EstateSection) : Result<EstateDefinition> =
        if not (Directory.Exists root) then Result.failureOf (rootMissing root)
        else
            let tables = matchPattern root estate.TablesPattern |> List.map readFile |> Result.aggregate
            let schemas =
                match estate.SchemasPattern with
                | Some pattern -> matchPattern root pattern |> List.map readFile |> Result.aggregate
                | None -> Result.success []
            let lanes = estate.StaticData |> List.map (readExplicit root) |> Result.aggregate
            match tables, schemas, lanes with
            | Ok t, Ok s, Ok l -> EstateDefinition.create t s l
            | tR, sR, lR -> Result.failure (Result.errors tR @ Result.errors sR @ Result.errors lR)

    /// The estate's fingerprint contributions — one per file, named by
    /// its relative path.
    let contributions (estate: EstateDefinition) : Fingerprint.Contribution list =
        EstateDefinition.allFiles estate
        |> List.map (fun f -> { Fingerprint.Contribution.Name = f.RelativePath; Fingerprint.Contribution.Content = f.Content })
