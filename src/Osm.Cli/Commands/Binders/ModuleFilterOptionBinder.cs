using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Binders;

internal sealed class ModuleFilterOptionBinder : BinderBase<ModuleFilterOverrides>, ICommandOptionSource
{
    private static readonly char[] ModuleSeparators = new[] { ',', ';' };

    public ModuleFilterOptionBinder()
    {
        ModulesOption = new Option<string?>("--modules", "Comma or semicolon separated list of modules.");
        ModulesOption.AddAlias("--module");

        IncludeSystemModulesOption = new Option<bool>("--include-system-modules", "Include system modules when filtering.");
        ExcludeSystemModulesOption = new Option<bool>("--exclude-system-modules", "Exclude system modules when filtering.");
        IncludeInactiveModulesOption = new Option<bool>("--include-inactive-modules", "Include inactive modules when filtering.");
        OnlyActiveModulesOption = new Option<bool>("--only-active-modules", "Restrict filtering to active modules only.");
        AllowMissingPrimaryKeyOption = CreateOverrideOption("--allow-missing-primary-key", "Allow ingestion to include entities without primary keys. Use Module::Entity or Module::*.");
        AllowMissingSchemaOption = CreateOverrideOption("--allow-missing-schema", "Allow ingestion to include entities without schema names. Use Module::Entity or Module::*.");
    }

    public Option<string?> ModulesOption { get; }

    public Option<bool> IncludeSystemModulesOption { get; }

    public Option<bool> ExcludeSystemModulesOption { get; }

    public Option<bool> IncludeInactiveModulesOption { get; }

    public Option<bool> OnlyActiveModulesOption { get; }

    public Option<string[]?> AllowMissingPrimaryKeyOption { get; }

    public Option<string[]?> AllowMissingSchemaOption { get; }

    public IEnumerable<Option> Options
    {
        get
        {
            yield return ModulesOption;
            yield return IncludeSystemModulesOption;
            yield return ExcludeSystemModulesOption;
            yield return IncludeInactiveModulesOption;
            yield return OnlyActiveModulesOption;
            yield return AllowMissingPrimaryKeyOption;
            yield return AllowMissingSchemaOption;
        }
    }

    protected override ModuleFilterOverrides GetBoundValue(BindingContext bindingContext)
        => Bind(bindingContext?.ParseResult ?? throw new ArgumentNullException(nameof(bindingContext)));

    public ModuleFilterOverrides Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        var modules = SplitList(parseResult.GetValueForOption(ModulesOption));
        var includeSystem = ResolveToggle(parseResult, IncludeSystemModulesOption, ExcludeSystemModulesOption);
        var includeInactive = ResolveToggle(parseResult, IncludeInactiveModulesOption, OnlyActiveModulesOption);
        var allowMissingPrimaryKey = SplitList(parseResult.GetValueForOption(AllowMissingPrimaryKeyOption));
        var allowMissingSchema = SplitList(parseResult.GetValueForOption(AllowMissingSchemaOption));

        return new ModuleFilterOverrides(modules, includeSystem, includeInactive, allowMissingPrimaryKey, allowMissingSchema);
    }

    private static Option<string[]?> CreateOverrideOption(string name, string description)
        => new(name, description)
        {
            AllowMultipleArgumentsPerToken = true
        };

    internal static IReadOnlyList<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split(ModuleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static IReadOnlyList<string> SplitList(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var tokens = value.Split(ModuleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    results.Add(token);
                }
            }
        }

        return results;
    }

    internal static bool? ResolveToggle(ParseResult parseResult, Option<bool> positive, Option<bool> negative)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        if (positive is null)
        {
            throw new ArgumentNullException(nameof(positive));
        }

        if (negative is null)
        {
            throw new ArgumentNullException(nameof(negative));
        }

        if (parseResult.HasOption(positive))
        {
            return true;
        }

        if (parseResult.HasOption(negative))
        {
            return false;
        }

        return null;
    }
}
