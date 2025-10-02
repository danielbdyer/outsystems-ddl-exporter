using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Osm.Cli.Configuration;
using Osm.Domain.Configuration;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Dmm;
using Osm.Emission;
using Osm.Json;
using Osm.Json.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Osm.Validation.Tightening;
using Microsoft.Data.SqlClient;

const string EnvConfigPath = "OSM_CLI_CONFIG_PATH";
const string EnvModelPath = "OSM_CLI_MODEL_PATH";
const string EnvProfilePath = "OSM_CLI_PROFILE_PATH";
const string EnvDmmPath = "OSM_CLI_DMM_PATH";
const string EnvCacheRoot = "OSM_CLI_CACHE_ROOT";
const string EnvRefreshCache = "OSM_CLI_REFRESH_CACHE";
const string EnvProfilerProvider = "OSM_CLI_PROFILER_PROVIDER";
const string EnvProfilerMockFolder = "OSM_CLI_PROFILER_MOCK_FOLDER";
const string EnvSqlConnectionString = "OSM_CLI_CONNECTION_STRING";
const string EnvSqlCommandTimeout = "OSM_CLI_SQL_COMMAND_TIMEOUT";
const string EnvSqlAuthentication = "OSM_CLI_SQL_AUTHENTICATION";
const string EnvSqlAccessToken = "OSM_CLI_SQL_ACCESS_TOKEN";
const string EnvSqlTrustServerCertificate = "OSM_CLI_SQL_TRUST_SERVER_CERTIFICATE";
const string EnvSqlApplicationName = "OSM_CLI_SQL_APPLICATION_NAME";

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
        "extract-model" => await RunExtractModelAsync(remainder),
        "build-ssdt" => await RunBuildSsdtAsync(remainder),
        "dmm-compare" => await RunDmmCompareAsync(remainder),
        _ => UnknownCommand(command),
    };
}

