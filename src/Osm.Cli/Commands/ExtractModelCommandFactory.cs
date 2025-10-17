using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class ExtractModelCommandFactory : ICommandFactory
{
    private readonly PipelineVerbExecutor _verbExecutor;
    private readonly CliGlobalOptions _globalOptions;
    private readonly SqlOptionBinder _sqlOptionBinder;

    private readonly Option<string?> _modulesOption = new("--modules", "Comma or semicolon separated list of modules.");
    private readonly Option<bool> _includeSystemOption = new("--include-system-modules", "Include system modules during extraction.");
    private readonly Option<bool> _excludeSystemOption = new("--exclude-system-modules", "Exclude system modules during extraction.");
    private readonly Option<bool> _onlyActiveAttributesOption = new("--only-active-attributes", "Extract only active attributes.");
    private readonly Option<bool> _includeInactiveAttributesOption = new("--include-inactive-attributes", "Include inactive attributes when extracting.");
    private readonly Option<string?> _outputOption = new("--out", () => "model.extracted.json", "Output path for extracted model JSON.");
    private readonly Option<string?> _mockSqlOption = new("--mock-advanced-sql", "Path to advanced SQL manifest fixture.");

    public ExtractModelCommandFactory(
        PipelineVerbExecutor verbExecutor,
        CliGlobalOptions globalOptions,
        SqlOptionBinder sqlOptionBinder)
    {
        _verbExecutor = verbExecutor ?? throw new ArgumentNullException(nameof(verbExecutor));
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _sqlOptionBinder = sqlOptionBinder ?? throw new ArgumentNullException(nameof(sqlOptionBinder));
        _modulesOption.AddAlias("--module");
    }

    public Command Create()
    {
        var command = new Command("extract-model", "Extract the OutSystems model using Advanced SQL.")
        {
            _modulesOption,
            _includeSystemOption,
            _excludeSystemOption,
            _onlyActiveAttributesOption,
            _includeInactiveAttributesOption,
            _outputOption,
            _mockSqlOption
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        CommandOptionBuilder.AddSqlOptions(command, _sqlOptionBinder);

        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var moduleTokens = ModuleFilterOptionBinder.SplitList(parseResult.GetValueForOption(_modulesOption));
        IReadOnlyList<string>? moduleOverride = moduleTokens.Count > 0 ? moduleTokens : null;
        var includeSystemOverride = ModuleFilterOptionBinder.ResolveToggle(parseResult, _includeSystemOption, _excludeSystemOption);
        var onlyActiveOverride = ResolveOnlyActiveOverride(parseResult);

        var overrides = new ExtractModelOverrides(
            moduleOverride,
            includeSystemOverride,
            onlyActiveOverride,
            parseResult.GetValueForOption(_outputOption),
            parseResult.GetValueForOption(_mockSqlOption));

        var options = new ExtractModelVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            Sql = _sqlOptionBinder.Bind(parseResult)
        };

        var execution = await _verbExecutor
            .ExecuteAsync<ExtractModelVerbResult>(context, ExtractModelVerb.VerbName, options, context.GetCancellationToken())
            .ConfigureAwait(false);

        if (!execution.IsSuccess || execution.Payload is not { } payload)
        {
            return;
        }

        await EmitResultsAsync(context, payload).ConfigureAwait(false);
        context.ExitCode = 0;
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
        var outputPath = result.OutputPath ?? "model.extracted.json";
        var cancellationToken = context.GetCancellationToken();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Directory.GetCurrentDirectory());
        await using (var outputStream = File.Create(outputPath))
        {
            await result.ExtractionResult.JsonPayload.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
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
        CommandConsole.WriteLine(context.Console, $"Model written to {outputPath}.");
        CommandConsole.WriteLine(context.Console, $"Extraction timestamp (UTC): {result.ExtractionResult.ExtractedAtUtc:O}");
    }
}
