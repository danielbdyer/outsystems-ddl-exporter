namespace Twin.Cli

open Projection.Core
open Twin.Core
open Twin.Runtime

/// THE TWIN — the operator surface copy (Twin.Cli).
///
/// Every line obeys the THE_VOICE.md register: stative and agentless, no
/// pronouns, the verdict first, humane numbers, the next move named as a
/// plain imperative, refusals concrete and located. Codes and paths live
/// beneath the statement, never on it.
[<RequireQualifiedAccess>]
module Render =

    let private n0 (v: int64) : string =
        v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    let private ni (v: int) : string = n0 (int64 v)

    /// One refusal line: the message, with the code and location beneath.
    let private errorLines (es: ValidationError list) : string list =
        es
        |> List.collect (fun e ->
            let where =
                [ "path"; "coordinate"; "table"; "scenario"; "detail" ]
                |> List.tryPick (fun k -> e.Metadata |> Map.tryFind k |> Option.flatten)
            let beneath =
                match where with
                | Some w -> System.String.Concat("    ", e.Code, " · ", w)
                | None -> System.String.Concat("    ", e.Code)
            [ System.String.Concat("  ", e.Message); beneath ])

    /// A failure surface: the verdict, then each cause.
    let failure (es: ValidationError list) : string list =
        "Stopped before any change was applied. The cause is shown below."
        :: errorLines es

    let upOutcome (outcome: Runs.UpOutcome) : string list =
        match outcome with
        | Runs.NothingToApply (tables, rows, scenario) ->
            [ System.String.Concat(
                "Twin current — ", ni tables, " tables, ", n0 rows, " rows, scenario \"", scenario, "\".")
              "Nothing to apply." ]
        | Runs.Materialized r ->
            let schemaLine =
                if r.SchemaPublished then "The schema has been published from the repository definitions."
                else "The schema is unchanged."
            let lanesLine =
                System.String.Concat(
                    ni r.LanesApplied, " static-data lanes applied — ", ni r.ProvidedKinds,
                    " tables carry the estate's own reference data.")
            let mintLine =
                System.String.Concat(
                    ni r.MintedKinds, " tables filled with synthetic rows — ", n0 r.TotalRows,
                    " rows held in total, scenario \"", r.Scenario, "\", seed ", string r.Seed, ".")
            let unsatisfiable =
                if r.UnsatisfiableFks = 0 then []
                else
                    [ System.String.Concat(
                        ni r.UnsatisfiableFks,
                        " non-nullable relationships could not be satisfied and hold NULL; the affected columns are named in the run detail.") ]
            [ System.String.Concat(
                "Twin materialized — ", ni r.DefinedTables, " tables, ", n0 r.TotalRows, " rows.")
              schemaLine; lanesLine; mintLine ]
            @ unsatisfiable

    let status (s: Runs.StatusReport) : string list =
        match s.Container with
        | TwinContainer.Absent ->
            [ "No twin container is present."
              System.String.Concat("The repository defines ", ni s.DefinedTables, " tables.")
              "Run: twin up" ]
        | TwinContainer.Stopped ->
            [ "The twin container is stopped."
              "Run: twin up" ]
        | TwinContainer.Running ->
            if not s.DatabasePresent then
                [ "The twin container is running; the twin database has not been created."
                  "Run: twin up" ]
            else
                let current = s.SchemaCurrent = Some true && s.DataCurrent = Some true
                if current then
                    let scenario = defaultArg s.StoredScenario TwinConfig.BaselineScenario
                    [ System.String.Concat(
                        "Twin current — ",
                        (match s.LiveTables with Some t -> System.String.Concat(ni t, " tables, ") | None -> ""),
                        n0 (defaultArg s.LiveRows 0L), " rows, scenario \"", scenario, "\".")
                      "The schema matches the repository definitions. Nothing to apply." ]
                else
                    let planes =
                        match s.SchemaCurrent, s.DataCurrent with
                        | Some false, _ -> "The schema differs from the repository definitions."
                        | Some true, Some false -> "The schema matches; the data was minted from an earlier definition."
                        | _ -> "The twin's contents do not match the repository definitions."
                    [ planes
                      System.String.Concat(
                          "The twin holds ", n0 (defaultArg s.LiveRows 0L), " rows across ",
                          (match s.LiveTables with Some t -> ni t | None -> "an unknown number of"), " tables.")
                      "Run: twin up" ]

    let down () : string list =
        [ "The twin container is stopped. State is preserved; twin up restarts it." ]

    let reset () : string list =
        [ "The twin container has been removed, and its data with it. The next twin up starts from the repository definitions alone." ]

    let initScaffolded (path: string) : string list =
        [ "A starter twin.json has been written."
          System.String.Concat("    ", path)
          "Set the estate.tables pattern to the repository's table scripts, then run: twin up" ]

    let initExists (path: string) : string list =
        [ "twin.json is already present; nothing was written."
          System.String.Concat("    ", path) ]

    let check (r: Check.CheckReport) : string list =
        let clean =
            r.OrphanRows = 0L && r.DeterministicRemint && List.isEmpty r.Findings
            && r.TablesLive = r.TablesDefined
        let verdict =
            if clean then "Verified. The estate models, publishes, and mints; the proof holds."
            else "The proof returned findings. Each is shown below."
        let body =
            [ System.String.Concat(
                "    ", ni r.TablesLive, " of ", ni r.TablesDefined, " tables live · ",
                ni r.LanesApplied, " lanes · ", n0 r.TotalRows, " rows · ",
                n0 r.OrphanRows, " orphaned references")
              System.String.Concat(
                "    re-mint ",
                (if r.DeterministicRemint then "byte-identical" else "DIVERGED — determinism does not hold")) ]
        let findings =
            r.Findings
            |> List.map (fun f -> System.String.Concat("  ", f.Coordinate, " — ", f.Detail))
        verdict :: body @ findings

    let evidenceImport (r: EvidenceImport.ImportReport) : string list =
        let perSource =
            r.Sources
            |> List.map (fun s ->
                System.String.Concat("    ", s.Source, " — ", ni s.Tables, " tables, ", ni s.Columns, " columns"))
        [ "Evidence imported — the rich pack is written."
          System.String.Concat("    ", r.RichPath) ]
        @ perSource
        @ [ System.String.Concat("    ", ni r.FanOuts, " relationship fan-outs captured.")
            "Next: twin evidence derive — the committed, literal-free shape tier." ]

    let evidenceDerive (path: string) : string list =
        [ "The shape tier is derived — counts, null rates, cardinalities, fan-out shapes; no captured literal."
          System.String.Concat("    ", path)
          "Commit it beside twin.json; the rich pack stays out of the repository." ]

    let evidenceVerify (r: EvidenceImport.VerifyReport) : string list =
        let header =
            match r.RichPresent, r.ShapePresent with
            | false, false -> "No evidence packs are present. Run: twin evidence import"
            | _ ->
                if List.isEmpty r.Problems then "Evidence binds against the current estate definition."
                else "Evidence no longer binds cleanly. Each problem is shown below."
        let coverage =
            r.Coverage
            |> List.map (fun c ->
                System.String.Concat(
                    "    ", c.Table, " — ", c.Tier, " · ",
                    ni c.EvidencedColumns, " of ", ni c.TotalColumns, " columns"))
        header :: coverage @ errorLines r.Problems

    let classify (r: Classify.ClassifyReport) : string list =
        [ System.String.Concat(ni r.Classified, " columns classified as personal data by name; the artifact is written for review.")
          System.String.Concat("    ", r.Path) ]
        @ (if r.ConfigSet then []
           else [ "Add \"corrections\": \"twin/corrections.json\" to twin.json so the mint applies it." ])

    let bake (r: Bake.BakeReport) : string list =
        [ "A docker build context for the estate's schema image is written."
          System.String.Concat("    ", r.Directory)
          "Build with: docker build -t twin-estate ."
          "The image carries the schema; start it, then apply the lanes and twin seed for data." ]

    let usage : string list =
        [ "twin — a synthetic-data sidecar for an SSDT repository."
          ""
          "  twin up      [--scenario <name>]   Container present, schema current, data present. No-op when nothing changed."
          "  twin seed    [--scenario <name>]   Re-mint the data, reproducibly."
          "  twin status  [--scenario <name>]   What the twin holds against what the repository defines."
          "  twin check   [--scenario <name>]   The proof, on a throwaway database: model, publish, mint, zero orphans, byte-identical re-mint."
          "  twin evidence import                Profile the configured sources into the rich pack (out of repo)."
          "  twin evidence derive                Project rich → shape: the committed, literal-free tier."
          "  twin evidence verify                Bind both packs against the estate; the per-table coverage board."
          "  twin classify                       Propose PII classifications from column names (reviewable artifact)."
          "  twin bake                           A docker build context for a distributable schema image."
          "  twin down                           Stop the container; keep its state."
          "  twin reset                          Remove the container and its data."
          "  twin init                           Write a starter twin.json."
          ""
          "Configuration is read from ./twin.json (or TWIN_CONFIG)." ]
