using System;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Sql;
using Osm.Pipeline.StaticData;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Application;

public interface IStaticDataProviderFactory
{
    Result<IStaticEntityDataProvider?> Create(
        BuildSsdtOverrides overrides,
        ResolvedSqlOptions sqlOptions,
        TighteningOptions tighteningOptions);
}

public sealed class StaticDataProviderFactory : IStaticDataProviderFactory
{
    public Result<IStaticEntityDataProvider?> Create(
        BuildSsdtOverrides overrides,
        ResolvedSqlOptions sqlOptions,
        TighteningOptions tighteningOptions)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        if (tighteningOptions is null)
        {
            throw new ArgumentNullException(nameof(tighteningOptions));
        }

        if (!string.IsNullOrWhiteSpace(overrides.StaticDataPath))
        {
            return Result<IStaticEntityDataProvider?>.Success(new FixtureStaticEntityDataProvider(overrides.StaticDataPath!));
        }

        if (!string.IsNullOrWhiteSpace(sqlOptions.ConnectionString))
        {
            var authentication = sqlOptions.Authentication;
            var connectionOptions = new SqlConnectionOptions(
                authentication.Method,
                authentication.TrustServerCertificate,
                authentication.ApplicationName,
                authentication.AccessToken);

            var factory = new SqlConnectionFactory(sqlOptions.ConnectionString!, connectionOptions);
            return Result<IStaticEntityDataProvider?>.Success(
                new SqlStaticEntityDataProvider(factory, sqlOptions.CommandTimeoutSeconds));
        }

        var requiresStaticData = tighteningOptions.Emission.StaticSeeds.SynchronizationMode
            != StaticSeedSynchronizationMode.NonDestructive;
        if (requiresStaticData)
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.staticData.missingSource",
                "Static seed synchronization requires either --static-data or a SQL connection string.");
        }

        return Result<IStaticEntityDataProvider?>.Success(null);
    }
}
