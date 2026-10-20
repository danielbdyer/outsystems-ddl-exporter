using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Data.SqlClient.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// DacFx's probe of the server's host, refused before SqlClient sends it (R14, the operator's ruling of 2026-10-20). DacFx 170.5.96's
/// ReliableConnectionHelper.GetServerPropertiesFromConnection sends <c>exec xp_msver @optname=N'Platform', …</c> on any connection to an
/// engine edition of 1, 2, 3, 4 or 8 when its per-process cache (MemoryCache 'ServerAndDatabaseInfo', keyed by data source and initial
/// catalog) holds no entry; the values feed only DacFxOperationTelemetry, which io/Telemetry turns off, and DacFx runs the call inside
/// catch (Exception). The observer here watches SqlClient's WriteCommandBefore event, which SqlClient raises on the calling thread before
/// it sends a command, and throws for exactly the commands Refuses names; it never alters a command, reads no parameter, logs nothing
/// and touches nothing on the server. The exception is <see cref="Refused"/>, which io/DacFx recognises anywhere in an exception's chain
/// and maps to dacfx.failed naming the call, so a DacFx release that stops catching it, or wraps it, fails the read, never silently.
/// </summary>
public static class ServerProbe
{
    /// <summary>The name SqlClient gives the DiagnosticListener its command events are written to.</summary>
    private const string Source = "SqlClientDiagnosticListener";

    private const string Name = "xp_msver";

    private static int installed;

    /// <summary>
    /// The observer, subscribed once per process to every SqlClient listener the process creates; a second call does nothing. cli calls
    /// it in Main, beside the telemetry opt-out: a listener sees every command of the process, and only in dbchange's own process is
    /// every connection one that dbchange opens. A test host that runs a verb inside itself (ScratchRepository.RunAt) keeps DacFx's call,
    /// since its own fixtures publish through DacFx on connections dbchange never opened.
    /// </summary>
    public static void Refuse()
    {
        if (Interlocked.Exchange(ref installed, 1) == 0)
        {
            DiagnosticListener.AllListeners.Subscribe(new Listeners());
        }
    }

    /// <summary>
    /// Whether a command is a call of xp_msver alone: a stored-procedure command whose name's last part is xp_msver, or a text batch
    /// whose one statement executes a procedure of that name. A text that does not name xp_msver is never parsed, a text that does not
    /// parse passes (SQL Server refuses it itself), and so does every other command.
    /// </summary>
    internal static bool Refuses(CommandType type, string text) => type switch
    {
        CommandType.StoredProcedure => TSql.NameParts(text) is [.., var last] && Is(last),
        CommandType.Text when text.Contains(Name, StringComparison.OrdinalIgnoreCase) =>
            TSql.Parse(text, out var errors) is TSqlScript script && errors.Count == 0
            && script.Batches.SelectMany(b => b.Statements).ToList() is [ExecuteStatement { ExecuteSpecification.ExecutableEntity:
                ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value: var called } }]
            && Is(called),
        _ => false,
    };

    /// <summary>The exception the observer throws, its message naming the call and the rule that refuses it.</summary>
    internal static Refused Refusal() =>
        new("dbchange refused DacFx's EXEC of xp_msver before SqlClient sent it: the principal that reads an environment sends no EXEC "
            + "(R14). DacFx asks xp_msver for the host's platform, processor count and memory, for its telemetry alone.");

    /// <summary>
    /// The refusal of DacFx's xp_msver call. DacFx 170.5.96 catches it; one that lets it out, wrapped or not, fails as dacfx.failed with
    /// <see cref="Remedy"/>, since no change to the project or the publish profile clears it.
    /// </summary>
    public sealed class Refused(string message) : InvalidOperationException(message)
    {
        /// <summary>What a reader does when DacFx does not catch the refusal.</summary>
        public const string Remedy = "The DacFx release dbchange runs no longer catches the refusal of its xp_msver call: keep the DacFx "
            + "pin in dbchange/ledgers/toolchain.md at 170.5.96 until io/ServerProbe is revisited for the newer release.";
    }

    private static bool Is(string name) => string.Equals(name, Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Subscribes the command observer to SqlClient's listener, for its WriteCommandBefore event alone.</summary>
    private sealed class Listeners : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == Source)
            {
                value.Subscribe(new Commands(), name => name == SqlClientCommandBefore.Name);
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }

    /// <summary>Throws the refusal for a command Refuses names, and returns for every other.</summary>
    private sealed class Commands : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is SqlClientCommandBefore { Command: { } command } && Refuses(command.CommandType, command.CommandText))
            {
                throw Refusal();
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
