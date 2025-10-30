using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;

namespace Osm.Json.Configuration;

public interface ITighteningOptionsDeserializer
{
    Result<TighteningOptions> Deserialize(Stream jsonStream);
}

public sealed class TighteningOptionsDeserializer : ITighteningOptionsDeserializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Result<TighteningOptions> Deserialize(Stream jsonStream)
    {
        if (jsonStream is null)
        {
            throw new ArgumentNullException(nameof(jsonStream));
        }

        TighteningOptionsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<TighteningOptionsDocument>(jsonStream, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return Result<TighteningOptions>.Failure(ValidationError.Create("config.parse.failed", $"Invalid configuration JSON: {ex.Message}"));
        }

        if (document is null)
        {
            return Result<TighteningOptions>.Failure(ValidationError.Create("config.document.null", "Configuration document is empty."));
        }

        return Map(document);
    }

    private static Result<TighteningOptions> Map(TighteningOptionsDocument document)
    {
        if (document.Policy is null)
        {
            return ValidationError.Create("config.policy.missing", "Configuration must include 'policy'.");
        }

        if (!Enum.TryParse<TighteningMode>(document.Policy.Mode, ignoreCase: true, out var mode))
        {
            return ValidationError.Create("config.policy.mode.invalid", $"Unrecognized tightening mode '{document.Policy.Mode}'.");
        }

        var policyResult = PolicyOptions.Create(mode, document.Policy.NullBudget);
        if (policyResult.IsFailure)
        {
            return Result<TighteningOptions>.Failure(policyResult.Errors);
        }

        if (document.ForeignKeys is null)
        {
            return ValidationError.Create("config.foreignKeys.missing", "Configuration must include 'foreignKeys'.");
        }

        var foreignKeyResult = ForeignKeyOptions.Create(
            document.ForeignKeys.EnableCreation,
            document.ForeignKeys.AllowCrossSchema,
            document.ForeignKeys.AllowCrossCatalog,
            document.ForeignKeys.TreatMissingDeleteRuleAsIgnore);

        if (foreignKeyResult.IsFailure)
        {
            return Result<TighteningOptions>.Failure(foreignKeyResult.Errors);
        }

        if (document.Uniqueness is null)
        {
            return ValidationError.Create("config.uniqueness.missing", "Configuration must include 'uniqueness'.");
        }

        var uniquenessResult = UniquenessOptions.Create(
            document.Uniqueness.EnforceSingleColumnUnique,
            document.Uniqueness.EnforceMultiColumnUnique);

        if (uniquenessResult.IsFailure)
        {
            return Result<TighteningOptions>.Failure(uniquenessResult.Errors);
        }

        if (document.Remediation is null)
        {
            return ValidationError.Create("config.remediation.missing", "Configuration must include 'remediation'.");
        }

        if (document.Remediation.Sentinels is null)
        {
            return ValidationError.Create("config.remediation.sentinels.missing", "Configuration must include remediation sentinels.");
        }

        var sentinelResult = RemediationSentinelOptions.Create(
            document.Remediation.Sentinels.Numeric,
            document.Remediation.Sentinels.Text,
            document.Remediation.Sentinels.Date);

        if (sentinelResult.IsFailure)
        {
            return Result<TighteningOptions>.Failure(sentinelResult.Errors);
        }

        var remediationResult = RemediationOptions.Create(
            document.Remediation.GeneratePreScripts,
            sentinelResult.Value,
            document.Remediation.MaxRowsDefaultBackfill);

        if (remediationResult.IsFailure)
        {
            return Result<TighteningOptions>.Failure(remediationResult.Errors);
        }

        if (document.Emission is null)
        {
            return ValidationError.Create("config.emission.missing", "Configuration must include 'emission'.");
        }

        NamingOverrideOptions namingOverrides = NamingOverrideOptions.Empty;
        List<NamingOverrideRule>? namingOverrideRules = null;

        if (document.Emission.NamingOverrides is { } namingDocument)
        {
            namingOverrideRules = new List<NamingOverrideRule>();

            if (namingDocument.Rules is { Length: > 0 } ruleDocuments)
            {
                var overrides = Result.Collect(ruleDocuments.Select(o => NamingOverrideRule.Create(o.Schema, o.Table, o.Module, o.Entity, o.Override)));
                if (overrides.IsFailure)
                {
                    return Result<TighteningOptions>.Failure(overrides.Errors);
                }

                namingOverrideRules.AddRange(overrides.Value);
            }

            if (namingDocument.Tables is { Length: > 0 } tableDocuments)
            {
                var overrides = Result.Collect(tableDocuments.Select(o => NamingOverrideRule.Create(o.Schema, o.Table, null, null, o.Override)));
                if (overrides.IsFailure)
                {
                    return Result<TighteningOptions>.Failure(overrides.Errors);
                }

                namingOverrideRules.AddRange(overrides.Value);
            }

            if (namingDocument.Entities is { Length: > 0 } entityDocuments)
            {
                var overrides = Result.Collect(entityDocuments.Select(o => NamingOverrideRule.Create(null, null, o.Module, o.Entity, o.Override)));
                if (overrides.IsFailure)
                {
                    return Result<TighteningOptions>.Failure(overrides.Errors);
                }

                namingOverrideRules.AddRange(overrides.Value);
            }
        }

        if (namingOverrideRules is { Count: > 0 })
        {
            var namingOverrideResult = NamingOverrideOptions.Create(namingOverrideRules);
            if (namingOverrideResult.IsFailure)
            {
                return Result<TighteningOptions>.Failure(namingOverrideResult.Errors);
            }

            namingOverrides = namingOverrideResult.Value;
        }

        StaticSeedOptions staticSeeds = StaticSeedOptions.Default;
        if (document.Emission.StaticSeeds is { } staticSeedDocument)
        {
            var modeText = staticSeedDocument.Mode;
            var modeValue = StaticSeedSynchronizationMode.NonDestructive;
            if (!string.IsNullOrWhiteSpace(modeText) &&
                !Enum.TryParse<StaticSeedSynchronizationMode>(modeText, ignoreCase: true, out modeValue))
            {
                return ValidationError.Create(
                    "config.staticSeeds.mode.invalid",
                    $"Unrecognized static seed synchronization mode '{modeText}'.");
            }

            var staticSeedResult = StaticSeedOptions.Create(
                staticSeedDocument.GroupByModule,
                staticSeedDocument.EmitMasterFile,
                modeValue);
            if (staticSeedResult.IsFailure)
            {
                return Result<TighteningOptions>.Failure(staticSeedResult.Errors);
            }

            staticSeeds = staticSeedResult.Value;
        }

        var emissionResult = EmissionOptions.Create(
            document.Emission.PerTableFiles,
            document.Emission.IncludePlatformAutoIndexes,
            document.Emission.SanitizeModuleNames,
            document.Emission.EmitBareTableOnly,
            document.Emission.EmitTableHeaders,
            document.Emission.ModuleParallelism,
            namingOverrides,
            staticSeeds);

        if (emissionResult.IsFailure)
        {
            return Result<TighteningOptions>.Failure(emissionResult.Errors);
        }

        if (document.Mocking is null)
        {
            return ValidationError.Create("config.mocking.missing", "Configuration must include 'mocking'.");
        }

        var mockingResult = MockingOptions.Create(
            document.Mocking.UseProfileMockFolder,
            document.Mocking.ProfileMockFolder);

        if (mockingResult.IsFailure)
        {
            return Result<TighteningOptions>.Failure(mockingResult.Errors);
        }

        return TighteningOptions.Create(
            policyResult.Value,
            foreignKeyResult.Value,
            uniquenessResult.Value,
            remediationResult.Value,
            emissionResult.Value,
            mockingResult.Value);
    }

    private sealed record TighteningOptionsDocument
    {
        [JsonPropertyName("policy")]
        public PolicyDocument? Policy { get; init; }

        [JsonPropertyName("foreignKeys")]
        public ForeignKeysDocument? ForeignKeys { get; init; }

        [JsonPropertyName("uniqueness")]
        public UniquenessDocument? Uniqueness { get; init; }

        [JsonPropertyName("remediation")]
        public RemediationDocument? Remediation { get; init; }

        [JsonPropertyName("emission")]
        public EmissionDocument? Emission { get; init; }

        [JsonPropertyName("mocking")]
        public MockingDocument? Mocking { get; init; }
    }

    private sealed record PolicyDocument
    {
        [JsonPropertyName("mode")]
        public string? Mode { get; init; }

        [JsonPropertyName("nullBudget")]
        public double NullBudget { get; init; }
    }

    private sealed record ForeignKeysDocument
    {
        [JsonPropertyName("enableCreation")]
        public bool EnableCreation { get; init; }

        [JsonPropertyName("allowCrossSchema")]
        public bool AllowCrossSchema { get; init; }

        [JsonPropertyName("allowCrossCatalog")]
        public bool AllowCrossCatalog { get; init; }

        [JsonPropertyName("treatMissingDeleteRuleAsIgnore")]
        public bool TreatMissingDeleteRuleAsIgnore { get; init; }
    }

    private sealed record UniquenessDocument
    {
        [JsonPropertyName("enforceSingleColumnUnique")]
        public bool EnforceSingleColumnUnique { get; init; }

        [JsonPropertyName("enforceMultiColumnUnique")]
        public bool EnforceMultiColumnUnique { get; init; }
    }

    private sealed record RemediationDocument
    {
        [JsonPropertyName("generatePreScripts")]
        public bool GeneratePreScripts { get; init; }

        [JsonPropertyName("sentinels")]
        public RemediationSentinelsDocument? Sentinels { get; init; }

        [JsonPropertyName("maxRowsDefaultBackfill")]
        public int MaxRowsDefaultBackfill { get; init; }
    }

    private sealed record RemediationSentinelsDocument
    {
        [JsonPropertyName("numeric")]
        public string? Numeric { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("date")]
        public string? Date { get; init; }
    }

    private sealed record EmissionDocument
    {
        [JsonPropertyName("perTableFiles")]
        public bool PerTableFiles { get; init; }

        [JsonPropertyName("includePlatformAutoIndexes")]
        public bool IncludePlatformAutoIndexes { get; init; }

        [JsonPropertyName("sanitizeModuleNames")]
        public bool SanitizeModuleNames { get; init; }

        [JsonPropertyName("emitBareTableOnly")]
        public bool EmitBareTableOnly { get; init; }

        [JsonPropertyName("emitTableHeaders")]
        public bool EmitTableHeaders { get; init; }

        [JsonPropertyName("moduleParallelism")]
        public int ModuleParallelism { get; init; } = 1;

        [JsonPropertyName("namingOverrides")]
        public NamingOverridesDocument? NamingOverrides { get; init; }

        [JsonPropertyName("staticSeeds")]
        public StaticSeedsDocument? StaticSeeds { get; init; }
    }

    private sealed record StaticSeedsDocument
    {
        [JsonPropertyName("groupByModule")]
        public bool GroupByModule { get; init; } = true;

        [JsonPropertyName("emitMasterFile")]
        public bool EmitMasterFile { get; init; }

        [JsonPropertyName("mode")]
        public string? Mode { get; init; }
    }

    private sealed record NamingOverridesDocument
    {
        [JsonPropertyName("rules")]
        public NamingOverrideRuleDocument[]? Rules { get; init; }

        [JsonPropertyName("tables")]
        public TableOverrideDocument[]? Tables { get; init; }

        [JsonPropertyName("entities")]
        public EntityOverrideDocument[]? Entities { get; init; }
    }

    private sealed record NamingOverrideRuleDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("table")]
        public string? Table { get; init; }

        [JsonPropertyName("module")]
        public string? Module { get; init; }

        [JsonPropertyName("entity")]
        public string? Entity { get; init; }

        [JsonPropertyName("override")]
        public string? Override { get; init; }
    }

    private sealed record TableOverrideDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("table")]
        public string? Table { get; init; }

        [JsonPropertyName("override")]
        public string? Override { get; init; }
    }

    private sealed record EntityOverrideDocument
    {
        [JsonPropertyName("module")]
        public string? Module { get; init; }

        [JsonPropertyName("entity")]
        public string? Entity { get; init; }

        [JsonPropertyName("override")]
        public string? Override { get; init; }
    }

    private sealed record MockingDocument
    {
        [JsonPropertyName("useProfileMockFolder")]
        public bool UseProfileMockFolder { get; init; }

        [JsonPropertyName("profileMockFolder")]
        public string? ProfileMockFolder { get; init; }
    }
}
