using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;

namespace Osm.Cli;

internal static class ExtractCommandFactory
{
    public static Command Create()
    {
        var configOption = CliOptions.CreateConfigOption();
        var modulesOption = CliOptions.CreateModulesOption();
        var includeSystemOption = CliOptions.CreateIncludeSystemModulesOption("Include system modules during extraction.");
        var excludeSystemOption = CliOptions.CreateExcludeSystemModulesOption("Exclude system modules during extraction.");
        var onlyActiveAttributesOption = new Option<bool>("--only-active-attributes", "Extract only active attributes.");
        var includeInactiveAttributesOption = new Option<bool>("--include-inactive-attributes", "Include inactive attributes when extracting.");
        var sqlOptions = CliOptions.CreateSqlOptionSet();

        var outputOption = new Option<string?>("--out", () => "model.extracted.json", "Output path for extracted model JSON.");
        var mockSqlOption = new Option<string?>("--mock-advanced-sql", "Path to advanced SQL manifest fixture.");

        var command = new Command("extract-model", "Extract the OutSystems model using Advanced SQL.")
        {
            modulesOption,
            includeSystemOption,
            excludeSystemOption,
            onlyActiveAttributesOption,
            includeInactiveAttributesOption,
            outputOption,
            mockSqlOption
        };

        command.AddGlobalOption(configOption);
        CliCommandUtilities.AddSqlOptions(command, sqlOptions);

        command.SetHandler(async context =>
        {
            var rootServices = CliCommandUtilities.GetServices(context);
            using var scope = rootServices.CreateScope();
            var services = scope.ServiceProvider;
            var configurationService = services.GetRequiredService<ICliConfigurationService>();
            var application = services.GetRequiredService<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>>();

            var configPath = context.ParseResult.GetValueForOption(configOption);
            var configurationResult = await configurationService.LoadAsync(configPath, context.GetCancellationToken()).ConfigureAwait(false);
            if (configurationResult.IsFailure)
            {
                CliCommandUtilities.WriteErrors(context, configurationResult.Errors);
                context.ExitCode = 1;
                return;
            }

            var modules = CliCommandUtilities.SplitModuleList(context.ParseResult.GetValueForOption(modulesOption));
            var moduleOverride = modules.Count > 0 ? modules : null;
            var includeSystemOverride = CliCommandUtilities.ResolveIncludeOverride(context, includeSystemOption, excludeSystemOption);
            var onlyActiveOverride = CliCommandUtilities.ResolveOnlyActiveOverride(context, onlyActiveAttributesOption, includeInactiveAttributesOption);
            var overrides = new ExtractModelOverrides(
                moduleOverride,
                includeSystemOverride,
                onlyActiveOverride,
                context.ParseResult.GetValueForOption(outputOption),
                context.ParseResult.GetValueForOption(mockSqlOption));

            var input = new ExtractModelApplicationInput(
                configurationResult.Value,
                overrides,
                CliCommandUtilities.CreateSqlOverrides(context.ParseResult, sqlOptions));

            var result = await application.RunAsync(input, context.GetCancellationToken()).ConfigureAwait(false);
            if (result.IsFailure)
            {
                CliCommandUtilities.WriteErrors(context, result.Errors);
                context.ExitCode = 1;
                return;
            }

            var outputPath = result.Value.OutputPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Directory.GetCurrentDirectory());
            await File.WriteAllTextAsync(outputPath, result.Value.ExtractionResult.Json, context.GetCancellationToken()).ConfigureAwait(false);

            var model = result.Value.ExtractionResult.Model;
            var moduleCount = model.Modules.Length;
            var entityCount = model.Modules.Sum(static m => m.Entities.Length);
            var attributeCount = model.Modules.Sum(static m => m.Entities.Sum(static e => e.Attributes.Length));

            if (result.Value.ExtractionResult.Warnings.Count > 0)
            {
                foreach (var warning in result.Value.ExtractionResult.Warnings)
                {
                    CliCommandUtilities.WriteErrorLine(context.Console, $"Warning: {warning}");
                }
            }

            CliCommandUtilities.WriteLine(context.Console, $"Extracted {moduleCount} modules spanning {entityCount} entities.");
            CliCommandUtilities.WriteLine(context.Console, $"Attributes: {attributeCount}");
            CliCommandUtilities.WriteLine(context.Console, $"Model written to {outputPath}.");
            CliCommandUtilities.WriteLine(context.Console, $"Extraction timestamp (UTC): {result.Value.ExtractionResult.ExtractedAtUtc:O}");
            context.ExitCode = 0;
        });

        return command;
    }
}
