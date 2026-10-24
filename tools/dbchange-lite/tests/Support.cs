using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DbChangeLite;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;

namespace DbChangeLite.Tests;

internal static class Repo
{
    public static string Lite { get; } = Locate();

    public static string Root { get; } = File.Exists(Path.Combine(Path.GetFullPath(Path.Combine(Lite, "..", "..")), "DbChange.sln")) ? Path.GetFullPath(Path.Combine(Lite, "..", "..")) : Lite;

    public static string Golden => Path.Combine(Root, "tests", "Golden");

    public static string Tool => Environment.GetEnvironmentVariable("DBCHANGE_LITE_TOOL") is { Length: > 0 } tool ? tool : Path.Combine(Lite, "dist", "dbchange-lite");

    public static string Src => Path.Combine(Lite, "src");

    public static string FixtureServer => LocalServer.ConnectionString();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DbChangeLite.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("DbChangeLite.sln was not found above " + AppContext.BaseDirectory);
    }
}

public sealed class Scratch : IDisposable
{
    public Scratch()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dbchange-lite-tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Under(string relative) => System.IO.Path.Combine(Path, relative);

    public string Write(string relative, string text)
    {
        var path = Under(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("DBCHANGE_LITE_KEEP") == "1")
        {
            return;
        }

        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class Dacpacs
{
    public static string Of(string path, params string[] scripts)
    {
        using var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        foreach (var statement in scripts.SelectMany(script => DeployScripts.Parse(script).Batches.SelectMany(b => b.Statements)))
        {
            model.AddObjects(DeployScripts.Render(statement) + ";");
        }

        DacPackageExtensions.BuildPackage(path, model, new PackageMetadata { Name = "Test", Version = "1.0" });
        return path;
    }
}

internal static class Scripts
{
    public static DeployScript Read(string script, string pre = "", string post = "") => DeployScripts.Read(script, pre, post, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

internal static class Runs
{
    public static Run At(string root, string verb) => new(root, verb, DateTimeOffset.UtcNow, CancellationToken.None);
}

internal static class Cli
{
    public static (int Exit, JsonElement Answer) Run(string root, params string[] args)
    {
        using var output = new StringWriter();
        var exit = Program.Run(args, root, output, DateTimeOffset.UtcNow, CancellationToken.None);
        var text = output.ToString();
        var summary = JsonDocument.Parse(text).RootElement;
        return summary.TryGetProperty("full", out var full) && full.ValueKind == JsonValueKind.String
            ? (exit, JsonDocument.Parse(File.ReadAllText(Path.Combine(root, full.GetString()!))).RootElement)
            : (exit, summary);
    }

    public static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
