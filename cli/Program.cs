using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Estate.Io;

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
    /// verb's body runs; returns the exit code. The whole command runs inside one catch: the checkout, the --help renderers, the verb's
    /// body, the rendering and the write. An exception there is answered as internal.unexpected at exit 6 (Contract.Unexpected), its
    /// message withheld when the run read a named environment's connection or other reference (SqlServer.Reads), and exit 6 is returned
    /// even when writing that answer fails as well, since standard output is then all the answer had.
    /// </summary>
    internal static int Run(IReadOnlyList<string> args, Stream output, Func<Checkout> here, IReadOnlyList<Verb> verbs)
    {
        using var reads = SqlServer.Reads.Begin();
        var (json, word) = (false, "");
        Verb? verb = null;
        try
        {
            json = args.Contains("--json");
            var words = args.Where(a => a != "--json").ToArray();
            if (words.Length == 0 || words.Contains("--help"))
            {
                Write.Text(output, json ? Io.Json.Text(Render.Help()) : Render.HelpMarkdown());
                return words.Length == 0 ? 1 : 0;
            }

            (word, verb) = (words[0], verbs.FirstOrDefault(v => v.Name == words[0]));
            var answer = verb is null ? Contract.UnknownVerb(word) : verb.Body is null ? Contract.NotBuilt(verb) : verb.Body(here(), words[1..]);
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

            return Contract.ExitByCategory(Estate.Kernel.ErrorCategory.Internal);
        }
    }

    private static string Rendered(Envelope answer, bool json) => json ? Io.Json.Text(Render.Json(answer)) : Render.Markdown(answer);
}
