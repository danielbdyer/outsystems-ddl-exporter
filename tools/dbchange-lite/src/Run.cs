using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace DbChangeLite;

public sealed class QueryLog(string path, string relative)
{
    public string Path { get; } = path;

    public string Relative { get; } = relative;

    public void Add(string server, string database, string statement, long elapsedMs, string result)
    {
        var line = string.Join('\t', DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture), server, database, result,
            elapsedMs.ToString(CultureInfo.InvariantCulture), statement.ReplaceLineEndings(" ")) + "\n";
        File.AppendAllText(Path, line);
    }
}

public sealed class Run
{
    private readonly Stopwatch clock = Stopwatch.StartNew();

    public Run(string workingDirectory, string verb, DateTimeOffset at, CancellationToken cancel)
    {
        Root = RootOf(workingDirectory);
        Verb = verb;
        At = at;
        Cancel = cancel;
        Folder = Path.Combine(Root, ".dbchange", "runs", at.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + verb);
        Directory.CreateDirectory(Folder);
        Log = new QueryLog(Path.Combine(Folder, "queries.log"), Relative(Path.Combine(Folder, "queries.log")));
        Tool = Environment.GetEnvironmentVariable("DBCHANGE_LITE_TOOL") is { Length: > 0 } tool ? Path.GetFullPath(tool) : AppContext.BaseDirectory;
        Pin = ToolchainLedger.Read(Root).Pin;
    }

    public string Root { get; }

    public string Verb { get; }

    public string Folder { get; }

    public string Tool { get; }

    public string Pin { get; }

    public DateTimeOffset At { get; }

    public CancellationToken Cancel { get; }

    public QueryLog Log { get; }

    public long ElapsedMs() => clock.ElapsedMilliseconds;

    public string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    public string Stamp => At.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string RootOf(string workingDirectory)
    {
        for (var directory = new DirectoryInfo(workingDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, EnvironmentsFile.Name)))
            {
                return directory.FullName;
            }
        }

        return Path.GetFullPath(workingDirectory);
    }
}