static async Task<int> RunExtractModelAsync(string[] args)
{
    var options = ParseOptions(args);
    var configurationResult = await LoadCliConfigurationAsync(options);
    if (configurationResult.IsFailure)
    {
        WriteErrors(configurationResult.Errors);
        return 1;
    }

    var configuration = configurationResult.Value.Configuration;
    var modules = ResolveModuleArguments(options);
    var includeSystem = options.ContainsKey("--include-system-modules");
    var onlyActive = options.ContainsKey("--only-active-attributes");

    var commandResult = ModelExtractionCommand.Create(modules, includeSystem, onlyActive);
    if (commandResult.IsFailure)
    {
        WriteErrors(commandResult.Errors);
        return 1;
    }

    var outputPath = options.TryGetValue("--out", out var outValue) && !string.IsNullOrWhiteSpace(outValue)
        ? outValue!
        : "model.extracted.json";

    var sqlOptionsResult = ResolveSqlOptions(options, configuration.Sql);
    if (sqlOptionsResult.IsFailure)
    {
        WriteErrors(sqlOptionsResult.Errors);
        return 1;
    }

    var sqlOptions = sqlOptionsResult.Value;
    IAdvancedSqlExecutor executor;
    if (options.TryGetValue("--mock-advanced-sql", out var manifestPath) && !string.IsNullOrWhiteSpace(manifestPath))
    {
        executor = new FixtureAdvancedSqlExecutor(manifestPath!);
    }
    else
    {
        if (string.IsNullOrWhiteSpace(sqlOptions.ConnectionString))
        {
            Console.Error.WriteLine("Connection string is required for live extraction. Provide --connection-string or configure sql.connectionString.");
            return 1;
        }

        var samplingOptions = CreateSamplingOptions(sqlOptions.Sampling);
        var connectionOptions = CreateConnectionOptions(sqlOptions.Authentication);
        var executionOptions = new SqlExecutionOptions(sqlOptions.CommandTimeoutSeconds, samplingOptions);
        executor = new SqlClientAdvancedSqlExecutor(
            new SqlConnectionFactory(sqlOptions.ConnectionString!, connectionOptions),
            new EmbeddedAdvancedSqlScriptProvider(),
            executionOptions);
    }

    var extractionService = new SqlModelExtractionService(executor, new ModelJsonDeserializer());

    var extractionResult = await extractionService.ExtractAsync(commandResult.Value);
    if (extractionResult.IsFailure)
    {
        WriteErrors(extractionResult.Errors);
        return 1;
    }

    var result = extractionResult.Value;
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Directory.GetCurrentDirectory());
    await File.WriteAllTextAsync(outputPath, result.Json);

    var moduleCount = result.Model.Modules.Length;
    var entityCount = result.Model.Modules.Sum(static m => m.Entities.Length);
    Console.WriteLine($"Extracted {moduleCount} modules spanning {entityCount} entities.");
    Console.WriteLine($"Model written to {outputPath}.");
    Console.WriteLine($"Extraction timestamp (UTC): {result.ExtractedAtUtc:O}");
    return 0;
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
    var configurationResult = await LoadCliConfigurationAsync(options);
    if (configurationResult.IsFailure)
    {
        WriteErrors(configurationResult.Errors);
        return 1;
    }

    var configurationContext = configurationResult.Value;
    var configuration = configurationContext.Configuration;
    var configPath = configurationContext.ConfigPath;

    var moduleFilterResult = ResolveModuleFilterOptions(options, configuration.ModuleFilter);
    if (moduleFilterResult.IsFailure)
    {
        WriteErrors(moduleFilterResult.Errors);
        return 1;
    }

    var moduleFilter = moduleFilterResult.Value;

    var sqlOptionsResult = ResolveSqlOptions(options, configuration.Sql);
    if (sqlOptionsResult.IsFailure)
    {
        WriteErrors(sqlOptionsResult.Errors);
        return 1;
    }

    var sqlOptions = sqlOptionsResult.Value;

    if (!TryResolveRequiredPath(options, "--model", EnvModelPath, configuration.ModelPath, "model path", out var modelPath))
    {
        return 1;
    }

    var profilerProvider = ResolveProfilerProvider(options, configuration.Profiler);
    string? profilePath = null;
    if (string.Equals(profilerProvider, "fixture", StringComparison.OrdinalIgnoreCase))
    {
        var defaultProfilePath = configuration.ProfilePath ?? configuration.Profiler.ProfilePath;
        if (!TryResolveRequiredPath(options, "--profile", EnvProfilePath, defaultProfilePath, "profile path", out profilePath))
        {
            return 1;
        }
    }
    else
    {
        if (options.TryGetValue("--profile", out var optionalProfile) && !string.IsNullOrWhiteSpace(optionalProfile))
        {
            profilePath = optionalProfile;
        }
        else if (!string.IsNullOrWhiteSpace(configuration.ProfilePath))
        {
            profilePath = configuration.ProfilePath;
        }
        else if (!string.IsNullOrWhiteSpace(configuration.Profiler.ProfilePath))
        {
            profilePath = configuration.Profiler.ProfilePath;
        }
    }

    var outputPath = options.TryGetValue("--out", out var outValue) && !string.IsNullOrWhiteSpace(outValue)
        ? outValue!
        : "out";

    var cacheRoot = ResolveCacheRoot(options, configuration);
    var refreshCache = ResolveRefreshCache(options, configuration);
    var tighteningOptions = configuration.Tightening;

    var modelResult = await new ModelIngestionService(new ModelJsonDeserializer()).LoadFromFileAsync(modelPath);
    if (modelResult.IsFailure)
    {
        WriteErrors(modelResult.Errors);
        return 1;
    }

    var filteredModelResult = new ModuleFilter().Apply(modelResult.Value, moduleFilter);
    if (filteredModelResult.IsFailure)
    {
        WriteErrors(filteredModelResult.Errors);
        return 1;
    }

    var model = filteredModelResult.Value;

    var profileResult = await CaptureProfileAsync(profilerProvider, profilePath, sqlOptions, model);
    if (profileResult.IsFailure)
    {
        WriteErrors(profileResult.Errors);
        return 1;
    }

    if (!await TryCacheEvidenceAsync(
            cacheRoot,
            refreshCache,
            command: "build-ssdt",
            modelPath,
            profilePath,
            dmmPath: null,
            configPath,
            tighteningOptions,
            moduleFilter,
            configuration))
    {
        return 1;
    }

    var policy = new TighteningPolicy();
    var decisions = policy.Decide(model, profileResult.Value, tighteningOptions);
    var decisionReport = PolicyDecisionReporter.Create(decisions);

    var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission);
    var namingOverrideResult = ResolveNamingOverrides(options, smoOptions.NamingOverrides);
    if (namingOverrideResult.IsFailure)
    {
        WriteErrors(namingOverrideResult.Errors);
        return 1;
    }

    smoOptions = smoOptions.WithNamingOverrides(namingOverrideResult.Value);
    var smoModel = new SmoModelFactory().Create(model, decisions, smoOptions);

    var emitter = new SsdtEmitter();
    var manifest = await emitter.EmitAsync(smoModel, outputPath!, smoOptions, decisionReport);
    var decisionLogPath = await WriteDecisionLogAsync(outputPath!, decisionReport);

    Console.WriteLine($"Emitted {manifest.Tables.Count} tables to {outputPath}.");
    Console.WriteLine($"Manifest written to {Path.Combine(outputPath!, "manifest.json")}");
    Console.WriteLine($"Columns tightened: {decisionReport.TightenedColumnCount}/{decisionReport.ColumnCount}");
    Console.WriteLine($"Unique indexes enforced: {decisionReport.UniqueIndexesEnforcedCount}/{decisionReport.UniqueIndexCount}");
    Console.WriteLine($"Foreign keys created: {decisionReport.ForeignKeysCreatedCount}/{decisionReport.ForeignKeyCount}");
    Console.WriteLine($"Decision log written to {decisionLogPath}");
    return 0;
}

