using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Osm.Domain.Configuration;
using Osm.Domain.Abstractions;
using Osm.Dmm;
using Osm.Emission;
using Osm.Json;
using Osm.Json.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Profiling;
using Osm.Smo;
using Osm.Validation.Tightening;

return await DispatchAsync(args);

static async Task<int> DispatchAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "--help" or "-h")
    {
        PrintRootUsage();
        return 0;
    }

    var command = args[0];
    var remainder = args.Skip(1).ToArray();

    return command switch
    {
        "inspect" => await RunInspectAsync(remainder),
        "build-ssdt" => await RunBuildSsdtAsync(remainder),
        "dmm-compare" => await RunDmmCompareAsync(remainder),
        _ => UnknownCommand(command),
    };
}

static async Task<int> RunInspectAsync(string[] args)
{
    var options = ParseOptions(args);
    if (!options.TryGetValue("--in", out var input) && !options.TryGetValue("--model", out input))
    {
        Console.Error.WriteLine("Model path is required. Pass --model <path>.");
        return 1;
    }

    var ingestion = new ModelIngestionService(new ModelJsonDeserializer());
    var result = await ingestion.LoadFromFileAsync(input!);
    if (result.IsFailure)
    {
        WriteErrors(result.Errors);
        return 1;
    }

    var model = result.Value;
    var entityCount = model.Modules.Sum(m => m.Entities.Length);
    var attributeCount = model.Modules.Sum(m => m.Entities.Sum(e => e.Attributes.Length));

    Console.WriteLine($"Model exported at {model.ExportedAtUtc:O}");
    Console.WriteLine($"Modules: {model.Modules.Length}");
    Console.WriteLine($"Entities: {entityCount}");
    Console.WriteLine($"Attributes: {attributeCount}");
    return 0;
}

static async Task<int> RunBuildSsdtAsync(string[] args)
{
    var options = ParseOptions(args);
    if (!TryGetRequired(options, "--model", out var modelPath))
    {
        return 1;
    }

    if (!TryGetRequired(options, "--profile", out var profilePath))
    {
        return 1;
    }

    var outputPath = options.TryGetValue("--out", out var outValue) ? outValue ?? "out" : "out";
    var configPath = options.TryGetValue("--config", out var configValue) ? configValue : null;

    var tighteningOptionsResult = await LoadTighteningOptionsAsync(configPath);
    if (tighteningOptionsResult.IsFailure)
    {
        WriteErrors(tighteningOptionsResult.Errors);
        return 1;
    }

    var modelResult = await new ModelIngestionService(new ModelJsonDeserializer()).LoadFromFileAsync(modelPath!);
    if (modelResult.IsFailure)
    {
        WriteErrors(modelResult.Errors);
        return 1;
    }

    var profileResult = await new FixtureDataProfiler(profilePath!, new ProfileSnapshotDeserializer()).CaptureAsync();
    if (profileResult.IsFailure)
    {
        WriteErrors(profileResult.Errors);
        return 1;
    }

    var tighteningOptions = tighteningOptionsResult.Value;
    var policy = new TighteningPolicy();
    var decisions = policy.Decide(modelResult.Value, profileResult.Value, tighteningOptions);
    var decisionReport = PolicyDecisionReporter.Create(decisions);

    var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission);
    var smoModel = new SmoModelFactory().Create(modelResult.Value, decisions, smoOptions);

    var emitter = new SsdtEmitter();
    var manifest = await emitter.EmitAsync(smoModel, outputPath!, smoOptions, decisionReport);
    var decisionLogPath = await WriteDecisionLogAsync(outputPath!, decisionReport);

    Console.WriteLine($"Emitted {manifest.Tables.Count} tables to {outputPath}.");
    Console.WriteLine($"Manifest written to {Path.Combine(outputPath!, "manifest.json")}");
    Console.WriteLine($"Columns tightened: {decisionReport.TightenedColumnCount}/{decisionReport.ColumnCount}");
    Console.WriteLine($"Foreign keys created: {decisionReport.ForeignKeysCreatedCount}/{decisionReport.ForeignKeyCount}");
    Console.WriteLine($"Decision log written to {decisionLogPath}");
    return 0;
}

