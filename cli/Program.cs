using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Estate.Io;
using Estate.Kernel;

namespace Estate.Cli;

/// <summary>The one executable: parse the arguments, answer from the verb table, render.</summary>
public static class Program
{
    /// <summary>
    /// The telemetry opt-out is the first statement, before anything can load DacFx; Program has no static state to run ahead of it. The run
    /// then listens for Ctrl-C, Ctrl-Break, SIGTERM and SIGHUP (io/Interruption), which stop a verb at its next program or lock wait.
    /// </summary>
    public static int Main(string[] args)
    {
        Telemetry.OptOut();
        using var interruption = Interruption.Listen();
        return Run(args, Console.OpenStandardOutput(), Checkout.Here, Contract.Verbs, interruption);
    }

    /// <summary>Answers <paramref name="args"/> on <paramref name="output"/>, as Markdown or, with --json, as one JSON object, for the checkout of the working directory; returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, Stream output) => Run(args, output, Checkout.Here, Contract.Verbs);

    /// <summary>Answers <paramref name="args"/> on <paramref name="output"/> for the estate checkout <paramref name="here"/>; returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, Stream output, Checkout here) => Run(args, output, () => here, Contract.Verbs);

    /// <summary>A run inside another process (a test's): no signal reaches it, and --timeout alone interrupts it.</summary>
    internal static int Run(IReadOnlyList<string> args, Stream output, Func<Checkout> here, IReadOnlyList<Verb> verbs)
    {
        using var interruption = Interruption.Quiet();
        return Run(args, output, here, verbs, interruption);
    }

    /// <summary>--timeout &lt;seconds&gt; among the arguments, as a duration; null when absent; arguments.timeout for a value that is missing or is not a whole number of seconds from 1 to 86400.</summary>
    public static Result<TimeSpan?> Timeout(IReadOnlyList<string> args)
    {
        var at = args.ToList().IndexOf("--timeout");
        return at < 0 ? Result.Ok<TimeSpan?>(null)
            : at + 1 < args.Count && int.TryParse(args[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 1 and <= 86400 ? TimeSpan.FromSeconds(seconds)
            : new Error("arguments.timeout", "--timeout takes a whole number of seconds from 1 to 86400.", "Write the timeout in seconds, such as --timeout 600.");
    }

    /// <summary>
    /// Answers <paramref name="args"/> from <paramref name="verbs"/> for the checkout <paramref name="here"/> gives, asked for only when a
    /// verb's body runs; returns the exit code. The whole command runs inside one catch: the checkout, the --help renderers, the verb's
    /// body, the rendering and the write. A run the interruption stopped (a signal, or --timeout) is answered as interrupted at exit 130,
    /// once the verb has released its locks and ended the programs it started. Any other exception there is answered as internal.unexpected
    /// at exit 6 (Contract.Unexpected), its message withheld when the run read a named environment's connection or other reference
    /// (SqlServer.Reads), and exit 6 is returned even when writing that answer fails as well, since standard output is then all the answer had.
    /// </summary>
    private static int Run(IReadOnlyList<string> args, Stream output, Func<Checkout> here, IReadOnlyList<Verb> verbs, Interruption interruption)
    {
        using var reads = SqlServer.Reads.Begin();
        var (json, word) = (false, "");
        Verb? verb = null;
        try
        {
            json = args.Contains("--json");
            var words = Words(args);
            if (words.Length == 0 || words.Contains("--help"))
            {
                Write.Text(output, json ? Io.Json.Text(Render.Help()) : Render.HelpMarkdown());
                return words.Length == 0 ? 1 : 0;
            }

            (word, verb) = (words[0], verbs.FirstOrDefault(v => v.Name == words[0]));
            var timeout = Timeout(args);
            var answer = verb is null ? Contract.UnknownVerb(word)
                : timeout is Result<TimeSpan?>.Failed { Error: var badTimeout } ? Contract.Failed(verb, badTimeout)
                : Answered(verb, here, words[1..], ((Result<TimeSpan?>.Ok)timeout).Value, interruption);
            Write.Text(output, Rendered(answer, json));
            return answer.Exit;
        }
        catch (OperationCanceledException) when (interruption.Token.IsCancellationRequested)
        {
            var stopped = Contract.Exits.Single(e => e.Name == "interrupted");
            var command = word.Length == 0 ? "estate" : "estate " + word;
            var answer = Contract.Answer(verb?.Output ?? "estate.envelope/1", stopped.Name,
                command + " stopped after " + (interruption.Cause ?? "an interruption") + ": it ended the programs it had started and released its locks.", [], stopped.Code);
            try
            {
                Write.Text(output, Rendered(answer, json));
            }
            catch (Exception)
            {
                // Standard output refused the answer too; the exit code is what still reaches the caller.
            }

            return stopped.Code;
        }
        catch (Exception e)
        {
            try
            {
                Write.Text(output, Rendered(Contract.Unexpected(verb, word, e, withheld: reads.NamedEnvironment), json));
            }
            catch (Exception)
            {
                // Standard output refused the answer too; the exit code is what still reaches the caller.
            }

            return Contract.Defect;
        }
    }

    /// <summary>The verb's body, or its not-built answer, with --timeout begun just before it and counted from there.</summary>
    private static Envelope Answered(Verb verb, Func<Checkout> here, IReadOnlyList<string> words, TimeSpan? timeout, Interruption interruption)
    {
        if (timeout is { } after)
        {
            interruption.After(after);
        }

        return verb.Body is null ? Contract.NotBuilt(verb) : verb.Body(here(), words);
    }

    /// <summary>The arguments less --json and less --timeout with its value, which every verb takes and none reads.</summary>
    private static string[] Words(IReadOnlyList<string> args)
    {
        var words = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--timeout")
            {
                i++;
            }
            else if (args[i] != "--json")
            {
                words.Add(args[i]);
            }
        }

        return [.. words];
    }

    private static string Rendered(Envelope answer, bool json) => json ? Io.Json.Text(Render.Json(answer)) : Render.Markdown(answer);
}