static async Task<int> RunDmmCompareAsync(string[] args)
{
    var options = ParseOptions(args);
    var configurationResult = await LoadCliConfigurationAsync(options);
    if (configurationResult.IsFailure)
    {
        WriteErrors(configurationResult.Errors);
        return 1;
    }

    var configurationContext = configurationResult.Value;
    var configuration = configurationContext.Configuration;
    var configPath = configurationContext.ConfigPath;

    var moduleFilterResult = ResolveModuleFilterOptions(options, configuration.ModuleFilter);
    if (moduleFilterResult.IsFailure)
    {
        WriteErrors(moduleFilterResult.Errors);
        return 1;
    }

    var moduleFilter = moduleFilterResult.Value;

    if (!TryResolveRequiredPath(options, "--model", EnvModelPath, configuration.ModelPath, "model path", out var modelPath))
    {
        return 1;
    }

    if (!TryResolveRequiredPath(options, "--profile", EnvProfilePath, configuration.ProfilePath, "profile path", out var profilePath))
    {
        return 1;
    }

    if (!TryResolveRequiredPath(options, "--dmm", EnvDmmPath, configuration.DmmPath, "DMM path", out var dmmPath))
    {
        return 1;
    }

    var diffPath = options.TryGetValue("--out", out var diffValue) && !string.IsNullOrWhiteSpace(diffValue)
        ? diffValue!
        : "dmm-diff.json";

    var cacheRoot = ResolveCacheRoot(options, configuration);
    var refreshCache = ResolveRefreshCache(options, configuration);
    var tighteningOptions = configuration.Tightening;

    var modelResult = await new ModelIngestionService(new ModelJsonDeserializer()).LoadFromFileAsync(modelPath);
    if (modelResult.IsFailure)
    {
        WriteErrors(modelResult.Errors);
        return 1;
    }

    var filteredModelResult = new ModuleFilter().Apply(modelResult.Value, moduleFilter);
    if (filteredModelResult.IsFailure)
    {
        WriteErrors(filteredModelResult.Errors);
        return 1;
    }

    var model = filteredModelResult.Value;

    var profileResult = await new FixtureDataProfiler(profilePath, new ProfileSnapshotDeserializer()).CaptureAsync();
    if (profileResult.IsFailure)
    {
        WriteErrors(profileResult.Errors);
        return 1;
    }

    if (!File.Exists(dmmPath))
    {
        Console.Error.WriteLine($"DMM script '{dmmPath}' not found.");
        return 1;
    }

    if (!await TryCacheEvidenceAsync(
            cacheRoot,
            refreshCache,
            command: "dmm-compare",
            modelPath,
            profilePath,
            dmmPath,
            configPath,
            tighteningOptions,
            moduleFilter,
            configuration))
    {
        return 1;
    }

    var policy = new TighteningPolicy();
    var decisions = policy.Decide(model, profileResult.Value, tighteningOptions);
    var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission, applyNamingOverrides: false);
    var smoModel = new SmoModelFactory().Create(model, decisions, smoOptions);

    var parser = new DmmParser();
    using var reader = File.OpenText(dmmPath);
    var parseResult = parser.Parse(reader);
    if (parseResult.IsFailure)
    {
        WriteErrors(parseResult.Errors);
        return 1;
    }

    var comparator = new DmmComparator();
    var comparison = comparator.Compare(smoModel, parseResult.Value, tighteningOptions.Emission.NamingOverrides);

    string diffArtifactPath;
    try
    {
        diffArtifactPath = await WriteDmmDiffAsync(
            diffPath,
            modelPath,
            profilePath,
            dmmPath,
            comparison);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to write diff artifact to '{diffPath}': {ex.Message}");
        return 1;
    }

    if (comparison.IsMatch)
    {
        Console.WriteLine($"DMM parity confirmed. Diff artifact written to {diffArtifactPath}.");
        return 0;
    }

    if (comparison.ModelDifferences.Count > 0)
    {
        Console.Error.WriteLine("Model requires additional SSDT coverage:");
        foreach (var difference in comparison.ModelDifferences)
        {
            Console.Error.WriteLine($" - {difference}");
        }
    }

    if (comparison.SsdtDifferences.Count > 0)
    {
        Console.Error.WriteLine("SSDT scripts drift from OutSystems model:");
        foreach (var difference in comparison.SsdtDifferences)
        {
            Console.Error.WriteLine($" - {difference}");
        }
    }

    Console.Error.WriteLine($"Diff artifact written to {diffArtifactPath}.");
    return 2;
}

static async Task<Result<CliConfigurationContext>> LoadCliConfigurationAsync(Dictionary<string, string?> options)
{
    var configPath = ResolveConfigPath(options);
    var loader = new CliConfigurationLoader();
    var loadResult = await loader.LoadAsync(configPath);
    if (loadResult.IsFailure)
    {
        return Result<CliConfigurationContext>.Failure(loadResult.Errors);
    }

    var configuration = ApplyEnvironmentOverrides(loadResult.Value);
    return new CliConfigurationContext(configuration, configPath);
}

static string? ResolveConfigPath(Dictionary<string, string?> options)
{
    if (options.TryGetValue("--config", out var configValue) && !string.IsNullOrWhiteSpace(configValue))
    {
        return configValue;
    }

    var envPath = Environment.GetEnvironmentVariable(EnvConfigPath);
    return string.IsNullOrWhiteSpace(envPath) ? null : envPath;
}

