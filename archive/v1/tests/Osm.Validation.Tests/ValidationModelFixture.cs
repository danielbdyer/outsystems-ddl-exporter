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

        var model = result.Value;
        if (!model.Modules.Any(m => string.Equals(m.Name.Value, "Users", StringComparison.OrdinalIgnoreCase)))
        {
            var usersEntity = OutSystemsInternalModel.Users;
            var usersModuleResult = ModuleModel.Create(
                usersEntity.Module,
                isSystemModule: true,
                isActive: true,
                new[] { usersEntity });

            if (usersModuleResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Failed to construct Users module: {string.Join(", ", usersModuleResult.Errors.Select(e => e.Code))}");
            }

            model = model with { Modules = model.Modules.Add(usersModuleResult.Value) };
        }

        if (!model.Modules.SelectMany(m => m.Entities).Any(e => string.Equals(e.LogicalName.Value, "User", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Users entity missing from validation fixture.");
        }

        return model;
    }
}
