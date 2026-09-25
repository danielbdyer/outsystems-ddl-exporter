using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Estate.Io;
using Estate.Kernel;

namespace Estate.Cli;

/// <summary>The one executable: parse the arguments, answer from the verb table, render.</summary>
public static class Program
{
    /// <summary>The telemetry opt-out is the first statement, before anything can load DacFx; Program has no static state to run ahead of it.</summary>
    public static int Main(string[] args)
    {
        Telemetry.OptOut();
        return Run(args, Console.OpenStandardOutput());
    }

    /// <summary>Answers <paramref name="args"/> on <paramref name="output"/>, as Markdown or, with --json, as one JSON object, for the checkout of the working directory; returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, Stream output) => Run(args, output, Checkout.Here, Contract.Verbs);

    /// <summary>Answers <paramref name="args"/> on <paramref name="output"/> for the estate checkout <paramref name="here"/>; returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, Stream output, Checkout here) => Run(args, output, () => here, Contract.Verbs);

    /// <summary>
    /// Answers <paramref name="args"/> from <paramref name="verbs"/> for the checkout <paramref name="here"/> gives, asked for only when a
    /// verb's body runs; returns the exit code. The global switches are --json and --summary. A verb's body runs with the run's query
    /// log begun, and its answer is cut to the default form (Render.Cut), the whole answer written to the run's answer.json when anything
    /// was left out. The whole command runs inside one catch: the checkout, the --help renderers, the verb's body, the rendering and the
    /// write. An exception there is answered as internal.unexpected at its category's exit (Contract.Unexpected), its message withheld
    /// when the run read a named environment's connection or other reference (SqlServer.Reads), and that exit is returned even when
    /// writing that answer fails as well, since standard output is then all the answer had.
    /// </summary>
    internal static int Run(IReadOnlyList<string> args, Stream output, Func<Checkout> here, IReadOnlyList<Verb> verbs)
    {
        using var reads = SqlServer.Reads.Begin();
        var (json, summary, word) = (false, false, "");
        Verb? verb = null;
        try
        {
            (json, summary) = (args.Contains("--json"), args.Contains("--summary"));
            var words = args.Where(a => a is not ("--json" or "--summary")).ToArray();
            if (words.Length == 0 || words.Contains("--help"))
            {
                Write.Text(output, json ? Render.JsonText(Render.Help()) : Render.HelpMarkdown());
                return words.Length == 0 ? 1 : 0;
            }

            (word, verb) = (words[0], verbs.FirstOrDefault(v => v.Name == words[0]));
            var answer = verb is null ? Contract.UnknownVerb(word) : verb.Body is null ? Contract.NotBuilt(verb) : Answered(verb, here(), words[1..], summary);
            Write.Text(output, Rendered(answer, json));
            return answer.Exit;
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
    /// A verb's answer for the run: its body run with the run's query log, the answer cut to the default form or the summary, and, when
    /// anything was left out, the whole answer written to the run's answer.json, which the cut answer names as full; a write the file
    /// system refuses leaves full null and adds the note run.full-unwritten, so the answer on standard output says so instead of being lost.
    /// </summary>
    private static Envelope Answered(Verb verb, Checkout checkout, IReadOnlyList<string> words, bool summary)
    {
        var run = checkout.Run;
        var answer = verb.Body!(checkout with { Log = run }, words);
        var shown = Render.Cut(answer, summary);
        if (!shown.Truncated)
        {
            return answer;
        }

        var full = Path.Combine(Path.GetDirectoryName(run.Path)!, "answer.json");
        var named = Path.GetRelativePath(checkout.Root, full).Replace('\\', '/');
        try
        {
            Write.Text(full, Render.JsonText(Render.Json(answer)));
            return shown with { Full = named };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return shown with
            {
                Findings = [.. shown.Findings, Finding.Note("run.full-unwritten", named, "The answer was cut to its first entries, and the whole answer could not be written to " + named + " (" + e.GetType().Name + ").")],
            };
        }
    }

    private static string Rendered(Envelope answer, bool json) => json ? Render.JsonText(Render.Json(answer)) : Render.Markdown(answer);
}
