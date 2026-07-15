namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE: server-digest fast-path at the SQL boundary — terminal
//   SQL-text emission (String.Concat/Join/concat over typed encode-quoted
//   segments) at the DB boundary (the LiveProfiler file-header precedent).
//
// Editorial donor: V1's M1.8 data-integrity design
// (docs/implementation-specs/M1.8-data-integrity-verification.md — designed,
// status DEFERRED, never shipped; ADMIRE.md row 2026-07-15). The donation is
// the query shape — `HASHBYTES('SHA2_256', (SELECT … ORDER BY pk FOR XML
// RAW, BINARY BASE64))` with the physical→logical name bridge supplied by
// the model — and the rejection of `CHECKSUM`/`BINARY_CHECKSUM`/
// `CHECKSUM_AGG` for integrity (4-byte, collision-prone, NULL-blind). V2's
// basis, keying, and ladder are its own (T17; DECISIONS 2026-07-15, the
// fidelity chapter opens, entry 3).

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// Whether the server projection can carry a kind — a kind it cannot carry
/// descends to the authoritative client-canonical plane BY NAME (the
/// capability-descent discipline; a descent is never silent).
[<RequireQualifiedAccess>]
type ServerDigestSupport =
    | Supported
    | Unsupported of reason: string

[<RequireQualifiedAccess>]
module ServerDigest =

    // recon #8 — the one Core quoter (`SqlIdentifier.quote`, byte-verified
    // against ScriptDom's `Identifier.EncodeIdentifier`).
    let private encode = SqlIdentifier.quote

    /// The server digest of an empty table — the projection's own identity
    /// value (the subquery yields NULL over zero rows; absence is rendered
    /// as this constant so empty = empty holds within the plane). Mirrors
    /// the client fold's zero digest cosmetically; the planes never compare
    /// values across each other, only verdicts.
    let emptyDigest : string = String.replicate 64 "0"

    /// Decide whether the fast path can carry a kind. The projection orders
    /// by the primary key SERVER-side, so the key's ordering semantics must
    /// be stable across instances: integer order is universal and
    /// `uniqueidentifier` order is engine-defined identically everywhere;
    /// a text-family key rides column collation (two servers may disagree),
    /// and composite or absent keys have no single ORDER BY column.
    let supportOf (kind: Kind) : ServerDigestSupport =
        match Kind.primaryKey kind with
        | [ pk ] ->
            match pk.Type with
            | Integer | Guid -> ServerDigestSupport.Supported
            | other ->
                ServerDigestSupport.Unsupported
                    (String.Concat
                        ("the key '", Name.value pk.Name, "' is ", string other,
                         " — server-side ordering of this key family rides collation or rendering; the canonical plane compares this kind"))
        | [] ->
            ServerDigestSupport.Unsupported
                "the kind declares no primary key — the server projection has no stable row order"
        | _ ->
            ServerDigestSupport.Unsupported
                "the primary key is composite — the server projection orders by a single key column"

    /// The digest query: every column aliased to its LOGICAL name (the
    /// model-supplied bridge — two renditions of one kind then serialize to
    /// identical XML), projected in logical-name-sorted order (self-
    /// canonical, independent of either rendition's declaration order),
    /// rows ordered by the primary key, the whole document hashed once
    /// server-side. `BINARY BASE64` carries binary columns; a NULL cell
    /// omits its attribute (NULL stays distinct from the empty string).
    let private digestSql (renameMap: Map<Name, Name>) (kind: Kind) : string =
        let logicalNameOf (a: Attribute) : Name =
            match Map.tryFind a.Name renameMap with
            | Some logical -> logical
            | None -> a.Name
        let projection =
            kind.Attributes
            |> List.map (fun a -> a, logicalNameOf a)
            |> List.sortBy (fun (_, logical) -> Name.value logical)
            |> List.map (fun (a, logical) ->
                String.Concat(encode (ColumnRealization.columnNameText a.Column), " AS ", encode (Name.value logical)))
            |> String.concat ", "
        let table =
            String.Join(".", [| encode (TableId.schemaText kind.Physical); encode (TableId.tableText kind.Physical) |])
        let pkColumn =
            kind.Attributes
            |> List.tryFind (fun a -> a.IsPrimaryKey)
            |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)
            |> Option.defaultValue ""
        String.Concat(
            "SELECT CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', CONVERT(NVARCHAR(MAX), (SELECT ",
            projection, " FROM ", table, " ORDER BY ", encode pkColumn,
            " FOR XML RAW, BINARY BASE64))), 2) AS [digest]")

    /// One kind's server-side digest under the name bridge — one round-trip,
    /// zero rows transferred (Bench `fidelity.serverDigest`). The caller
    /// gates on `supportOf` first; a runtime failure (a type the projection
    /// cannot serialize, the 2 GB document ceiling) returns the named error
    /// and the caller descends to the canonical plane.
    let digest (cnn: SqlConnection) (renameMap: Map<Name, Name>) (kind: Kind) : Task<Result<string>> =
        task {
            try
                use _ = Bench.scope "fidelity.serverDigest"
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- digestSql renameMap kind
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                let! value = cmd.ExecuteScalarAsync()
                // An empty table yields a NULL scalar (the subquery has no
                // rows) — the projection's own empty identity, not a failure.
                match Option.ofObj value with
                | Some boxed ->
                    match boxed with
                    | :? string as hex -> return Result.success hex
                    | _ -> return Result.success emptyDigest
                | None -> return Result.success emptyDigest
            with ex ->
                return
                    Result.failureOf
                        (ValidationError.create "fidelity.serverDigest.failed"
                            (String.Concat("the server digest did not complete: ", ex.Message)))
        }
