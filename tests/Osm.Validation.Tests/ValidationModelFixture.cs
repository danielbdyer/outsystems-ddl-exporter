using System;
using System.Linq;
using Osm.Domain.Model;
using Osm.Json;
using Tests.Support;

namespace Osm.Validation.Tests;

internal static class ValidationModelFixture
{
    private static readonly Lazy<OsmModel> EdgeCase = new(LoadEdgeCase);

    public static OsmModel CreateValidModel()
        => EdgeCase.Value;

    private static OsmModel LoadEdgeCase()
    {
        var deserializer = new ModelJsonDeserializer();
        using var stream = FixtureFile.OpenRead("model.edge-case.json");
        var result = deserializer.Deserialize(stream);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to load edge-case model fixture: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }

        return result.Value;
    }
}
