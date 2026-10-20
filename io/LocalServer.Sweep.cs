using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using DbChange.Kernel;

namespace DbChange.Io;

public static partial class LocalServer
{
    /// <summary>
    /// How old a registered copy is before the sweep drops it (VALUES.md O5): a day, so a copy another run on this machine still holds is
    /// never dropped under it, and a killed run's copy goes with the first run a day later.
    /// </summary>
    internal static readonly TimeSpan SweptAfter = TimeSpan.FromDays(1);

    /// <summary>How long a sweep waits for another run's sweep, which drops each of its copies within its DROP's timeout.</summary>
    private static readonly TimeSpan SweepTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The sweep (VALUES.md O5): each copy <see cref="Stale"/> chooses from .dbchange/copies.json dropped as Drop drops one, its row with
    /// it; the names dropped. It runs before a copy is made, on the server that copy is made on, with the clock its caller gives. Where
    /// the registry holds a stale row, the run takes .dbchange/sweep.lock, reads the rows again under it and drops them under it, so a
    /// run that sweeps at the same time waits and then finds those rows gone; Unmake's application lock keeps a DROP apart from a Drop
    /// of the same copy outside the sweep.
    /// </summary>
    internal static Result<IReadOnlyList<CopyName>> Swept(string repositoryRoot, string server, Kernel.ServerName name, DateTimeOffset now,
        SqlServer.QueryLog log)
    {
        var state = new LocalState(repositoryRoot);
        return Rows(repositoryRoot).Bind(seen => Stale(seen, name, now).Count == 0 ? Result.Ok<IReadOnlyList<CopyName>>([])
            : state.Made(state.Folder).Bind(_ => FileLock.Take(state.SweepLock, SweepTimeout)).Bind(held =>
            {
                using (held)
                {
                    return Rows(repositoryRoot).Bind(rows => Result.All(Stale(rows, name, now)
                        .Select(stale => Drop(new SqlServer.Copy(stale, server, repositoryRoot), log))));
                }
            }));
    }

    /// <summary>
    /// The registry's rows the sweep drops: a name dbchange gives a copy, made on this machine, on <paramref name="server"/> (the local
    /// server as this machine names it now; a copy on another server cannot be dropped from here), registered more than
    /// <see cref="SweptAfter"/> before <paramref name="now"/>. A row whose time is missing or unreadable, which no run of dbchange wrote,
    /// counts as old. Nothing the registry does not hold is ever chosen.
    /// </summary>
    internal static IReadOnlyList<CopyName> Stale(IEnumerable<JsonObject> rows, Kernel.ServerName server, DateTimeOffset now)
    {
        var machine = CopyName.Make(Environment.MachineName, 0, 0).Machine;
        return [.. rows.Where(row => (string?)row["server"] == server.ToString()
                && (row["created"] is not JsonValue created || !created.TryGetValue(out string? text)
                    || !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var at)
                    || now - at > SweptAfter))
            .Select(row => CopyName.Of("a copy .dbchange/copies.json registers", (string?)row["name"]))
            .OfType<Result<CopyName>.Ok>().Select(named => named.Value).Where(named => named.Machine == machine)];
    }

    /// <summary>
    /// The Facsimile's TLC-10: a connect to the local server that fails before the connection opens (server.unreachable, the connection
    /// never opened) is tried once more after <see cref="Pause"/>, since LocalDB's first connection can time out while the instance starts;
    /// this holds until the server has first answered this process, by a connection that opened or by an error of its own such as a
    /// refused login. A failure after the connection opened, a severity of 20 or more among them, is never retried, since the server may
    /// have run the statement, and once the server has answered no later connect is retried.
    /// </summary>
    internal sealed class FirstConnect
    {
        /// <summary>How long the retry waits: long enough for an instance that is starting to listen.</summary>
        internal static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

        private int answered;

        /// <summary>The one this process's statements to the local server share (io/SqlServer.Query).</summary>
        internal static FirstConnect OfProcess { get; } = new();

        /// <summary>
        /// The statement's answer, <paramref name="attempt"/> saying with it whether its connection opened; the wait watches the run's
        /// interruption (Interruption.RunToken) and throws OperationCanceledException when it ends.
        /// </summary>
        internal Result<T> Attempt<T>(Func<(Result<T> Answer, bool Opened)> attempt, TimeSpan pause)
        {
            var sent = attempt();
            if (Unanswered(sent) && Volatile.Read(ref answered) == 0)
            {
                if (Interruption.RunToken.WaitHandle.WaitOne(pause))
                {
                    throw new OperationCanceledException("The run was interrupted before the local server was asked again.",
                        Interruption.RunToken);
                }

                sent = attempt();
            }

            if (!Unanswered(sent))
            {
                Volatile.Write(ref answered, 1);
            }

            return sent.Answer;
        }

        /// <summary>A connect the server never answered: server.unreachable before the connection opened.</summary>
        private static bool Unanswered<T>((Result<T> Answer, bool Opened) sent) =>
            !sent.Opened && sent.Answer is Result<T>.Failed { Error.Code: "server.unreachable" };
    }
}
