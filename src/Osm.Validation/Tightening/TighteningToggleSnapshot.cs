using System;
using System.Collections.Generic;
using Osm.Domain.Configuration;

namespace Osm.Validation.Tightening;

public enum ToggleSource
{
    Default,
    Configuration,
    Environment,
    CommandLine
}

public static class TighteningToggleKeys
{
    public const string PolicyMode = "policy.mode";
    public const string PolicyNullBudget = "policy.nullBudget";
    public const string ForeignKeysEnableCreation = "foreignKeys.enableCreation";
    public const string ForeignKeysAllowCrossSchema = "foreignKeys.allowCrossSchema";
    public const string ForeignKeysAllowCrossCatalog = "foreignKeys.allowCrossCatalog";
    public const string UniquenessEnforceSingleColumn = "uniqueness.enforceSingleColumn";
    public const string UniquenessEnforceMultiColumn = "uniqueness.enforceMultiColumn";
    public const string RemediationGeneratePreScripts = "remediation.generatePreScripts";
    public const string RemediationMaxRowsDefaultBackfill = "remediation.maxRowsDefaultBackfill";
}

public sealed record ToggleState<T>(T Value, ToggleSource Source);

public sealed record ToggleExportValue(object? Value, ToggleSource Source)
{
    public static ToggleExportValue From<T>(ToggleState<T> state, Func<T, object?>? converter = null)
    {
        var value = converter is null ? state.Value : converter(state.Value);
        return new ToggleExportValue(value, state.Source);
    }
}

public sealed record TighteningToggleSnapshot(
    ToggleState<TighteningMode> Mode,
    ToggleState<double> NullBudget,
    ToggleState<bool> ForeignKeyCreationEnabled,
    ToggleState<bool> ForeignKeyCrossSchemaAllowed,
    ToggleState<bool> ForeignKeyCrossCatalogAllowed,
    ToggleState<bool> SingleColumnUniqueEnforced,
    ToggleState<bool> MultiColumnUniqueEnforced,
    ToggleState<bool> RemediationGeneratePreScripts,
    ToggleState<int> RemediationMaxRowsDefaultBackfill)
{
    public static TighteningToggleSnapshot Create(
        TighteningOptions options,
        Func<string, ToggleSource?>? sourceResolver = null,
        TighteningOptions? baseline = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        baseline ??= TighteningOptions.Default;

        return new TighteningToggleSnapshot(
            Resolve(TighteningToggleKeys.PolicyMode, options.Policy.Mode, baseline.Policy.Mode, sourceResolver, static mode => mode.ToString()),
            Resolve(TighteningToggleKeys.PolicyNullBudget, options.Policy.NullBudget, baseline.Policy.NullBudget, sourceResolver),
            Resolve(TighteningToggleKeys.ForeignKeysEnableCreation, options.ForeignKeys.EnableCreation, baseline.ForeignKeys.EnableCreation, sourceResolver),
            Resolve(TighteningToggleKeys.ForeignKeysAllowCrossSchema, options.ForeignKeys.AllowCrossSchema, baseline.ForeignKeys.AllowCrossSchema, sourceResolver),
            Resolve(TighteningToggleKeys.ForeignKeysAllowCrossCatalog, options.ForeignKeys.AllowCrossCatalog, baseline.ForeignKeys.AllowCrossCatalog, sourceResolver),
            Resolve(TighteningToggleKeys.UniquenessEnforceSingleColumn, options.Uniqueness.EnforceSingleColumnUnique, baseline.Uniqueness.EnforceSingleColumnUnique, sourceResolver),
            Resolve(TighteningToggleKeys.UniquenessEnforceMultiColumn, options.Uniqueness.EnforceMultiColumnUnique, baseline.Uniqueness.EnforceMultiColumnUnique, sourceResolver),
            Resolve(TighteningToggleKeys.RemediationGeneratePreScripts, options.Remediation.GeneratePreScripts, baseline.Remediation.GeneratePreScripts, sourceResolver),
            Resolve(TighteningToggleKeys.RemediationMaxRowsDefaultBackfill, options.Remediation.MaxRowsDefaultBackfill, baseline.Remediation.MaxRowsDefaultBackfill, sourceResolver));
    }

    public IReadOnlyDictionary<string, ToggleExportValue> ToExportDictionary()
    {
        var dictionary = new Dictionary<string, ToggleExportValue>(StringComparer.OrdinalIgnoreCase)
        {
            [TighteningToggleKeys.PolicyMode] = ToggleExportValue.From(Mode, static mode => mode.ToString()),
            [TighteningToggleKeys.PolicyNullBudget] = ToggleExportValue.From(NullBudget),
            [TighteningToggleKeys.ForeignKeysEnableCreation] = ToggleExportValue.From(ForeignKeyCreationEnabled),
            [TighteningToggleKeys.ForeignKeysAllowCrossSchema] = ToggleExportValue.From(ForeignKeyCrossSchemaAllowed),
            [TighteningToggleKeys.ForeignKeysAllowCrossCatalog] = ToggleExportValue.From(ForeignKeyCrossCatalogAllowed),
            [TighteningToggleKeys.UniquenessEnforceSingleColumn] = ToggleExportValue.From(SingleColumnUniqueEnforced),
            [TighteningToggleKeys.UniquenessEnforceMultiColumn] = ToggleExportValue.From(MultiColumnUniqueEnforced),
            [TighteningToggleKeys.RemediationGeneratePreScripts] = ToggleExportValue.From(RemediationGeneratePreScripts),
            [TighteningToggleKeys.RemediationMaxRowsDefaultBackfill] = ToggleExportValue.From(RemediationMaxRowsDefaultBackfill)
        };

        return dictionary;
    }

    private static ToggleState<T> Resolve<T>(
        string key,
        T value,
        T baseline,
        Func<string, ToggleSource?>? sourceResolver,
        Func<T, object?>? converter = null)
    {
        var resolvedSource = sourceResolver?.Invoke(key);
        var source = resolvedSource ?? (EqualityComparer<T>.Default.Equals(value, baseline) ? ToggleSource.Default : ToggleSource.Configuration);
        return new ToggleState<T>(value, source);
    }
}
