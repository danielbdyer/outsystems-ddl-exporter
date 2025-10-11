using System;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Configuration;

public enum TighteningMode
{
    Cautious,
    EvidenceGated,
    Aggressive
}

public sealed record TighteningOptions
{
    private TighteningOptions(
        PolicyOptions policy,
        ForeignKeyOptions foreignKeys,
        UniquenessOptions uniqueness,
        RemediationOptions remediation,
        EmissionOptions emission,
        MockingOptions mocking)
    {
        Policy = policy;
        ForeignKeys = foreignKeys;
        Uniqueness = uniqueness;
        Remediation = remediation;
        Emission = emission;
        Mocking = mocking;
    }

    public PolicyOptions Policy { get; }

    public ForeignKeyOptions ForeignKeys { get; }

    public UniquenessOptions Uniqueness { get; }

    public RemediationOptions Remediation { get; }

    public EmissionOptions Emission { get; }

    public MockingOptions Mocking { get; }

    public static TighteningOptions Default { get; } = Create(
        PolicyOptions.Create(TighteningMode.EvidenceGated, 0.0).Value,
        ForeignKeyOptions.Create(enableCreation: true, allowCrossSchema: false, allowCrossCatalog: false).Value,
        UniquenessOptions.Create(enforceSingleColumnUnique: true, enforceMultiColumnUnique: true).Value,
        RemediationOptions.Create(
            generatePreScripts: true,
            RemediationSentinelOptions.Create(numeric: "0", text: string.Empty, date: "1900-01-01").Value,
            maxRowsDefaultBackfill: 100_000).Value,
        EmissionOptions.Create(
            perTableFiles: true,
            includePlatformAutoIndexes: false,
            sanitizeModuleNames: true,
            emitBareTableOnly: false,
            emitTableHeaders: false,
            moduleParallelism: 1,
            staticSeeds: StaticSeedOptions.Default).Value,
        MockingOptions.Create(useProfileMockFolder: false, profileMockFolder: null).Value).Value;

    public static Result<TighteningOptions> Create(
        PolicyOptions policy,
        ForeignKeyOptions foreignKeys,
        UniquenessOptions uniqueness,
        RemediationOptions remediation,
        EmissionOptions emission,
        MockingOptions mocking)
    {
        if (policy is null)
        {
            return ValidationError.Create("options.policy.null", "Policy options must be provided.");
        }

        if (foreignKeys is null)
        {
            return ValidationError.Create("options.foreignKeys.null", "Foreign key options must be provided.");
        }

        if (uniqueness is null)
        {
            return ValidationError.Create("options.uniqueness.null", "Uniqueness options must be provided.");
        }

        if (remediation is null)
        {
            return ValidationError.Create("options.remediation.null", "Remediation options must be provided.");
        }

        if (emission is null)
        {
            return ValidationError.Create("options.emission.null", "Emission options must be provided.");
        }

        if (mocking is null)
        {
            return ValidationError.Create("options.mocking.null", "Mocking options must be provided.");
        }

        return new TighteningOptions(policy, foreignKeys, uniqueness, remediation, emission, mocking);
    }
}

public sealed record PolicyOptions
{
    private PolicyOptions(TighteningMode mode, double nullBudget)
    {
        Mode = mode;
        NullBudget = nullBudget;
    }

    public TighteningMode Mode { get; }

    public double NullBudget { get; }

    public static Result<PolicyOptions> Create(TighteningMode mode, double nullBudget)
    {
        if (double.IsNaN(nullBudget) || double.IsInfinity(nullBudget))
        {
            return ValidationError.Create("options.policy.nullBudget.nan", "Null budget must be a finite number between 0 and 1 inclusive.");
        }

        if (nullBudget < 0 || nullBudget > 1)
        {
            return ValidationError.Create("options.policy.nullBudget.outOfRange", "Null budget must be between 0 and 1 inclusive.");
        }

        return new PolicyOptions(mode, nullBudget);
    }
}

public sealed record ForeignKeyOptions
{
    private ForeignKeyOptions(bool enableCreation, bool allowCrossSchema, bool allowCrossCatalog)
    {
        EnableCreation = enableCreation;
        AllowCrossSchema = allowCrossSchema;
        AllowCrossCatalog = allowCrossCatalog;
    }

    public bool EnableCreation { get; }

    public bool AllowCrossSchema { get; }

    public bool AllowCrossCatalog { get; }

    public static Result<ForeignKeyOptions> Create(bool enableCreation, bool allowCrossSchema, bool allowCrossCatalog)
    {
        return new ForeignKeyOptions(enableCreation, allowCrossSchema, allowCrossCatalog);
    }
}

public sealed record UniquenessOptions
{
    private UniquenessOptions(bool enforceSingleColumnUnique, bool enforceMultiColumnUnique)
    {
        EnforceSingleColumnUnique = enforceSingleColumnUnique;
        EnforceMultiColumnUnique = enforceMultiColumnUnique;
    }

    public bool EnforceSingleColumnUnique { get; }

    public bool EnforceMultiColumnUnique { get; }

    public static Result<UniquenessOptions> Create(bool enforceSingleColumnUnique, bool enforceMultiColumnUnique)
    {
        return new UniquenessOptions(enforceSingleColumnUnique, enforceMultiColumnUnique);
    }
}

public sealed record RemediationOptions
{
    private RemediationOptions(bool generatePreScripts, RemediationSentinelOptions sentinels, int maxRowsDefaultBackfill)
    {
        GeneratePreScripts = generatePreScripts;
        Sentinels = sentinels;
        MaxRowsDefaultBackfill = maxRowsDefaultBackfill;
    }