static CliConfiguration ApplyEnvironmentOverrides(CliConfiguration configuration)
{
    var result = configuration;

    if (TryGetEnvironmentPath(EnvModelPath, out var modelPath))
    {
        result = result with { ModelPath = modelPath };
    }

    if (TryGetEnvironmentPath(EnvProfilePath, out var profilePath))
    {
        result = result with { ProfilePath = profilePath };
        result = result with { Profiler = result.Profiler with { ProfilePath = profilePath } };
    }
    else if (string.IsNullOrWhiteSpace(result.ProfilePath) && !string.IsNullOrWhiteSpace(result.Profiler.ProfilePath))
    {
        result = result with { ProfilePath = result.Profiler.ProfilePath };
    }

    if (TryGetEnvironmentPath(EnvDmmPath, out var dmmPath))
    {
        result = result with { DmmPath = dmmPath };
    }

    var cache = result.Cache;
    if (TryGetEnvironmentPath(EnvCacheRoot, out var cacheRoot))
    {
        cache = cache with { Root = cacheRoot };
    }

    if (TryParseBoolean(Environment.GetEnvironmentVariable(EnvRefreshCache), out var refreshCache))
    {
        cache = cache with { Refresh = refreshCache };
    }

    result = result with { Cache = cache };

    var profiler = result.Profiler;
    var provider = Environment.GetEnvironmentVariable(EnvProfilerProvider);
    if (!string.IsNullOrWhiteSpace(provider))
    {
        profiler = profiler with { Provider = provider };
    }

    if (TryGetEnvironmentPath(EnvProfilerMockFolder, out var mockFolder))
    {
        profiler = profiler with { MockFolder = mockFolder };
    }

    result = result with { Profiler = profiler };

    var sql = result.Sql;
    var connectionString = Environment.GetEnvironmentVariable(EnvSqlConnectionString);
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        sql = sql with { ConnectionString = connectionString.Trim() };
    }

    if (TryParseInt(Environment.GetEnvironmentVariable(EnvSqlCommandTimeout), out var timeout))
    {
        sql = sql with { CommandTimeoutSeconds = timeout };
    }

    var authentication = sql.Authentication;
    var authenticationRaw = Environment.GetEnvironmentVariable(EnvSqlAuthentication);
    if (!string.IsNullOrWhiteSpace(authenticationRaw) && Enum.TryParse(authenticationRaw, true, out SqlAuthenticationMethod method))
    {
        authentication = authentication with { Method = method };
    }

    if (TryParseBoolean(Environment.GetEnvironmentVariable(EnvSqlTrustServerCertificate), out var trustServerCertificate))
    {
        authentication = authentication with { TrustServerCertificate = trustServerCertificate };
    }

    var applicationName = Environment.GetEnvironmentVariable(EnvSqlApplicationName);
    if (!string.IsNullOrWhiteSpace(applicationName))
    {
        authentication = authentication with { ApplicationName = applicationName!.Trim() };
    }

    var accessToken = Environment.GetEnvironmentVariable(EnvSqlAccessToken);
    if (!string.IsNullOrWhiteSpace(accessToken))
    {
        authentication = authentication with { AccessToken = accessToken };
    }

    sql = sql with { Authentication = authentication };

    result = result with { Sql = sql };

    return result;
}

static bool TryGetEnvironmentPath(string variable, out string path)
{
    var raw = Environment.GetEnvironmentVariable(variable);
    if (string.IsNullOrWhiteSpace(raw))
    {
        path = string.Empty;
        return false;
    }

    var trimmed = raw.Trim();
    try
    {
        path = Path.GetFullPath(trimmed);
    }
    catch
    {
        path = trimmed;
    }

    return true;
}

static bool TryParseBoolean(string? value, out bool result)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        result = default;
        return false;
    }

    return bool.TryParse(value, out result);
}

static bool TryParseInt(string? value, out int result)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        result = default;
        return false;
    }

    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}

static bool TryResolveRequiredPath(
    Dictionary<string, string?> options,
    string optionKey,
    string envVariable,
    string? configurationValue,
    string friendlyName,
    out string value)
{
    if (options.TryGetValue(optionKey, out var cliValue) && !string.IsNullOrWhiteSpace(cliValue))
    {
        value = cliValue!;
        return true;
    }

    if (!string.IsNullOrWhiteSpace(configurationValue))
    {
        value = configurationValue!;
        return true;
    }

    Console.Error.WriteLine($"Missing required {friendlyName}. Provide {optionKey} <value>, set {envVariable}, or define it in the CLI configuration file.");
    value = string.Empty;
    return false;
}

static string? ResolveCacheRoot(Dictionary<string, string?> options, CliConfiguration configuration)
{
    if (options.TryGetValue("--cache-root", out var cliValue) && !string.IsNullOrWhiteSpace(cliValue))
    {
        return cliValue;
    }

    return configuration.Cache.Root;
}

static bool ResolveRefreshCache(Dictionary<string, string?> options, CliConfiguration configuration)
{
    if (options.ContainsKey("--refresh-cache"))
    {
        return true;
    }

    return configuration.Cache.Refresh ?? false;
}

static string ComputeSha256(string value)
{
    using var sha = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(value);
    return Convert.ToHexString(sha.ComputeHash(bytes));
}