static async Task<int> RunDmmCompareAsync(string[] args)
{
    var options = ParseOptions(args);
    if (!TryGetRequired(options, "--model", out var modelPath))
    {
        return 1;
    }

    if (!TryGetRequired(options, "--profile", out var profilePath))
    {
        return 1;
    }

    if (!TryGetRequired(options, "--dmm", out var dmmPath))
    {
        return 1;
    }

    var configPath = options.TryGetValue("--config", out var configValue) ? configValue : null;

    var tighteningOptionsResult = await LoadTighteningOptionsAsync(configPath);
    if (tighteningOptionsResult.IsFailure)
    {
        WriteErrors(tighteningOptionsResult.Errors);
        return 1;
    }

    var modelResult = await new ModelIngestionService(new ModelJsonDeserializer()).LoadFromFileAsync(modelPath!);
    if (modelResult.IsFailure)
    {
        WriteErrors(modelResult.Errors);
        return 1;
    }

    var profileResult = await new FixtureDataProfiler(profilePath!, new ProfileSnapshotDeserializer()).CaptureAsync();
    if (profileResult.IsFailure)
    {
        WriteErrors(profileResult.Errors);
        return 1;
    }

    var tighteningOptions = tighteningOptionsResult.Value;
    var policy = new TighteningPolicy();
    var decisions = policy.Decide(modelResult.Value, profileResult.Value, tighteningOptions);
    var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission);
    var smoModel = new SmoModelFactory().Create(modelResult.Value, decisions, smoOptions);

    if (!File.Exists(dmmPath!))
    {
        Console.Error.WriteLine($"DMM script '{dmmPath}' not found.");
        return 1;
    }

    var parser = new DmmParser();
    using var reader = File.OpenText(dmmPath!);
    var parseResult = parser.Parse(reader);
    if (parseResult.IsFailure)
    {
        WriteErrors(parseResult.Errors);
        return 1;
    }

    var comparator = new DmmComparator();
    var comparison = comparator.Compare(smoModel, parseResult.Value);
    if (comparison.IsMatch)
    {
        Console.WriteLine("DMM parity confirmed.");
        return 0;
    }

    Console.Error.WriteLine("DMM parity differences detected:");
    foreach (var difference in comparison.Differences)
    {
        Console.Error.WriteLine($" - {difference}");
    }

    return 2;
}

static async Task<Result<TighteningOptions>> LoadTighteningOptionsAsync(string? configPath)
{
    if (string.IsNullOrWhiteSpace(configPath))
    {
        return Result<TighteningOptions>.Success(TighteningOptions.Default);
    }

    await using var stream = File.OpenRead(configPath);
    var deserializer = new TighteningOptionsDeserializer();
    return deserializer.Deserialize(stream);
}

static async Task<string> WriteDecisionLogAsync(string outputDirectory, PolicyDecisionReport report)
{
    var log = new PolicyDecisionLog(
        report.ColumnCount,
        report.TightenedColumnCount,
        report.RemediationColumnCount,
        report.ForeignKeyCount,
        report.ForeignKeysCreatedCount,
        report.ColumnRationaleCounts,
        report.ForeignKeyRationaleCounts,
        report.Columns.Select(static c => new PolicyDecisionLogColumn(
            c.Column.Schema.Value,
            c.Column.Table.Value,
            c.Column.Column.Value,
            c.MakeNotNull,
            c.RequiresRemediation,
            c.Rationales.ToArray())).ToArray(),
        report.ForeignKeys.Select(static f => new PolicyDecisionLogForeignKey(
            f.Column.Schema.Value,
            f.Column.Table.Value,
            f.Column.Column.Value,
            f.CreateConstraint,
            f.Rationales.ToArray())).ToArray());

    var path = Path.Combine(outputDirectory, "policy-decisions.json");
    var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(path, json);
    return path;
}

static Dictionary<string, string?> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var current = args[i];
        if (!current.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        string? value = null;
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++i];
        }

        options[current] = value;
    }

    return options;
}

static bool TryGetRequired(Dictionary<string, string?> options, string key, out string? value)
{
    if (!options.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
    {
        Console.Error.WriteLine($"Missing required option {key} <value>.");
        return false;
    }

    return true;
}

static void WriteErrors(IEnumerable<ValidationError> errors)
{
    foreach (var error in errors)
    {
        Console.Error.WriteLine($"{error.Code}: {error.Message}");
    }
}

static void PrintRootUsage()
{
    Console.WriteLine("Usage: dotnet run --project src/Osm.Cli -- <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  inspect --model <model.json>");
    Console.WriteLine("  build-ssdt --model <model.json> --profile <profile.json> [--out <dir>] [--config <path>]");
    Console.WriteLine("  dmm-compare --model <model.json> --profile <profile.json> --dmm <dmm.sql> [--config <path>]");
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintRootUsage();
    return 1;
}

file sealed record PolicyDecisionLog(
    int ColumnCount,
    int TightenedColumnCount,
    int RemediationColumnCount,
    int ForeignKeyCount,
    int ForeignKeysCreatedCount,
    IReadOnlyDictionary<string, int> ColumnRationales,
    IReadOnlyDictionary<string, int> ForeignKeyRationales,
    IReadOnlyList<PolicyDecisionLogColumn> Columns,
    IReadOnlyList<PolicyDecisionLogForeignKey> ForeignKeys);

file sealed record PolicyDecisionLogColumn(
    string Schema,
    string Table,
    string Column,
    bool MakeNotNull,
    bool RequiresRemediation,
    IReadOnlyList<string> Rationales);

file sealed record PolicyDecisionLogForeignKey(
    string Schema,
    string Table,
    string Column,
    bool CreateConstraint,
    IReadOnlyList<string> Rationales);
