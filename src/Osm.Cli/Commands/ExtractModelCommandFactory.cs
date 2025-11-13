using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Options;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class ExtractModelCommandFactory : PipelineCommandFactory<ExtractModelVerbOptions, ExtractModelVerbResult>
{
    private readonly VerbOptionDeclaration<ExtractModelOverrides> _verbOptions;

    public ExtractModelCommandFactory(
        IServiceScopeFactory scopeFactory,
        VerbOptionRegistry optionRegistry)
        : base(scopeFactory)
    {
        if (optionRegistry is null)
        {
            throw new ArgumentNullException(nameof(optionRegistry));
        }

        _verbOptions = optionRegistry.ExtractModel;
    }

    protected override string VerbName => ExtractModelVerb.VerbName;

    protected override Command CreateCommandCore()
    {
        var command = new Command("extract-model", "Extract the OutSystems model using Advanced SQL.");
        _verbOptions.Configure(command);
        return command;
    }

    protected override ExtractModelVerbOptions BindOptions(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var bound = _verbOptions.Bind(context.ParseResult);

        if (bound.Sql is null)
        {
            throw new InvalidOperationException("SQL overrides missing.");
        }

        return new ExtractModelVerbOptions
        {
            ConfigurationPath = bound.ConfigurationPath,
            Overrides = bound.Overrides,
            Sql = bound.Sql,
            SqlMetadataOutputPath = bound.Overrides.SqlMetadataOutputPath
        };
    }

    protected override async Task<int> OnRunSucceededAsync(InvocationContext context, ExtractModelVerbResult payload)
    {
        await EmitResultsAsync(context, payload).ConfigureAwait(false);
        return 0;
    }

    private async Task EmitResultsAsync(InvocationContext context, ExtractModelVerbResult verbResult)
    {
        var result = verbResult.ApplicationResult;
        var requestedOutputPath = result.OutputPath ?? "model.extracted.json";
        var cancellationToken = context.GetCancellationToken();
        var extractionPayload = result.ExtractionResult.JsonPayload;

        string resolvedOutputPath;
        if (extractionPayload.IsPersisted)
        {
            var persistedPath = extractionPayload.FilePath;
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
                await extractionPayload.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
                resolvedOutputPath = requestedFullPath;
            }
        }
        else
        {
            var requestedFullPath = Path.GetFullPath(requestedOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(requestedFullPath) ?? Directory.GetCurrentDirectory());
            await using var outputStream = File.Create(requestedFullPath);
            await extractionPayload.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            resolvedOutputPath = requestedFullPath;
        }

        CommandConsole.EmitExtractModelSummary(context.Console, result, resolvedOutputPath);
    }
}
