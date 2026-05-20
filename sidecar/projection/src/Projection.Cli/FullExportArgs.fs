module Projection.Cli.FullExportArgs

open Argu

/// Argu closed-DU surface for `projection full-export` (chapter B.4
/// slice 7). Per `docs/logging-format.md` §15.2: Argu is V2's CLI
/// argument-parsing library — F#-native; discriminated-union-based
/// subcommand definition matches V2's closed-DU posture.
///
/// Slice-7 THIN scope: two flags only. Dormant config sections in
/// `Pipeline.Config` (Profile / Cache / Profiler / TypeMapping /
/// Emission booleans / Policy.* / Overrides.MigrationDependencies /
/// Overrides.StaticData / Overrides.CircularDependencies) continue
/// to parse-but-ignore — operator hand-writing future-shaped configs
/// gets no surprises. Chapter C extends the operator-facing surface
/// (axes 2-6 + 7a + 9-revised); chapter B.4 ships only the wiring
/// for today's three live consumers (`Model.Path` → `CatalogReader
/// .parse`; `Overrides.TableRenames` → `TableRename.registered`;
/// `Output.Dir` → `Compose.write`).
///
/// `--verbose` is added as the contract §4 verbosity gate. Chapter C
/// slice C.6 widens to `--debug` + per-category filters.
type FullExportArg =
    | [<Mandatory; AltCommandLine("-c")>] Config of path: string
    | [<AltCommandLine("-o")>] Output of dir: string
    | Verbose
    | [<AltCommandLine("-d")>] Debug
    | MuteCategory of category: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Config _ ->
                "Path to the unified config JSON (per V2_PRODUCTION_CUTOVER §5.1). "
                + "Today the wired consumers are Model.Path (catalog source), "
                + "Overrides.TableRenames (rename pass), and Output.Dir (artifact "
                + "destination); other sections parse-but-ignore."
            | Output _ ->
                "Override the config's Output.Dir. When supplied, takes precedence "
                + "over the config's Output.Dir value at write time."
            | Verbose ->
                "Emit Debug-level events to stderr in addition to Info / Warn / "
                + "Error. Default suppresses Trace/Debug per logging-format "
                + "contract §4."
            | Debug ->
                "Emit Trace AND Debug level events to stderr (broader than "
                + "--verbose: Trace surfaces function-entry/exit + iteration-level "
                + "bench samples). Implies --verbose."
            | MuteCategory _ ->
                "Suppress events from the named category at the egress boundary. "
                + "Accepts: config | extract | profile | transform | emit | deploy "
                + "| canary | summary. May be specified multiple times to mute "
                + "several categories. Muted envelopes don't contribute to the "
                + "§11 rollup."
