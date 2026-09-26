using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Emission.Seeds;

namespace Osm.Pipeline.DynamicData;

public enum StaticSeedParentHandlingMode
{
    AutoLoad,
    ValidateStaticSeedApplication
}

public enum StaticSeedParentSatisfaction
{
    AutoLoaded,
    RequiresVerification,
    Verified
}

public sealed record StaticSeedParentReference(string Module, string Entity);

public sealed record StaticSeedParentStatus(
    StaticEntitySeedTableDefinition Definition,
    StaticSeedParentSatisfaction Satisfaction,
    ImmutableArray<StaticSeedParentReference> ReferencedBy)
{
    public static StaticSeedParentStatus Create(
        StaticEntitySeedTableDefinition definition,
        StaticSeedParentSatisfaction satisfaction,
        ImmutableArray<StaticSeedParentReference> referencedBy)
    {
        var normalizedReferences = referencedBy.IsDefault
            ? ImmutableArray<StaticSeedParentReference>.Empty
            : referencedBy;

        return new StaticSeedParentStatus(definition, satisfaction, normalizedReferences);
    }
}

public sealed class StaticSeedParentValidator
{
    public async Task<Result<ImmutableArray<StaticSeedParentStatus>>> ValidateAsync(
        ImmutableArray<StaticSeedParentStatus> parents,
        IStaticEntityDataProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        if (parents.IsDefaultOrEmpty)
        {
            return Result<ImmutableArray<StaticSeedParentStatus>>.Success(
                ImmutableArray<StaticSeedParentStatus>.Empty);
        }

        var pending = parents
            .Where(static status => status.Satisfaction == StaticSeedParentSatisfaction.RequiresVerification)
            .ToImmutableArray();

        if (pending.Length == 0)
        {
            return Result<ImmutableArray<StaticSeedParentStatus>>.Success(parents);
        }

        var definitions = pending
            .Select(static status => status.Definition)
            .ToImmutableArray();

        var verificationResult = await provider
            .GetDataAsync(definitions, cancellationToken)
            .ConfigureAwait(false);

        if (verificationResult.IsFailure)
        {
            return Result<ImmutableArray<StaticSeedParentStatus>>.Failure(verificationResult.Errors);
        }

        var updated = parents
            .Select(static status => status.Satisfaction == StaticSeedParentSatisfaction.RequiresVerification
                ? status with { Satisfaction = StaticSeedParentSatisfaction.Verified }
                : status)
            .ToImmutableArray();

        return Result<ImmutableArray<StaticSeedParentStatus>>.Success(updated);
    }
}
