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
using Osm.Pipeline.Configuration;

namespace Osm.Cli.Commands;

internal sealed class ExtractModelCommandModule : ICommandModule
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliGlobalOptions _globalOptions;
    private readonly SqlOptionBinder _sqlOptionBinder;

    private readonly Option<string?> _modulesOption = new("--modules", "Comma or semicolon separated list of modules.");
    private readonly Option<bool> _includeSystemOption = new("--include-system-modules", "Include system modules during extraction.");
    private readonly Option<bool> _excludeSystemOption = new("--exclude-system-modules", "Exclude system modules during extraction.");
    private readonly Option<bool> _onlyActiveAttributesOption = new("--only-active-attributes", "Extract only active attributes.");
    private readonly Option<bool> _includeInactiveAttributesOption = new("--include-inactive-attributes", "Include inactive attributes when extracting.");
    private readonly Option<string?> _outputOption = new("--out", () => "model.extracted.json", "Output path for extracted model JSON.");
    private readonly Option<string?> _mockSqlOption = new("--mock-advanced-sql", "Path to advanced SQL manifest fixture.");

    public ExtractModelCommandModule(
        IServiceScopeFactory scopeFactory,
        CliGlobalOptions globalOptions,
        SqlOptionBinder sqlOptionBinder)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _sqlOptionBinder = sqlOptionBinder ?? throw new ArgumentNullException(nameof(sqlOptionBinder));
        _modulesOption.AddAlias("--module");
    }

    public Command BuildCommand()
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
        foreach (var option in _sqlOptionBinder.Options)
        {
            command.AddOption(option);
        }

        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var configurationService = services.GetRequiredService<ICliConfigurationService>();
        var application = services.GetRequiredService<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>>();

        var cancellationToken = context.GetCancellationToken();
        var configPath = context.ParseResult.GetValueForOption(_globalOptions.ConfigPath);
        var configurationResult = await configurationService.LoadAsync(configPath, cancellationToken).ConfigureAwait(false);
        if (configurationResult.IsFailure)
        {
            CommandConsole.WriteErrors(context.Console, configurationResult.Errors);
            context.ExitCode = 1;
            return;
        }

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

        var input = new ExtractModelApplicationInput(
            configurationResult.Value,
            overrides,
            _sqlOptionBinder.Bind(context.ParseResult));

        var result = await application.RunAsync(input, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            CommandConsole.WriteErrors(context.Console, result.Errors);
            context.ExitCode = 1;
            return;
        }

        await EmitResultsAsync(context, result.Value).ConfigureAwait(false);
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

    private async Task EmitResultsAsync(InvocationContext context, ExtractModelApplicationResult result)
    {
        var outputPath = result.OutputPath ?? "model.extracted.json";
        var cancellationToken = context.GetCancellationToken();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Directory.GetCurrentDirectory());
        await File.WriteAllTextAsync(outputPath, result.ExtractionResult.Json, cancellationToken).ConfigureAwait(false);

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
