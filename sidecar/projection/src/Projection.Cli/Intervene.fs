module Projection.Cli.Intervene

open System
open Spectre.Console

/// The operator-intervention seam (the gate, made answerable). A running face
/// may, at a SAFE pre-write boundary, pause and present the operator a small
/// choice (or a value input) INSTEAD of crashing or proceeding into loss — and
/// MUST degrade to an already-expressible named fallback when non-interactive.
///
/// Three disciplines hold this honest:
///   - **No copy.** Every prompt title / choice label is caller-supplied,
///     resolved through `Voice` at the call site. The seam authors no strings.
///   - **Headless-total.** A piped / CI / no-TTY run is never blocked on stdin;
///     it takes the named fallback (an existing refusal or a config/flag default
///     — A44 expressible ⇔ reachable; "downgrades never silent").
///   - **CLI-boundary only.** Lives in `Projection.Cli`, never in Core/Pipeline
///     (purity). Prompts draw on stderr (channel 2), leaving stdout the answer
///     surface.
///
/// Ships INERT — exercised by tests; wired by the intervention slices (the
/// unmapped-users gate, the tightening-relaxation gate) and reused by the
/// config wizard.

/// A single operator choice at an intervention point. `Code` is a Voice /
/// diagnostic code (never prose); `Label` is the already-resolved
/// operator-facing line (resolved through `Voice` at the call site); `Value`
/// is the typed outcome the choice carries.
type Choice<'T> =
    { Code  : string
      Label : string
      Value : 'T }

/// The outcome of an intervention point.
type Decision<'T> =
    /// The operator picked an option at a real terminal.
    | Chosen of 'T
    /// Non-interactive (piped / CI / no TTY): the named fallback outcome was
    /// taken. `fallbackCode` is the fallback `Choice`'s code so the caller can
    /// emit the corresponding named refusal / note — never a silent downgrade.
    | Degraded of value: 'T * fallbackCode: string

/// True iff an intervention may pause for input: stderr is a real terminal
/// (prompts draw on channel 2, mirroring `TtyRenderer.shouldRender`) AND stdin
/// is a real terminal (a keystroke can be read). A headless / piped / CI run is
/// never blocked on stdin.
let isInteractive () : bool =
    not Console.IsErrorRedirected && not Console.IsInputRedirected

/// The stderr console (channel 2). Mirrors `TtyRenderer`'s console creation so
/// an intervention prompt renders where the decision surface lives.
let private stderrConsole () : IAnsiConsole =
    AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(Console.Error)))

/// Choice gate with the console + interactivity flag INJECTED — the testable
/// core (drive it with `Spectre.Console.Testing.TestConsole`). When
/// `interactive` is false the console is never touched and the named fallback
/// is returned.
let chooseOn
    (console: IAnsiConsole)
    (interactive: bool)
    (title: string)
    (choices: Choice<'T> list)
    (fallback: Choice<'T>)
    : Decision<'T> =
    if not interactive then
        Degraded(fallback.Value, fallback.Code)
    else
        let prompt = SelectionPrompt<Choice<'T>>()
        prompt.Title <- title
        prompt.Converter <- Func<Choice<'T>, string>(fun c -> c.Label)
        for c in choices do prompt.AddChoice c |> ignore
        let picked = AnsiConsoleExtensions.Prompt(console, prompt)
        Chosen picked.Value

/// Present a choice at an intervention point. Non-interactive → the named
/// fallback. `title` and each `Choice.Label` are caller-resolved copy.
let chooseOrDefault
    (title: string)
    (choices: Choice<'T> list)
    (fallback: Choice<'T>)
    : Decision<'T> =
    chooseOn (stderrConsole ()) (isInteractive ()) title choices fallback

/// Value-input gate with the console + interactivity flag INJECTED — the
/// testable core. Spectre re-asks (via the validator) until `parse` succeeds;
/// non-interactive returns the named fallback value. The rare legible case
/// (e.g. a path); most intervention points are choices, not free input.
let promptValueOn
    (console: IAnsiConsole)
    (interactive: bool)
    (title: string)
    (invalid: string)
    (parse: string -> 'T option)
    (fallback: 'T)
    (fallbackCode: string)
    : Decision<'T> =
    if not interactive then
        Degraded(fallback, fallbackCode)
    else
        let prompt = TextPrompt<string>(title)
        prompt.Validator <-
            Func<string, ValidationResult>(fun s ->
                match parse s with
                | Some _ -> ValidationResult.Success()
                | None   -> ValidationResult.Error(invalid))
        let raw = AnsiConsoleExtensions.Prompt(console, prompt)
        match parse raw with
        | Some v -> Chosen v
        | None   -> Degraded(fallback, fallbackCode)

/// Prompt for a typed value at an intervention point. Non-interactive → the
/// named fallback value.
let promptValueOrDefault
    (title: string)
    (invalid: string)
    (parse: string -> 'T option)
    (fallback: 'T)
    (fallbackCode: string)
    : Decision<'T> =
    promptValueOn (stderrConsole ()) (isInteractive ()) title invalid parse fallback fallbackCode
