using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class ExtractModelCommandFactory : PipelineCommandFactory<ExtractModelVerbOptions, ExtractModelVerbResult>
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;

    private readonly Option<bool> _onlyActiveAttributesOption = new("--only-active-attributes", "Extract only active attributes.");
    private readonly Option<bool> _includeInactiveAttributesOption = new("--include-inactive-attributes", "Include inactive attributes when extracting.");
    private readonly Option<string?> _outputOption = new("--out", () => "model.extracted.json", "Output path for extracted model JSON.");
    private readonly Option<string?> _mockSqlOption = new("--mock-advanced-sql", "Path to advanced SQL manifest fixture.");
    private readonly Option<string?> _sqlMetadataOption = new("--sql-metadata-out", "Path to write SQL metadata diagnostics (JSON).");

    public ExtractModelCommandFactory(
        IServiceScopeFactory scopeFactory,
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        SqlOptionBinder sqlOptionBinder)
        : base(scopeFactory)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _moduleFilterBinder = moduleFilterBinder ?? throw new ArgumentNullException(nameof(moduleFilterBinder));
        _sqlOptionBinder = sqlOptionBinder ?? throw new ArgumentNullException(nameof(sqlOptionBinder));
    }

    protected override string VerbName => ExtractModelVerb.VerbName;

    protected override Command CreateCommandCore()
    {
        var command = new Command("extract-model", "Extract the OutSystems model using Advanced SQL.")
        {
            _onlyActiveAttributesOption,
            _includeInactiveAttributesOption,
            _outputOption,
            _mockSqlOption,
            _sqlMetadataOption
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        CommandOptionBuilder.AddModuleFilterOptions(command, _moduleFilterBinder);
        CommandOptionBuilder.AddSqlOptions(command, _sqlOptionBinder);
        return command;
    }

    protected override ExtractModelVerbOptions BindOptions(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var parseResult = context.ParseResult;
        var moduleFilter = _moduleFilterBinder.Bind(parseResult);
        IReadOnlyList<string>? moduleOverride = moduleFilter.Modules.Count > 0 ? moduleFilter.Modules : null;
        var includeSystemOverride = moduleFilter.IncludeSystemModules;
        var onlyActiveOverride = ResolveOnlyActiveOverride(parseResult);

        var overrides = new ExtractModelOverrides(
            moduleOverride,
            includeSystemOverride,
            onlyActiveOverride,
            parseResult.GetValueForOption(_outputOption),
            parseResult.GetValueForOption(_mockSqlOption),
            parseResult.GetValueForOption(_sqlMetadataOption));

        return new ExtractModelVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            Sql = _sqlOptionBinder.Bind(parseResult),
            SqlMetadataOutputPath = overrides.SqlMetadataOutputPath
        };
    }

    protected override async Task<int> OnRunSucceededAsync(InvocationContext context, ExtractModelVerbResult payload)
    {
        await EmitResultsAsync(context, payload).ConfigureAwait(false);
        return 0;
    }

    private bool? ResolveOnlyActiveOverride(ParseResult parseResult)
    {
        if (parseResult.HasOption(_onlyActiveAttributesOption))
        {
            return true;
        }

        if (parseResult.HasOption(_includeInactiveAttributesOption))
        {
            return false;
        }

        return null;
    }

    private async Task EmitResultsAsync(InvocationContext context, ExtractModelVerbResult payload)
    {
        var result = payload.ApplicationResult;
        var requestedOutputPath = result.OutputPath ?? "model.extracted.json";
        var cancellationToken = context.GetCancellationToken();
        var payload = result.ExtractionResult.JsonPayload;

        string resolvedOutputPath;
        if (payload.IsPersisted)
        {
            var persistedPath = payload.FilePath;
            var requestedFullPath = Path.GetFullPath(requestedOutputPath);
            if (!string.IsNullOrWhiteSpace(persistedPath)
                && string.Equals(persistedPath, requestedFullPath, StringComparison.OrdinalIgnoreCase))
            {
                resolvedOutputPath = persistedPath;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(requestedFullPath) ?? Directory.GetCurrentDirectory());
                await using var outputStream = File.Create(requestedFullPath);
                await payload.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
                resolvedOutputPath = requestedFullPath;
            }
        }
        else
        {
            var requestedFullPath = Path.GetFullPath(requestedOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(requestedFullPath) ?? Directory.GetCurrentDirectory());
            await using var outputStream = File.Create(requestedFullPath);
            await payload.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            resolvedOutputPath = requestedFullPath;
        }

        var model = result.ExtractionResult.Model;
        var moduleCount = model.Modules.Length;
        var entityCount = model.Modules.Sum(static m => m.Entities.Length);
        var attributeCount = model.Modules.Sum(static m => m.Entities.Sum(static e => e.Attributes.Length));

        if (result.ExtractionResult.Warnings.Count > 0)
        {
            foreach (var warning in result.ExtractionResult.Warnings)
            {
                CommandConsole.WriteErrorLine(context.Console, $"Warning: {warning}");
            }
        }

        CommandConsole.WriteLine(context.Console, $"Extracted {moduleCount} modules spanning {entityCount} entities.");
        CommandConsole.WriteLine(context.Console, $"Attributes: {attributeCount}");
        CommandConsole.WriteLine(context.Console, $"Model written to {resolvedOutputPath}.");
        CommandConsole.WriteLine(context.Console, $"Extraction timestamp (UTC): {result.ExtractionResult.ExtractedAtUtc:O}");
    }
}
