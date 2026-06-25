namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE: terminal SSDT post-deployment-script text composition at the
//   emitter boundary. The structural inputs (lane paths / lane SQL) are typed;
//   the SQLCMD `:r` directives + banner comments are the terminal text surface
//   (`String.concat` is the BCL primitive). Per `DECISIONS 2026-05-09 — Built-in
//   obligation`: there is no typed-AST for a SQLCMD post-deploy directive.

/// Π_PostDeploy — the SSDT **post-deployment script** that loads the DATA lanes
/// (static-entity seeds + migration-dependency rows) AFTER the schema publish,
/// matching the operator's SSDT flow: `sqlpackage /Action:Publish` (or DacFx
/// `DacServices.Deploy`) deploys the schema, then runs the post-deploy.
///
/// The **Bootstrap** MERGE/INSERT lane is deliberately NOT here — it is the
/// SEPARATE post-publish data-load step the operator runs against the live
/// database (operator decision 2026-06-24: post-deploy carries static seeds +
/// migration data; bootstrap is the after-publish load).
///
/// Pure string composition over caller-supplied lane content (no
/// `Projection.Targets.Data` dependency — the Pipeline threads the rendered lane
/// SQL / bundle-relative file paths in). Two forms, because the two SSDT
/// realizations consume the post-deploy differently:
///   - `renderIncludes` — the SQLCMD `:r`-include form, written as the
///     `Script.PostDeployment.sql` bundle artifact. `Microsoft.Build.Sql` inlines
///     the referenced `Data/*.sql` at `.sqlproj` build into the package's
///     post-deploy.
///   - `renderInlined` — the self-contained form embedded in a `.dacpac` via
///     `PackageMetadata.PostDeploymentScript` (DacFx's `BuildPackage` does not
///     resolve SQLCMD `:r`, so the lane SQL is inlined directly).
[<RequireQualifiedAccess>]
module PostDeployEmitter =

    /// The conventional SSDT post-deploy file name (one per project; SDK-marked
    /// `PostDeploy`). Kept in sync with `SqlprojEmitter`'s `PostDeploy` item.
    [<Literal>]
    let fileName : string = "Script.PostDeployment.sql"

    let private header : string =
        String.concat "\n"
            [ "-- ============================================================"
              "-- Post-Deployment Script — V2 projection"
              "-- Runs AFTER the schema publish: static-entity seeds + migration"
              "-- dependency rows. The Bootstrap MERGE/INSERT is a SEPARATE"
              "-- post-publish data-load step and is not included here."
              "-- ============================================================"
              "" ]

    /// The SQLCMD `:r`-include post-deploy (`Script.PostDeployment.sql`).
    /// `laneRelPaths` are the bundle-relative data-lane paths in deploy order
    /// (forward-slash, e.g. `Data/StaticSeeds.sql`); the caller omits empty
    /// lanes. SQLCMD `:r` resolves each relative to the script's project location
    /// at build (`Microsoft.Build.Sql`), inlining the lane's MERGE batches.
    let renderIncludes (laneRelPaths: string list) : string =
        match laneRelPaths with
        | [] -> System.String.Concat(header, "-- (no data lanes to include)\n")
        | paths ->
            let includes =
                paths
                |> List.map (fun p -> System.String.Concat(":r ", p))
                |> String.concat "\n"
            System.String.Concat(header, includes, "\n")

    /// The self-contained inlined post-deploy embedded in a `.dacpac`. `lanes`
    /// are `(label, sql)` pairs in deploy order; each lane's already-GO-batched
    /// SQL is concatenated under a labeled banner. Whitespace-only lanes are
    /// skipped (an absent lane contributes nothing).
    let renderInlined (lanes: (string * string) list) : string =
        let sections =
            lanes
            |> List.filter (fun (_, sql) -> not (System.String.IsNullOrWhiteSpace sql))
            |> List.map (fun (label, sql) ->
                System.String.Concat("-- ---- ", label, " ----\n", sql.TrimEnd(), "\n"))
        match sections with
        | [] -> System.String.Concat(header, "-- (no data lanes)\n")
        | xs -> System.String.Concat(header, "\n", String.concat "\n" xs)