static async Task<string> WriteDecisionLogAsync(string outputDirectory, PolicyDecisionReport report)
{
    var log = new PolicyDecisionLog(
        report.ColumnCount,
        report.TightenedColumnCount,
        report.RemediationColumnCount,
        report.UniqueIndexCount,
        report.UniqueIndexesEnforcedCount,
        report.UniqueIndexesRequireRemediationCount,
        report.ForeignKeyCount,
        report.ForeignKeysCreatedCount,
        report.ColumnRationaleCounts,
        report.UniqueIndexRationaleCounts,
        report.ForeignKeyRationaleCounts,
        report.Columns.Select(static c => new PolicyDecisionLogColumn(
            c.Column.Schema.Value,
            c.Column.Table.Value,
            c.Column.Column.Value,
            c.MakeNotNull,
            c.RequiresRemediation,
            c.Rationales.ToArray())).ToArray(),
        report.UniqueIndexes.Select(static u => new PolicyDecisionLogUniqueIndex(
            u.Index.Schema.Value,
            u.Index.Table.Value,
            u.Index.Index.Value,
            u.EnforceUnique,
            u.RequiresRemediation,
            u.Rationales.ToArray())).ToArray(),
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

static async Task<string> WriteDmmDiffAsync(
    string outputPath,
    string modelPath,
    string profilePath,
    string dmmPath,
    DmmComparisonResult comparison)
{
    var fullPath = Path.GetFullPath(outputPath);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var log = new DmmDiffLog(
        comparison.IsMatch,
        modelPath,
        profilePath,
        dmmPath,
        DateTimeOffset.UtcNow,
        comparison.ModelDifferences.ToArray(),
        comparison.SsdtDifferences.ToArray());

    var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(fullPath, json);
    return fullPath;
}

static async Task<bool> TryCacheEvidenceAsync(
    string? cacheRoot,
    bool refreshCache,
    string command,
    string modelPath,
    string? profilePath,
    string? dmmPath,
    string? configPath,
    TighteningOptions tighteningOptions,
    ModuleFilterOptions moduleFilter,
    CliConfiguration configuration)
{
    if (string.IsNullOrWhiteSpace(cacheRoot))
    {
        return true;
    }

    var root = cacheRoot.Trim();

    var metadata = BuildCacheMetadata(tighteningOptions, moduleFilter, configuration);
    var request = new EvidenceCacheRequest(
        root,
        command,
        modelPath,
        profilePath,
        dmmPath,
        configPath,
        metadata,
        refreshCache);

    var cacheResult = await new EvidenceCacheService().CacheAsync(request).ConfigureAwait(false);
    if (cacheResult.IsFailure)
    {
        WriteErrors(cacheResult.Errors);
        return false;
    }

    var cache = cacheResult.Value;
    Console.WriteLine($"Cached inputs to {cache.CacheDirectory} (key {cache.Manifest.Key}).");
    return true;
}

static IReadOnlyDictionary<string, string?> BuildCacheMetadata(
    TighteningOptions options,
    ModuleFilterOptions moduleFilter,
    CliConfiguration configuration)
{
    var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["policy.mode"] = options.Policy.Mode.ToString(),
        ["policy.nullBudget"] = options.Policy.NullBudget.ToString(CultureInfo.InvariantCulture),
        ["foreignKeys.enableCreation"] = options.ForeignKeys.EnableCreation.ToString(),
        ["foreignKeys.allowCrossSchema"] = options.ForeignKeys.AllowCrossSchema.ToString(),
        ["foreignKeys.allowCrossCatalog"] = options.ForeignKeys.AllowCrossCatalog.ToString(),
        ["uniqueness.singleColumn"] = options.Uniqueness.EnforceSingleColumnUnique.ToString(),
        ["uniqueness.multiColumn"] = options.Uniqueness.EnforceMultiColumnUnique.ToString(),
        ["remediation.generatePreScripts"] = options.Remediation.GeneratePreScripts.ToString(),
        ["remediation.maxRowsDefaultBackfill"] = options.Remediation.MaxRowsDefaultBackfill.ToString(CultureInfo.InvariantCulture),
        ["emission.perTableFiles"] = options.Emission.PerTableFiles.ToString(),
        ["emission.includePlatformAutoIndexes"] = options.Emission.IncludePlatformAutoIndexes.ToString(),
        ["emission.sanitizeModuleNames"] = options.Emission.SanitizeModuleNames.ToString(),
        ["emission.concatenatedConstraints"] = options.Emission.EmitConcatenatedConstraints.ToString(),
        ["mocking.useProfileMockFolder"] = options.Mocking.UseProfileMockFolder.ToString(),
    };

    metadata["moduleFilter.includeSystemModules"] = moduleFilter.IncludeSystemModules.ToString();
    metadata["moduleFilter.includeInactiveModules"] = moduleFilter.IncludeInactiveModules.ToString();

    if (!moduleFilter.Modules.IsDefaultOrEmpty)
    {
        metadata["moduleFilter.modules"] = string.Join(",", moduleFilter.Modules);
    }

    if (!string.IsNullOrWhiteSpace(options.Mocking.ProfileMockFolder))
    {
        metadata["mocking.profileMockFolder"] = options.Mocking.ProfileMockFolder;
    }

    if (!string.IsNullOrWhiteSpace(configuration.ModelPath))
    {
        metadata["inputs.model"] = Path.GetFullPath(configuration.ModelPath);
    }

    if (!string.IsNullOrWhiteSpace(configuration.ProfilePath))
    {
        metadata["inputs.profile"] = Path.GetFullPath(configuration.ProfilePath);
    }

    if (!string.IsNullOrWhiteSpace(configuration.DmmPath))
    {
        metadata["inputs.dmm"] = Path.GetFullPath(configuration.DmmPath);
    }

    if (!string.IsNullOrWhiteSpace(configuration.Cache.Root))
    {
        metadata["cache.root"] = Path.GetFullPath(configuration.Cache.Root);
    }

    if (configuration.Cache.Refresh.HasValue)
    {
        metadata["cache.refreshRequested"] = configuration.Cache.Refresh.Value.ToString();
    }

    if (!string.IsNullOrWhiteSpace(configuration.Profiler.Provider))
    {
        metadata["profiler.provider"] = configuration.Profiler.Provider;
    }

    if (!string.IsNullOrWhiteSpace(configuration.Profiler.MockFolder))
    {
        metadata["profiler.mockFolder"] = Path.GetFullPath(configuration.Profiler.MockFolder);
    }

    if (!string.IsNullOrWhiteSpace(configuration.Sql.ConnectionString))
    {
        metadata["sql.connectionHash"] = ComputeSha256(configuration.Sql.ConnectionString);
    }

    if (configuration.Sql.CommandTimeoutSeconds.HasValue)
    {
        metadata["sql.commandTimeoutSeconds"] = configuration.Sql.CommandTimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture);
    }

    if (configuration.Sql.Sampling.RowSamplingThreshold.HasValue)
    {
        metadata["sql.sampling.rowThreshold"] = configuration.Sql.Sampling.RowSamplingThreshold.Value.ToString(CultureInfo.InvariantCulture);
    }

    if (configuration.Sql.Sampling.SampleSize.HasValue)
    {
        metadata["sql.sampling.sampleSize"] = configuration.Sql.Sampling.SampleSize.Value.ToString(CultureInfo.InvariantCulture);
    }

    if (configuration.Sql.Authentication.Method.HasValue)
    {
        metadata["sql.authentication.method"] = configuration.Sql.Authentication.Method.Value.ToString();
    }

    if (configuration.Sql.Authentication.TrustServerCertificate.HasValue)
    {
        metadata["sql.authentication.trustServerCertificate"] = configuration.Sql.Authentication.TrustServerCertificate.Value.ToString();
    }

    if (!string.IsNullOrWhiteSpace(configuration.Sql.Authentication.ApplicationName))
    {
        metadata["sql.authentication.applicationName"] = configuration.Sql.Authentication.ApplicationName;
    }

    if (!string.IsNullOrWhiteSpace(configuration.Sql.Authentication.AccessToken))
    {
        metadata["sql.authentication.accessTokenHash"] = ComputeSha256(configuration.Sql.Authentication.AccessToken);
    }

    return metadata;
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

static Result<ResolvedSqlOptions> ResolveSqlOptions(Dictionary<string, string?> options, SqlConfiguration configuration)
{
    var connection = configuration.ConnectionString;
    var timeout = configuration.CommandTimeoutSeconds;
    var sampling = configuration.Sampling;
    var authentication = configuration.Authentication;

    if (options.TryGetValue("--connection-string", out var connectionValue) && !string.IsNullOrWhiteSpace(connectionValue))
    {
        connection = connectionValue;
    }

    if (options.TryGetValue("--command-timeout", out var timeoutValue) && !string.IsNullOrWhiteSpace(timeoutValue))
    {
        if (!int.TryParse(timeoutValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimeout) || parsedTimeout < 0)
        {
            return ValidationError.Create("cli.sql.commandTimeout.invalid", "Invalid --command-timeout value. Expected non-negative integer seconds.");
        }

        timeout = parsedTimeout;
    }

    if (options.TryGetValue("--sampling-threshold", out var thresholdValue) && !string.IsNullOrWhiteSpace(thresholdValue))
    {
        if (!long.TryParse(thresholdValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedThreshold) || parsedThreshold <= 0)
        {
            return ValidationError.Create("cli.sql.samplingThreshold.invalid", "Invalid --sampling-threshold value. Expected positive integer rows.");
        }

        sampling = sampling with { RowSamplingThreshold = parsedThreshold };
    }

    if (options.TryGetValue("--sampling-size", out var sampleValue) && !string.IsNullOrWhiteSpace(sampleValue))
    {
        if (!int.TryParse(sampleValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSample) || parsedSample <= 0)
        {
            return ValidationError.Create("cli.sql.samplingSize.invalid", "Invalid --sampling-size value. Expected positive integer rows.");
        }

        sampling = sampling with { SampleSize = parsedSample };
    }

    if (options.TryGetValue("--sql-authentication", out var authValue) && !string.IsNullOrWhiteSpace(authValue))
    {
        if (!Enum.TryParse(authValue, true, out SqlAuthenticationMethod method))
        {
            return ValidationError.Create("cli.sql.authentication.invalid", "Invalid --sql-authentication value. Expected a valid SqlAuthenticationMethod.");
        }

        authentication = authentication with { Method = method };
    }

    if (options.TryGetValue("--sql-trust-server-certificate", out var trustValue))
    {
        if (string.IsNullOrWhiteSpace(trustValue))
        {
            authentication = authentication with { TrustServerCertificate = true };
        }
        else if (bool.TryParse(trustValue, out var parsedTrust))
        {
            authentication = authentication with { TrustServerCertificate = parsedTrust };
        }
        else
        {
            return ValidationError.Create("cli.sql.trustServerCertificate.invalid", "Invalid --sql-trust-server-certificate value. Expected 'true' or 'false'.");
        }
    }

    if (options.TryGetValue("--sql-application-name", out var appValue) && !string.IsNullOrWhiteSpace(appValue))
    {
        authentication = authentication with { ApplicationName = appValue!.Trim() };
    }

    if (options.TryGetValue("--sql-access-token", out var tokenValue) && !string.IsNullOrWhiteSpace(tokenValue))
    {
        authentication = authentication with { AccessToken = tokenValue };
    }

    return new ResolvedSqlOptions(connection?.Trim(), timeout, sampling, authentication);
}

static SqlSamplingOptions CreateSamplingOptions(SqlSamplingConfiguration configuration)
{
    var threshold = configuration.RowSamplingThreshold ?? SqlSamplingOptions.Default.RowCountSamplingThreshold;
    var sampleSize = configuration.SampleSize ?? SqlSamplingOptions.Default.SampleSize;
    return new SqlSamplingOptions(threshold, sampleSize);
}

static SqlConnectionOptions CreateConnectionOptions(SqlAuthenticationConfiguration configuration)
{
    return new SqlConnectionOptions(
        configuration.Method,
        configuration.TrustServerCertificate,
        configuration.ApplicationName,
        configuration.AccessToken);
}

static string ResolveProfilerProvider(Dictionary<string, string?> options, ProfilerConfiguration configuration)
{
    if (options.TryGetValue("--profiler-provider", out var providerValue) && !string.IsNullOrWhiteSpace(providerValue))
    {
        return providerValue!;
    }

    if (!string.IsNullOrWhiteSpace(configuration.Provider))
    {
        return configuration.Provider!;
    }

    return "fixture";
}

static async Task<Result<ProfileSnapshot>> CaptureProfileAsync(
    string provider,
    string? profilePath,
    ResolvedSqlOptions sqlOptions,
    OsmModel model)
{
    if (string.Equals(provider, "sql", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(sqlOptions.ConnectionString))
        {
            return Result<ProfileSnapshot>.Failure(ValidationError.Create(
                "cli.sql.connectionString.missing",
                "Connection string is required when using the sql profiler."));
        }

        var samplingOptions = CreateSamplingOptions(sqlOptions.Sampling);
        var connectionOptions = CreateConnectionOptions(sqlOptions.Authentication);
        var profilerOptions = new SqlProfilerOptions(sqlOptions.CommandTimeoutSeconds, samplingOptions);
        var profiler = new SqlDataProfiler(new SqlConnectionFactory(sqlOptions.ConnectionString!, connectionOptions), model, profilerOptions);
        return await profiler.CaptureAsync().ConfigureAwait(false);
    }

    if (string.IsNullOrWhiteSpace(profilePath))
    {
        return Result<ProfileSnapshot>.Failure(ValidationError.Create(
            "cli.profile.path.missing",
            "Profile path is required when using the fixture profiler."));
    }

    var fixtureProfiler = new FixtureDataProfiler(profilePath!, new ProfileSnapshotDeserializer());
    return await fixtureProfiler.CaptureAsync().ConfigureAwait(false);
}

static Result<NamingOverrideOptions> ResolveNamingOverrides(
    Dictionary<string, string?> options,
    NamingOverrideOptions existingOverrides)
{
    if (!options.TryGetValue("--rename-table", out var rawOverrides) || string.IsNullOrWhiteSpace(rawOverrides))
    {
        return existingOverrides;
    }

    var separators = new[] { ';', ',' };
    var tokens = rawOverrides.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (tokens.Length == 0)
    {
        return existingOverrides;
    }

    var parsedOverrides = new List<NamingOverrideRule>();
    foreach (var token in tokens)
    {
        var assignment = token.Split('=', 2, StringSplitOptions.TrimEntries);
        if (assignment.Length != 2)
        {
            return ValidationError.Create(
                "cli.rename.invalidFormat",
                $"Invalid table rename '{token}'. Expected format source=OverrideName.");
        }

        var source = assignment[0];
        var replacement = assignment[1];

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(replacement))
        {
            return ValidationError.Create(
                "cli.rename.missingValue",
                "Naming overrides must include both source and replacement values.");
        }

        string? schema = null;
        string? tableName = null;
        string? module = null;
        string? entity = null;

        var segments = source.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            segments = new[] { source };
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            if (segment.Contains("::", StringComparison.Ordinal))
            {
                var logicalParts = segment.Split("::", 2, StringSplitOptions.TrimEntries);
                if (logicalParts.Length != 2 || string.IsNullOrWhiteSpace(logicalParts[1]))
                {
                    return ValidationError.Create(
                        "cli.rename.invalidLogical",
                        $"Invalid logical source '{segment}'. Expected format Module::Entity.");
                }

                module = string.IsNullOrWhiteSpace(logicalParts[0]) ? null : logicalParts[0];
                entity = logicalParts[1];
            }
            else if (segment.Contains('.', StringComparison.Ordinal))
            {
                var physicalParts = segment.Split('.', 2, StringSplitOptions.TrimEntries);
                if (physicalParts.Length != 2 || string.IsNullOrWhiteSpace(physicalParts[1]))
                {
                    return ValidationError.Create(
                        "cli.rename.invalidTable",
                        $"Invalid table source '{segment}'. Expected schema.table or table.");
                }

                schema = physicalParts[0];
                tableName = physicalParts[1];
            }
            else
            {
                tableName = segment;
            }
        }

        var overrideResult = NamingOverrideRule.Create(schema, tableName, module, entity, replacement);
        if (overrideResult.IsFailure)
        {
            return Result<NamingOverrideOptions>.Failure(overrideResult.Errors);
        }

        parsedOverrides.Add(overrideResult.Value);
    }

    return existingOverrides.MergeWith(parsedOverrides);
}

