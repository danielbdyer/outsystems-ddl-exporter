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

    /// <summary>Answers <paramref name="args"/> on <paramref name="output"/>, as Markdown or, with --json, as one JSON object; returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, Stream output)
    {
        var json = args.Contains("--json");
        var words = args.Where(a => a != "--json").ToArray();
        if (words.Length == 0 || words.Contains("--help"))
        {
            Write.Text(output, json ? Io.Json.Text(Render.Help()) : Render.HelpMarkdown());
            return words.Length == 0 ? 1 : 0;
        }

        var verb = Contract.Verbs.FirstOrDefault(v => v.Name == words[0]);
        var answer = verb is null ? Contract.UnknownVerb(words[0]) : verb.Body is null ? Contract.NotBuilt(verb) : verb.Body(words[1..]);
        Write.Text(output, json ? Io.Json.Text(Render.Json(answer)) : Render.Markdown(answer));
        return answer.Exit;
    }
}
