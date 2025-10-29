using System.Collections.Generic;
using System.Collections.Immutable;
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

    public static (OsmModel Model, PolicyDecisionSet Decisions, ProfileSnapshot Profile) LoadDefaultDeleteRuleArtifacts()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-default-delete-rule.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroFkDefaultDeleteRule);
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
}
