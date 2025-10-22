using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Smo.Tests;

internal static class SmoTestHelper
{
    public static (OsmModel Model, PolicyDecisionSet Decisions, ProfileSnapshot Profile) LoadEdgeCaseArtifacts()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, TighteningOptions.Default);
        return (model, decisions, snapshot);
    }

    public static (OsmModel Model, PolicyDecisionSet Decisions, ProfileSnapshot Profile) LoadCompositeForeignKeyArtifacts()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-composite.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroFkComposite);
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, TighteningOptions.Default);
        return (model, decisions, snapshot);
    }

    public static IReadOnlyDictionary<ColumnCoordinate, string> BuildProfileDefaults(ProfileSnapshot profile)
    {
        if (profile is null || profile.Columns.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<ColumnCoordinate, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, string>();
        foreach (var column in profile.Columns)
        {
            var normalized = SmoNormalization.NormalizeSqlExpression(column.DefaultDefinition);
            if (normalized is null)
            {
                continue;
            }

            builder[ColumnCoordinate.From(column)] = normalized;
        }

        return builder.ToImmutable();
    }

    public static IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> BuildForeignKeyReality(ProfileSnapshot profile)
    {
        if (profile is null || profile.ForeignKeys.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<ColumnCoordinate, ForeignKeyReality>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, ForeignKeyReality>();
        foreach (var foreignKey in profile.ForeignKeys)
        {
            builder[ColumnCoordinate.From(foreignKey.Reference)] = foreignKey;
        }

        return builder.ToImmutable();
    }

    public static bool TryEnsureSqlServer(out string skipReason)
    {
        try
        {
            using var context = new SmoContext();
            _ = context.Server.Version;
            skipReason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsSqlConnectionException(ex))
        {
            skipReason = $"SQL Server is not available for SMO tests: {ex.Message}";
            return false;
        }
    }

    private static bool IsSqlConnectionException(Exception ex)
    {
        return ex switch
        {
            ConnectionFailureException => true,
            FailedOperationException { InnerException: { } inner } => IsSqlConnectionException(inner),
            SqlException => true,
            _ when ex.InnerException is not null => IsSqlConnectionException(ex.InnerException),
            _ => false
        };
    }
}
