using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace DbChangeLite;

public static class Program
{
    public static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT", "1");
        Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
        ServerProbe.Install();
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        using var terminated = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cancel.Cancel();
        });
        return Run(args, Directory.GetCurrentDirectory(), Console.Out, DateTimeOffset.UtcNow, cancel.Token);
    }

    public static int Run(IReadOnlyList<string> args, string workingDirectory, TextWriter output, DateTimeOffset at, CancellationToken cancel)
    {
        if (args.Count > 0 && args[0] == "--help")
        {
            output.Write(args.Count > 1 && args[1] == "--json" ? JsonSerializer.Serialize(Contract.HelpAsData(), Answer.Json) + "\n" : Contract.Help());
            return 0;
        }

        if (args.Count > 0 && args[0] == "--version")
        {
            output.Write("dbchange-lite " + Contract.Version + "\n");
            return 0;
        }

        Invocation invocation;
        try
        {
            invocation = Contract.Parse(args);
        }
        catch (Stop stop)
        {
            output.Write(DbChangeLite.Answer.Before("dbchange-lite.arguments/1", stop).Text());
            return stop.Outcome.Exit;
        }

        Run run;
        try
        {
            run = new Run(workingDirectory, invocation.Verb.Name, at, cancel);
        }
        catch (Exception e) when (e is Stop or IOException or UnauthorizedAccessException)
        {
            var stop = e as Stop ?? Stop.Of("configuration-refused", "run.folder", workingDirectory, "The run folder under .dbchange/runs cannot be written: " + e.Message,
                "Make .dbchange/runs under the repository root a writable folder, then run the verb again.");
            output.Write(DbChangeLite.Answer.Before("dbchange-lite." + invocation.Verb.Name + "/1", stop).Text());
            return stop.Outcome.Exit;
        }

        var answer = Answered(invocation, run).Written(run);
        output.Write(answer.Text());
        return answer.Exit;
    }

    private static Answer Answered(Invocation invocation, Run run)
    {
        try
        {
            return invocation.Verb.Name switch
            {
                "doctor" => Doctor.Run(run),
                "drift" => Drift.Run(invocation, run),
                "diff" => Diff.Run(invocation, run),
                "predict" => Predict.Run(invocation, run),
                "prove" => Prove.Run(invocation, run),
                _ => throw new InvalidOperationException(invocation.Verb.Name),
            };
        }
        catch (Stop stop)
        {
            return DbChangeLite.Answer.Stopped(invocation.Verb.Name, stop, run);
        }
        catch (OperationCanceledException)
        {
            return DbChangeLite.Answer.Of(invocation.Verb.Name, Contract.Of("interrupted"), "Interrupted; the cleanup ran.", [], run);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            File.WriteAllText(Path.Combine(run.Folder, "error.txt"), e.ToString());
            return DbChangeLite.Answer.Of(invocation.Verb.Name, Contract.Of("configuration-refused"), "dbchange-lite failed: " + e.GetType().Name + ": " + e.Message,
                [Finding.Error("internal.unexpected", invocation.Verb.Name, e.GetType().Name + ": " + e.Message, "Report this answer and its run folder, which holds error.txt, with the command that produced it.")], run);
        }
    }
}