static Result<ModuleFilterOptions> ResolveModuleFilterOptions(
    Dictionary<string, string?> options,
    ModuleFilterConfiguration configuration)
{
    if (options is null)
    {
        throw new ArgumentNullException(nameof(options));
    }

    if (configuration is null)
    {
        throw new ArgumentNullException(nameof(configuration));
    }

    var includeSystemModules = configuration.IncludeSystemModules ?? true;
    var includeInactiveModules = configuration.IncludeInactiveModules ?? true;

    var configuredModules = configuration.Modules?.Count > 0
        ? configuration.Modules.ToArray()
        : Array.Empty<string>();

    var cliModules = ResolveModuleArguments(options);
    if (cliModules.Count > 0)
    {
        configuredModules = cliModules.ToArray();
    }

    if (options.ContainsKey("--exclude-system-modules"))
    {
        includeSystemModules = false;
    }
    else if (options.ContainsKey("--include-system-modules"))
    {
        includeSystemModules = true;
    }

    if (options.ContainsKey("--only-active-modules"))
    {
        includeInactiveModules = false;
    }
    else if (options.ContainsKey("--include-inactive-modules"))
    {
        includeInactiveModules = true;
    }

    return ModuleFilterOptions.Create(configuredModules, includeSystemModules, includeInactiveModules);
}

