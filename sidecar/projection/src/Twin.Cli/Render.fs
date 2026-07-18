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

    let usage : string list =
        [ "twin — a synthetic-data sidecar for an SSDT repository."
          ""
          "  twin up      [--scenario <name>]   Container present, schema current, data present. No-op when nothing changed."
          "  twin seed    [--scenario <name>]   Re-mint the data, reproducibly."
          "  twin status  [--scenario <name>]   What the twin holds against what the repository defines."
          "  twin down                           Stop the container; keep its state."
          "  twin reset                          Remove the container and its data."
          "  twin init                           Write a starter twin.json."
          ""
          "Configuration is read from ./twin.json (or TWIN_CONFIG)." ]
