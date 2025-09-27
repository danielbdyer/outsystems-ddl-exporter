using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Tests.Support;

public static class FixtureFile
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string RepositoryRoot { get; } = LocateRepositoryRoot();

    public static string GetPath(string relativePath)
    {
        return Cache.GetOrAdd(relativePath, static key =>
        {
            var absolute = Path.GetFullPath(Path.Combine(RepositoryRoot, "tests", "Fixtures", key));
            if (!File.Exists(absolute))
            {
                throw new FileNotFoundException($"Fixture '{key}' not found.", absolute);
            }

            return absolute;
        });
    }

    public static Stream OpenRead(string relativePath)
    {
        var path = GetPath(relativePath);
        return File.OpenRead(path);
    }

    public static Stream OpenStream(string relativePath) => OpenRead(relativePath);

    public static JsonDocument OpenJson(string relativePath)
    {
        return JsonDocument.Parse(OpenRead(relativePath));
    }

    private static string LocateRepositoryRoot()
    {
        var probe = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(probe))
        {
            if (File.Exists(Path.Combine(probe, "OutSystemsModelToSql.sln")))
            {
                return probe;
            }

            var parent = Directory.GetParent(probe);
            probe = parent?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Unable to locate repository root from test context.");
    }
}
