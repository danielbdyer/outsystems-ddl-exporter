using System;
using System.Collections.Generic;
using System.Threading;
using Estate.Io;
using Xunit.Sdk;

namespace Estate.Budgets.Tests;

/// <summary>
/// The tests' own program runs, through io/Command as estate's are (R6): a program that does not run or that overruns fails
/// the test naming the command, both streams are read as UTF-8, and dotnet's telemetry is off for every program started here.
/// </summary>
internal static class Programs
{
    /// <summary>How long a test's program may run: a publish, or a build of the golden project on a slow runner.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromMinutes(10);

    public static readonly IReadOnlyDictionary<string, string?> NoTelemetry = new Dictionary<string, string?>(StringComparer.Ordinal) { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1" };

    /// <summary>A program run from the repository's root, for at most <see cref="Default"/>.</summary>
    public static Command InRepository(string file, params string[] arguments) => new(file, arguments, Default) { Directory = Repository.Root, Environment = NoTelemetry };

    /// <summary>The command run with no token; a program not found or past its timeout fails the test, naming the command.</summary>
    public static Ran.Exited Finish(this Command command) => command.Run(CancellationToken.None) switch
    {
        Ran.Exited exited => exited,
        Ran.NotFound notFound => throw new XunitException(command + " did not run: " + notFound.Why),
        Ran.TimedOut timedOut => throw new XunitException(command + " ran past " + timedOut.Timeout + " and was stopped:\n" + timedOut.Output + timedOut.Errors),
        var other => throw new XunitException(command + ": " + other),
    };

    /// <summary>The exit code and both streams together, as a test reads a program's log.</summary>
    public static (int Exit, string Output) Joined(this Ran.Exited ran) => (ran.Code, ran.Output + ran.Errors);
}
