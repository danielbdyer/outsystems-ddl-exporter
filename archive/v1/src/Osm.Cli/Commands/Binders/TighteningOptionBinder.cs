using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Binders;

internal sealed class TighteningOptionBinder : BinderBase<TighteningOverrides?>, ICommandOptionSource
{
    public TighteningOptionBinder()
    {
        RemediationGeneratePreScriptsOption = CreateBooleanOption(
            "--remediation-generate-pre-scripts",
            "Enable or disable generation of remediation SQL pre-scripts.");

        RemediationMaxRowsOption = new Option<int?>(
            "--remediation-max-rows-default-backfill",
            "Override the maximum number of rows allowed in default remediation backfill batches.");

        RemediationSentinelNumericOption = new Option<string?>(
            "--remediation-sentinel-numeric",
            "Override the numeric remediation sentinel value emitted in pre-scripts.");

        RemediationSentinelTextOption = new Option<string?>(
            "--remediation-sentinel-text",
            "Override the text remediation sentinel value emitted in pre-scripts.");

        RemediationSentinelDateOption = new Option<string?>(
            "--remediation-sentinel-date",
            "Override the date remediation sentinel value emitted in pre-scripts.");

        UseProfileMockFolderOption = CreateBooleanOption(
            "--use-profile-mock-folder",
            "Toggle use of profile mock fixtures instead of live profiling.");

        ProfileMockFolderOption = new Option<string?>(
            "--profile-mock-folder",
            "Path to the folder containing profiling mock fixtures.");
    }

    public Option<bool?> RemediationGeneratePreScriptsOption { get; }

    public Option<int?> RemediationMaxRowsOption { get; }

    public Option<string?> RemediationSentinelNumericOption { get; }

    public Option<string?> RemediationSentinelTextOption { get; }

    public Option<string?> RemediationSentinelDateOption { get; }

    public Option<bool?> UseProfileMockFolderOption { get; }

    public Option<string?> ProfileMockFolderOption { get; }

    public IEnumerable<Option> Options
    {
        get
        {
            yield return RemediationGeneratePreScriptsOption;
            yield return RemediationMaxRowsOption;
            yield return RemediationSentinelNumericOption;
            yield return RemediationSentinelTextOption;
            yield return RemediationSentinelDateOption;
            yield return UseProfileMockFolderOption;
            yield return ProfileMockFolderOption;
        }
    }

    protected override TighteningOverrides? GetBoundValue(BindingContext bindingContext)
        => Bind(bindingContext?.ParseResult ?? throw new ArgumentNullException(nameof(bindingContext)));

    public TighteningOverrides? Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        var remediationGenerate = parseResult.GetValueForOption(RemediationGeneratePreScriptsOption);
        var remediationMaxRows = parseResult.GetValueForOption(RemediationMaxRowsOption);
        var remediationSentinelNumeric = TrimOrNullWhenEmpty(
            parseResult.GetValueForOption(RemediationSentinelNumericOption));
        var remediationSentinelText = TrimAllowEmpty(
            parseResult.GetValueForOption(RemediationSentinelTextOption));
        var remediationSentinelDate = TrimOrNullWhenEmpty(
            parseResult.GetValueForOption(RemediationSentinelDateOption));
        var useMockFolder = parseResult.GetValueForOption(UseProfileMockFolderOption);
        var mockFolder = Normalize(parseResult.GetValueForOption(ProfileMockFolderOption));

        var overrides = new TighteningOverrides(
            remediationGenerate,
            remediationMaxRows,
            useMockFolder,
            mockFolder,
            remediationSentinelNumeric,
            remediationSentinelText,
            remediationSentinelDate);
        return overrides.HasOverrides ? overrides : null;
    }

    private static Option<bool?> CreateBooleanOption(string alias, string description)
    {
        return new Option<bool?>(alias, ParseOptionalBoolean)
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = description,
            ArgumentHelpName = "true|false"
        };
    }

    private static bool? ParseOptionalBoolean(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return true;
        }

        var token = result.Tokens[0].Value;
        if (bool.TryParse(token, out var parsed))
        {
            return parsed;
        }

        result.ErrorMessage = "Invalid boolean value. Expected 'true' or 'false'.";
        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? TrimOrNullWhenEmpty(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? TrimAllowEmpty(string? value)
    {
        return value?.Trim();
    }
}