static IReadOnlyList<string> ResolveModuleArguments(Dictionary<string, string?> options)
{
    if (options.TryGetValue("--modules", out var modulesValue) && !string.IsNullOrWhiteSpace(modulesValue))
    {
        return SplitModuleList(modulesValue);
    }

    if (options.TryGetValue("--module", out var moduleValue) && !string.IsNullOrWhiteSpace(moduleValue))
    {
        return SplitModuleList(moduleValue);
    }

    return Array.Empty<string>();
}

static IReadOnlyList<string> SplitModuleList(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return Array.Empty<string>();
    }

    var separators = new[] { ',', ';' };
    var tokens = value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (tokens.Length == 0)
    {
        return Array.Empty<string>();
    }

    return tokens;
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
    Console.WriteLine("  extract-model [--connection-string <value>] [--command-timeout <seconds>] [--sql-authentication <method>] [--sql-access-token <token>] [--sql-trust-server-certificate [true|false]] [--sql-application-name <name>] [--sampling-threshold <rows>] [--sampling-size <rows>] [--mock-advanced-sql <manifest.json>] [--modules <csv>] [--include-system-modules] [--only-active-attributes] [--out <path>] [--config <path>]");
    Console.WriteLine("  build-ssdt --model <model.json> [--profile <profile.json>] [--profiler-provider <fixture|sql>] [--connection-string <value>] [--command-timeout <seconds>] [--sql-authentication <method>] [--sql-access-token <token>] [--sql-trust-server-certificate [true|false]] [--sql-application-name <name>] [--sampling-threshold <rows>] [--sampling-size <rows>] [--modules <csv>] [--exclude-system-modules] [--only-active-modules] [--out <dir>] [--config <path>] [--cache-root <dir>] [--refresh-cache] [--rename-table schema.table=Override]");
    Console.WriteLine("  dmm-compare --model <model.json> --profile <profile.json> --dmm <dmm.sql> [--modules <csv>] [--exclude-system-modules] [--only-active-modules] [--config <path>] [--out <path>] [--cache-root <dir>] [--refresh-cache]");
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintRootUsage();
    return 1;
}