    public bool GeneratePreScripts { get; }

    public RemediationSentinelOptions Sentinels { get; }

    public int MaxRowsDefaultBackfill { get; }

    public static Result<RemediationOptions> Create(bool generatePreScripts, RemediationSentinelOptions sentinels, int maxRowsDefaultBackfill)
    {
        if (sentinels is null)
        {
            return ValidationError.Create("options.remediation.sentinels.null", "Remediation sentinels must be provided.");
        }

        if (maxRowsDefaultBackfill < 0)
        {
            return ValidationError.Create("options.remediation.maxRows.invalid", "Max rows for default backfill must be zero or greater.");
        }

        return new RemediationOptions(generatePreScripts, sentinels, maxRowsDefaultBackfill);
    }
}

public sealed record RemediationSentinelOptions
{
    private RemediationSentinelOptions(string numeric, string text, string date)
    {
        Numeric = numeric;
        Text = text;
        Date = date;
    }

    public string Numeric { get; }

    public string Text { get; }

    public string Date { get; }

    public static Result<RemediationSentinelOptions> Create(string? numeric, string? text, string? date)
    {
        if (numeric is null)
        {
            return ValidationError.Create("options.remediation.sentinels.numeric.null", "Numeric sentinel must be provided.");
        }

        if (text is null)
        {
            return ValidationError.Create("options.remediation.sentinels.text.null", "Text sentinel must be provided.");
        }

        if (date is null)
        {
            return ValidationError.Create("options.remediation.sentinels.date.null", "Date sentinel must be provided.");
        }

        return new RemediationSentinelOptions(numeric, text, date);
    }
}

public sealed record EmissionOptions
{
    private EmissionOptions(
        bool perTableFiles,
        bool includePlatformAutoIndexes,
        bool sanitizeModuleNames,
        bool emitBareTableOnly,
        bool emitTableHeaders,
        int moduleParallelism,
        NamingOverrideOptions namingOverrides,
        StaticSeedOptions staticSeeds)
    {
        PerTableFiles = perTableFiles;
        IncludePlatformAutoIndexes = includePlatformAutoIndexes;
        SanitizeModuleNames = sanitizeModuleNames;
        EmitBareTableOnly = emitBareTableOnly;
        EmitTableHeaders = emitTableHeaders;
        ModuleParallelism = moduleParallelism;
        NamingOverrides = namingOverrides;
        StaticSeeds = staticSeeds;
    }

    public bool PerTableFiles { get; }

    public bool IncludePlatformAutoIndexes { get; }

    public bool SanitizeModuleNames { get; }

    public bool EmitBareTableOnly { get; }

    public bool EmitTableHeaders { get; }

    public int ModuleParallelism { get; }

    public NamingOverrideOptions NamingOverrides { get; }

    public StaticSeedOptions StaticSeeds { get; }

    public static Result<EmissionOptions> Create(
        bool perTableFiles,
        bool includePlatformAutoIndexes,
        bool sanitizeModuleNames,
        bool emitBareTableOnly,
        bool emitTableHeaders,
        int moduleParallelism,
        NamingOverrideOptions? namingOverrides = null,
        StaticSeedOptions? staticSeeds = null)
    {
        if (moduleParallelism < 1)
        {
            return ValidationError.Create(
                "options.emission.parallelism.invalid",
                "Module parallelism must be at least 1 for deterministic emission.");
        }

        return new EmissionOptions(
            perTableFiles,
            includePlatformAutoIndexes,
            sanitizeModuleNames,
            emitBareTableOnly,
            emitTableHeaders,
            moduleParallelism,
            namingOverrides ?? NamingOverrideOptions.Empty,
            staticSeeds ?? StaticSeedOptions.Default);
    }
}

public enum StaticSeedSynchronizationMode
{
    NonDestructive,
    Authoritative
}

public sealed record StaticSeedOptions
{
    private StaticSeedOptions(bool groupByModule, StaticSeedSynchronizationMode synchronizationMode)
    {
        GroupByModule = groupByModule;
        SynchronizationMode = synchronizationMode;
    }

    public bool GroupByModule { get; }

    public StaticSeedSynchronizationMode SynchronizationMode { get; }

    public static StaticSeedOptions Default { get; } = Create(groupByModule: true, StaticSeedSynchronizationMode.NonDestructive).Value;

    public static Result<StaticSeedOptions> Create(bool groupByModule, StaticSeedSynchronizationMode synchronizationMode)
    {
        if (!Enum.IsDefined(typeof(StaticSeedSynchronizationMode), synchronizationMode))
        {
            return ValidationError.Create(
                "options.staticSeeds.mode.invalid",
                $"Unrecognized static seed synchronization mode '{synchronizationMode}'.");
        }

        return new StaticSeedOptions(groupByModule, synchronizationMode);
    }
}

public sealed record MockingOptions
{
    private MockingOptions(bool useProfileMockFolder, string? profileMockFolder)
    {
        UseProfileMockFolder = useProfileMockFolder;
        ProfileMockFolder = profileMockFolder;
    }

    public bool UseProfileMockFolder { get; }

    public string? ProfileMockFolder { get; }

    public static Result<MockingOptions> Create(bool useProfileMockFolder, string? profileMockFolder)
    {
        if (useProfileMockFolder && string.IsNullOrWhiteSpace(profileMockFolder))
        {
            return ValidationError.Create("options.mocking.folder.required", "Profile mock folder must be supplied when mocking is enabled.");
        }

        return new MockingOptions(useProfileMockFolder, profileMockFolder);
    }
}
