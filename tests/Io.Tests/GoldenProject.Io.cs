using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Io;
using DbChange.Io.Tests;
using Microsoft.SqlServer.Dac;
using Xunit;

namespace DbChange.Tests;

/// <summary>
/// The golden project built against dist/dbchange/ once per test run for each head a test asks for: the base, the base again into a folder of
/// its own, and the base with sample changes applied; at most as many builds at once as the machine has processors, and each head's model
/// read once. The builds lie under .dbchange/golden/&lt;pid&gt;-&lt;random&gt;/, deleted when the process exits, or, after a run that was
/// killed, by the next run's first build.
/// </summary>
internal static partial class GoldenProject
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Packages = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Lazy<Task<Ssdt.ModelElements>>> Models = new(StringComparer.Ordinal);

    private static readonly SemaphoreSlim Builds = new(Environment.ProcessorCount);

    private static readonly Lazy<string> Run = new(Swept);

    private static int heads;

    /// <summary>The package of the golden project with the sample changes applied in turn, none for the base.</summary>
    public static Task<string> Built(params SampleChange[] changes) => Built(Head(changes), [.. changes.SelectMany(c => c.Edits)]);

    /// <summary>The base built a second time, into a folder of its own, so that two builds are compared and not one package with itself.</summary>
    public static Task<string> BuiltAgain() => Built("again", []);

    /// <summary>The model the head's package reads into.</summary>
    public static Task<Ssdt.ModelElements> Model(params SampleChange[] changes) => Read(Head(changes), () => Built(changes));

    /// <summary>The model the base built a second time reads into.</summary>
    public static Task<Ssdt.ModelElements> ModelAgain() => Read("again", BuiltAgain);

    /// <summary>The pipeline profile's deploy options, as DacFx reads the file, and nothing else from it.</summary>
    public static DacDeployOptions Pipeline() => DacProfile.Load(Profile).DeployOptions;

    /// <summary>
    /// A package published to a registered database as the fixture's administrator, loaded from a stream: a publish of a package loaded by
    /// path loads the assemblies beside it (the build's SampleCatalog.dll) into this process and holds them until it exits.
    /// </summary>
    public static void Publish(string dacpac, RegisteredDatabase database, DacDeployOptions options)
    {
        using var stream = File.OpenRead(dacpac);
        using var package = DacPackage.Load(stream);
        new DacServices(database.ConnectionString).Publish(package, database.Name, new PublishOptions { DeployOptions = options });
    }

    private static string Head(SampleChange[] changes) => string.Join('+', changes.Select(c => c.Name));

    private static Task<string> Built(string head, GoldenEdit[] edits) => Packages.GetOrAdd(head, _ => new(() => Task.Run(async () =>
    {
        await Builds.WaitAsync();
        try
        {
            var folder = Path.Combine(Run.Value, Interlocked.Increment(ref heads).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Expect.Value(Ssdt.Build(CopyTo(folder, edits), new PublishedTool().Folder, Path.Combine(folder, "build"))).Path;
        }
        finally
        {
            Builds.Release();
        }
    }))).Value;

    private static Task<Ssdt.ModelElements> Read(string head, Func<Task<string>> built) => Models.GetOrAdd(head, _ => new(async () =>
    {
        using var package = Expect.Value(Ssdt.Open(await built()));
        return Expect.Value(package.Elements);
    })).Value;

    /// <summary>This run's folder, made once the folders of runs whose process no longer runs are deleted, and deleted when this process exits.</summary>
    private static string Swept()
    {
        var parent = Path.Combine(Repository.Root, ".dbchange", "golden");
        foreach (var stale in (Directory.Exists(parent) ? Directory.GetDirectories(parent) : []).Where(f => int.TryParse(Path.GetFileName(f).Split('-')[0], out var pid) && !Running(pid)))
        {
            Directory.Delete(stale, recursive: true);
        }

        var run = ScratchFolder.UnderRepository("golden");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => run.Dispose();
        return run.Path;
    }

    private static bool Running(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