file sealed record ResolvedSqlOptions(
    string? ConnectionString,
    int? CommandTimeoutSeconds,
    SqlSamplingConfiguration Sampling,
    SqlAuthenticationConfiguration Authentication);
file sealed record CliConfigurationContext(CliConfiguration Configuration, string? ConfigPath);

file sealed record PolicyDecisionLog(
    int ColumnCount,
    int TightenedColumnCount,
    int RemediationColumnCount,
    int UniqueIndexCount,
    int UniqueIndexesEnforcedCount,
    int UniqueIndexesRequireRemediationCount,
    int ForeignKeyCount,
    int ForeignKeysCreatedCount,
    IReadOnlyDictionary<string, int> ColumnRationales,
    IReadOnlyDictionary<string, int> UniqueIndexRationales,
    IReadOnlyDictionary<string, int> ForeignKeyRationales,
    IReadOnlyList<PolicyDecisionLogColumn> Columns,
    IReadOnlyList<PolicyDecisionLogUniqueIndex> UniqueIndexes,
    IReadOnlyList<PolicyDecisionLogForeignKey> ForeignKeys);

file sealed record PolicyDecisionLogColumn(
    string Schema,
    string Table,
    string Column,
    bool MakeNotNull,
    bool RequiresRemediation,
    IReadOnlyList<string> Rationales);

file sealed record PolicyDecisionLogUniqueIndex(
    string Schema,
    string Table,
    string Index,
    bool EnforceUnique,
    bool RequiresRemediation,
    IReadOnlyList<string> Rationales);

file sealed record PolicyDecisionLogForeignKey(
    string Schema,
    string Table,
    string Column,
    bool CreateConstraint,
    IReadOnlyList<string> Rationales);

file sealed record DmmDiffLog(
    bool IsMatch,
    string ModelPath,
    string ProfilePath,
    string DmmPath,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<string> ModelDifferences,
    IReadOnlyList<string> SsdtDifferences);
