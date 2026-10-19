using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace DbChange.Io;

/// <summary>
/// One run's interruption (VALUES.md O7; cli/Contract.cs exit 130): Ctrl-C (SIGINT), Ctrl-Break (SIGQUIT), SIGTERM and SIGHUP (on
/// Windows, the console closing) cancel <see cref="Token"/>, and so does --timeout through <see cref="After"/>. The run's token is
/// current for the code the run calls, as SqlServer.Reads' record is (an AsyncLocal, so it reaches the threads the run's work starts):
/// every program io starts (Command) and every lock it waits for (FileLock) watches <see cref="RunToken"/> beside the token its caller
/// passes, so a verb stops at its next program or lock wait and cli answers interrupted at exit 130. The first interruption is taken
/// (a signal's Cancel = true); a signal after it, the timeout included, is left to the runtime's default handling, which ends the
/// process at once, so a person can always stop dbchange. Listen registers the signals; Quiet registers none, for tests and for a run inside another process.
/// </summary>
public sealed class Interruption : IDisposable
{
    private static readonly AsyncLocal<Interruption?> Current = new();

    private static readonly (PosixSignal Signal, string Name)[] Signals =
        [(PosixSignal.SIGINT, "Ctrl-C"), (PosixSignal.SIGQUIT, "Ctrl-Break"), (PosixSignal.SIGTERM, "SIGTERM"), (PosixSignal.SIGHUP, "SIGHUP")];

    private readonly CancellationTokenSource source = new();
    private readonly Interruption? outer;
    private readonly PosixSignalRegistration[] registrations;
    private string? cause;
    private string? timeoutCause;

    private Interruption(bool listening)
    {
        outer = Current.Value;
        Current.Value = this;
        registrations = listening ? Array.ConvertAll(Signals, s => PosixSignalRegistration.Create(s.Signal, context => Signalled(context, s.Name))) : [];
    }

    /// <summary>The current run's token; a token never cancelled outside any run.</summary>
    public static CancellationToken RunToken => Current.Value?.source.Token ?? CancellationToken.None;

    public CancellationToken Token => source.Token;

    /// <summary>
    /// What interrupted the run ("Ctrl-C", "SIGTERM", "--timeout 600"), or null while nothing has. A signal records itself before it
    /// cancels, so a cancellation no signal recorded is the timeout's: a wait the cancellation wakes reads its cause whatever order the
    /// token runs its callbacks in, newest first.
    /// </summary>
    public string? Cause => cause ?? (source.IsCancellationRequested ? timeoutCause : null);

    /// <summary>The run of dbchange's own process: the signals registered, the first one taken.</summary>
    public static Interruption Listen() => new(listening: true);

    /// <summary>A run that no signal reaches, stopped only by After: a test's, or one inside another process.</summary>
    public static Interruption Quiet() => new(listening: false);

    /// <summary>--timeout: the run is interrupted once <paramref name="timeout"/> has passed, unless a signal came first.</summary>
    public void After(TimeSpan timeout)
    {
        timeoutCause = "--timeout " + ((long)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        source.CancelAfter(timeout);
    }

    public void Dispose()
    {
        Array.ForEach(registrations, r => r.Dispose());
        Current.Value = outer;
        source.Dispose();
    }

    private void Signalled(PosixSignalContext context, string name)
    {
        if (!source.IsCancellationRequested && Interlocked.CompareExchange(ref cause, name, null) is null)
        {
            context.Cancel = true;
            source.Cancel();
        }
    }
}
