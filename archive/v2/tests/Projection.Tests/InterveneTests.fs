module Projection.Tests.InterveneTests

open System.IO
open Xunit
open Spectre.Console
open Projection.Cli

/// The operator-intervention seam (`Intervene.fs`). These tests pin the
/// SAFETY-CRITICAL contract: a non-interactive run (piped / CI / no TTY) is
/// never blocked on stdin — it takes the named fallback (an already-expressible
/// outcome), never prompts. The interactive `Chosen` path (a real
/// `SelectionPrompt` driven by pushed keystrokes) is exercised when the first
/// intervention slice wires it, via `Spectre.Console.Testing.TestConsole`.

/// A console whose output is discarded — for the non-interactive cases, where
/// the seam must NOT touch the console at all.
let private quietConsole () : IAnsiConsole =
    AnsiConsole.Create(
        AnsiConsoleSettings(
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = AnsiConsoleOutput(new StringWriter())))

/// The unmapped-users gate's shape (the canonical first consumer): three
/// affirmative choices over a named halt fallback.
let private choices : Intervene.Choice<int> list =
    [ { Code = "intervene.test.map";    Label = "Map the users, then halt";        Value = 1 }
      { Code = "intervene.test.assign"; Label = "Assign the system user";          Value = 2 }
      { Code = "intervene.test.accept"; Label = "Accept the loss and continue";    Value = 3 } ]

let private fallback : Intervene.Choice<int> =
    { Code = "intervene.test.halt"; Label = "Halt"; Value = 0 }

[<Fact>]
let ``chooseOn non-interactive degrades to the named fallback (headless never blocks)`` () =
    match Intervene.chooseOn (quietConsole ()) false "title" choices fallback with
    | Intervene.Degraded(value, code) ->
        Assert.Equal(0, value)
        Assert.Equal("intervene.test.halt", code)
    | Intervene.Chosen _ ->
        Assert.True(false, "a non-interactive intervention must not prompt")

[<Fact>]
let ``promptValueOn non-interactive degrades to the named fallback value`` () =
    let parse (s: string) =
        match System.Int32.TryParse s with
        | true, n -> Some n
        | _       -> None
    match Intervene.promptValueOn (quietConsole ()) false "title" "invalid" parse 42 "intervene.test.defaultValue" with
    | Intervene.Degraded(value, code) ->
        Assert.Equal(42, value)
        Assert.Equal("intervene.test.defaultValue", code)
    | Intervene.Chosen _ ->
        Assert.True(false, "a non-interactive value prompt must not read stdin")

[<Fact>]
let ``isInteractive is false under a redirected test harness (the headless contract)`` () =
    // `dotnet test` redirects stdin/stderr; the gate must read false so a
    // headless / CI run can never block on a prompt — it always falls to the
    // named fallback. This is the structural guarantee the slices rely on.
    Assert.False(Intervene.isInteractive ())
