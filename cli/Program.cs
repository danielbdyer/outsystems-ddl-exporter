using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

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
        return Run(args, Console.OpenStandardOutput(), () => Checkout.Here(Contract.Version), Contract.Verbs, interruption);
    }

    /// <summary>Answers <paramref name="args"/> on <paramref name="output"/>, as Markdown or, with --json, as one JSON object, for the checkout of the working directory; returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, Stream output) => Run(args, output, () => Checkout.Here(Contract.Version), Contract.Verbs);

    /// <summary>Answers <paramref name="args"/> on <paramref name="output"/> for the SSDT repository checkout <paramref name="here"/>; returns the exit code.</summary>
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
    /// verb's body runs; returns the exit code. The global switches are --json, --summary and --timeout <seconds>. A verb's body runs with
    /// the run's query log begun, and its answer is cut to the default form (Render.Cut), the whole answer written to the run's
    /// answer.json when anything was left out. The whole command runs inside one catch: the checkout, the --help renderers, the verb's
    /// body, the rendering and the write. A run the interruption stopped (a signal, or --timeout) is answered as interrupted at exit 130,
    /// once the verb has released its locks and ended the programs it started. Any other exception there is answered as internal.unexpected
    /// at its category's exit (Contract.Unexpected), its message withheld when the run read a named environment's connection or other
    /// reference (SqlServer.Reads), and that exit is returned even when writing that answer fails as well, since standard output is then all
    /// the answer had.
    /// </summary>
    private static int Run(IReadOnlyList<string> args, Stream output, Func<Checkout> here, IReadOnlyList<Verb> verbs, Interruption interruption)
    {
        using var reads = SqlServer.Reads.Begin();
        var (json, summary, word) = (false, false, "");
        Verb? verb = null;
        try
        {
            (json, summary) = (args.Contains("--json"), args.Contains("--summary"));
            var words = Words(args);
            if (words.Length == 0 || words.Contains("--help"))
            {
                Write.Text(output, json ? Render.JsonText(Render.Help()) : Render.HelpMarkdown());
                return words.Length == 0 ? 1 : 0;
            }

            (word, verb) = (words[0], verbs.FirstOrDefault(v => v.Name == words[0]));
            var timeout = Timeout(args);
            var answer = verb is null ? Contract.UnknownVerb(word)
                : timeout is Result<TimeSpan?>.Failed { Error: var badTimeout } ? Contract.Failed(verb, badTimeout)
                : Answered(verb, here, words[1..], ((Result<TimeSpan?>.Ok)timeout).Value, interruption, summary);
            Write.Text(output, Rendered(answer, json));
            return answer.Exit;
        }
        catch (OperationCanceledException) when (interruption.Token.IsCancellationRequested)
        {
            var stopped = Contract.Exits.Single(e => e.Name == "interrupted");
            var command = word.Length == 0 ? "dbchange" : "dbchange " + word;
            var answer = Contract.Answer(verb?.Output ?? "dbchange.envelope/1", Outcome.Of(stopped), stopped.Code,
                command + " stopped after " + (interruption.Cause ?? "an interruption") + ": it ended the programs it had started and released its locks.", []);
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

            return Contract.ExitByCategory(ErrorCategory.Internal);
        }
    }

    /// <summary>
    /// A verb's answer for the run: --timeout begun just before the body and counted from there; the body run with the run's query log;
    /// the answer cut to the default form or the summary; and, when anything was left out, the whole answer written to the run's
    /// answer.json, which the cut answer names as full. A write the file system refuses leaves full null and adds the note
    /// run.full-unwritten, so the answer on standard output says so instead of being lost.
    /// </summary>
    private static Envelope Answered(Verb verb, Func<Checkout> here, IReadOnlyList<string> words, TimeSpan? timeout, Interruption interruption, bool summary)
    {
        if (timeout is { } after)
        {
            interruption.After(after);
        }

        if (verb.Body is null)
        {
            return Contract.NotBuilt(verb);
        }

        var checkout = here();
        var run = SqlServer.QueryLog.Start(checkout.Root);
        var answer = verb.Body(checkout, run, words);
        var shown = Render.Cut(answer, summary);
        if (!shown.Truncated)
        {
            return answer;
        }

        var full = Path.Combine(Path.GetDirectoryName(run.Path)!, "answer.json");
        var named = Path.GetRelativePath(checkout.Root, full).Replace('\\', '/');
        return Write.Text(full, Render.JsonText(Render.Json(answer))).Match(
            _ => shown with { Full = named },
            error => shown with
            {
                Findings = [.. shown.Findings, Finding.Note("run.full-unwritten", named, "The answer was cut to its first entries, and the whole answer could not be written to " + named + ": " + error.Message)],
            });
    }

    /// <summary>The arguments less the global switches --json and --summary, and less --timeout with its value, which every verb takes and none reads.</summary>
    private static string[] Words(IReadOnlyList<string> args)
    {
        var words = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--timeout")
            {
                i++;
            }
            else if (args[i] is not ("--json" or "--summary"))
            {
                words.Add(args[i]);
            }
        }

        return [.. words];
    }

    private static string Rendered(Envelope answer, bool json) => json ? Render.JsonText(Render.Json(answer)) : Render.Markdown(answer);
}
