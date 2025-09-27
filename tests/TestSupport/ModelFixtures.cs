using System;
using System.Linq;
using Osm.Domain.Model;
using Osm.Json;

namespace Tests.Support;

public static class ModelFixtures
{
    public static OsmModel LoadModel(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Fixture path must be provided.", nameof(relativePath));
        }

        var deserializer = new ModelJsonDeserializer();
        using var stream = FixtureFile.OpenRead(relativePath);
        var result = deserializer.Deserialize(stream);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to load model fixture '{relativePath}': {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }

        return result.Value;
    }
}
