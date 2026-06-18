namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Targets.Json
open Projection.Targets.Data

/// `projection slice-apply` / `slice-reset` — the data-portability APPLY verbs
/// (Slice 7). Materialize a portable golden dataset against a TARGET schema and
/// emit the self-contained, DML-only T-SQL load artifact (capture-and-remap
/// MERGE — no IDENTITY_INSERT; the disposition rides the target catalog's
/// identity marks, so a managed/AssignedBySink kind gets MERGE-OUTPUT capture +
/// phase-2 FK repoint).
///
/// `slice-reset` additionally carries the authoritative
/// `WHEN NOT MATCHED BY SOURCE … THEN DELETE` arm, **bounded to the slice's
/// root predicate**. Safety is by construction: `DeleteScopePolicy.resolveFor`
/// yields the arm ONLY for kinds that carry the gating column(s), so the
/// pulled-in parents / lookups / canonical users (which do not) are NEVER
/// pruned — the operator's locked "only the slice's own entity, within the root
/// predicate" choice. Gated behind the loss-ack (`--allow-drops`).
[<RequireQualifiedAccess>]
module SliceApplyRun =

    /// Map the golden's logical rows onto the TARGET catalog, in target SsKey
    /// space, with a SCHEMA-PARITY gate (`slice.schemaParity`): every golden
    /// entity must resolve to a target kind, and every golden column to that
    /// kind's attribute. Target EXTRAS are tolerated (the operator's "congruent
    /// on reachable entities" choice). PURE.
    let mapToTarget (catalog: Catalog) (golden: GoldenDataset) : Result<Map<SsKey, StaticRow list>> =
        let perEntity (e: GoldenEntity) : Result<SsKey * StaticRow list> =
            match ClosureOracle.resolveEntity catalog (EntityCoordinate.ofEntity e.Entity) with
            | None ->
                Result.failureOf
                    (ValidationError.create "slice.schemaParity"
                        (sprintf "target schema has no entity '%s'." e.Entity))
            | Some kind ->
                let attrNames = kind.Attributes |> List.map (fun a -> a.Name) |> Set.ofList
                let badCols =
                    e.Rows
                    |> List.collect (fun row -> row |> Map.toList |> List.map fst)
                    |> List.distinct
                    |> List.filter (fun c -> not (Set.contains c attrNames))
                if not (List.isEmpty badCols) then
                    Result.failureOf
                        (ValidationError.create "slice.schemaParity"
                            (sprintf "entity '%s': target schema lacks column(s) %s."
                                e.Entity (badCols |> List.map Name.value |> String.concat ", ")))
                else
                    let rows =
                        e.Rows
                        |> List.mapi (fun i row ->
                            { Identifier = SsKey.synthesizedComposite "GOLDEN" [ e.Entity; string i ] |> Result.value
                              Values = row })
                    Ok (kind.SsKey, rows)
        golden.Entities
        |> List.map perEntity
        |> Result.collect
        |> Result.map Map.ofList

    /// Emit the self-contained DML-only T-SQL load artifact from the golden,
    /// against the target catalog. `deleteScope = Some` adds the bounded
    /// authoritative-reset DELETE arm. PURE — the whole apply/reset core is
    /// testable without a database.
    let emit (catalog: Catalog) (golden: GoldenDataset) (deleteScope: DeleteScopePolicy option) : Result<string> =
        match mapToTarget catalog golden with
        | Error es -> Error es
        | Ok rows ->
            let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
            let plan = DataLoadPlan.build catalog topo rows SurrogateRemapContext.empty
            let artifactR =
                match deleteScope with
                | Some _ -> StaticSeedsEmitter.emitFromPlanWith deleteScope catalog Profile.empty plan
                | None   -> StaticSeedsEmitter.emitFromPlan catalog Profile.empty plan
            match artifactR with
            | Error emitErr ->
                Result.failureOf (ValidationError.create "slice.emitFailed" (sprintf "%A" emitErr))
            | Ok artifact ->
                let map = ArtifactByKind.toMap artifact
                // Global Phase-1-then-Phase-2 in topological order (the cycle-
                // correct deploy order; the same walk the composer does).
                let phase1 = topo.Order |> List.choose (fun k -> Map.tryFind k map |> Option.map (fun s -> s.RenderedPhase1))
                let phase2 = topo.Order |> List.choose (fun k -> Map.tryFind k map |> Option.map (fun s -> s.RenderedPhase2))
                Ok (Seq.append phase1 phase2 |> String.concat "")

    /// Open a read connection to the target (same spec forms as
    /// `SliceExtractRun.openSource`: `env:`/`file:` via ConnectionResolver;
    /// `live:<connStr>` / a bare connection string opened directly).
    let private openTarget (spec: string) : Task<Result<SqlConnection>> =
        task {
            if spec.StartsWith "env:" || spec.StartsWith "file:" then
                match TransferSpec.parseConnectionSpec spec with
                | Error es -> return Result.failure es
                | Ok connRef ->
                    let sub : Substrate =
                        { Environment   = Environment.Named "slice-target"
                          Role          = SubstrateRole.Sink
                          ConnectionRef = connRef }
                    return! ConnectionResolver.openSubstrate sub
            else
                let connStr = if spec.StartsWith "live:" then spec.Substring 5 else spec
                try
                    let cnn = new SqlConnection(connStr)
                    do! cnn.OpenAsync()
                    return Result.success cnn
                with ex ->
                    return Result.failureOf (ValidationError.create "connection.openFailed" ex.Message)
        }

    /// Read the golden, read the target schema, emit the artifact, write it.
    /// Returns the row count emitted.
    let applyToFile (connSpec: string) (goldenPath: string) (deleteScope: DeleteScopePolicy option) (outPath: string) : Task<Result<int>> =
        task {
            let goldenJson =
                try Ok (System.IO.File.ReadAllText goldenPath)
                with ex -> Result.failureOf (ValidationError.create "slice.golden.read" ex.Message)
            match goldenJson with
            | Error es -> return Error es
            | Ok json ->
                match GoldenCodec.deserialize json with
                | Error es -> return Error es
                | Ok golden ->
                    match! openTarget connSpec with
                    | Error es -> return Error es
                    | Ok cnn ->
                        use cnn = cnn
                        match! ReadSide.read cnn with
                        | Error es -> return Error es
                        | Ok catalog ->
                            match emit catalog golden deleteScope with
                            | Error es -> return Error es
                            | Ok sql ->
                                try
                                    System.IO.File.WriteAllText(outPath, sql)
                                    return Ok (golden.Entities |> List.sumBy (fun e -> List.length e.Rows))
                                with ex ->
                                    return Result.failureOf (ValidationError.create "slice.writeFailed" ex.Message)
        }
