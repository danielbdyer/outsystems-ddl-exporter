using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Hosting;
using Osm.Pipeline.Hosting.Verbs;

namespace Osm.Cli.Commands;

internal sealed class ExtractModelCommandFactory : ICommandFactory
{
    private readonly IPipelineVerb<ExtractModelVerbOptions> _verb;
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
        IPipelineVerb<ExtractModelVerbOptions> verb,
        CliGlobalOptions globalOptions,
        SqlOptionBinder sqlOptionBinder)
    {
        _verb = verb ?? throw new ArgumentNullException(nameof(verb));
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
        var cancellationToken = context.GetCancellationToken();
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

        var verbOptions = new ExtractModelVerbOptions(
            parseResult.GetValueForOption(_globalOptions.ConfigPath),
            overrides,
            _sqlOptionBinder.Bind(context.ParseResult));

        var result = await _verb.RunAsync(verbOptions, cancellationToken).ConfigureAwait(false);
        context.ExitCode = result.ExitCode;
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
}
