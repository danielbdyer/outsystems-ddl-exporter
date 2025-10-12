using System;
using System.IO;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;
using Osm.Smo;

namespace Osm.Pipeline.Application;

internal static class TypeMappingPolicyResolver
{
    public static Result<TypeMappingPolicy> Resolve(CliConfigurationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var configuration = context.Configuration.TypeMapping ?? TypeMappingConfiguration.Empty;

        try
        {
            if (!string.IsNullOrWhiteSpace(configuration.Path))
            {
                var path = configuration.Path!;
                if (!File.Exists(path))
                {
                    return ValidationError.Create(
                        "pipeline.typeMapping.pathMissing",
                        $"Type mapping file '{path}' was not found.");
                }

                return TypeMappingPolicy.LoadFromFile(path, configuration.Default, configuration.Overrides);
            }

            return TypeMappingPolicy.LoadDefault(configuration.Default, configuration.Overrides);
        }
        catch (Exception ex)
        {
            return ValidationError.Create(
                "pipeline.typeMapping.loadFailed",
                $"Failed to load type mapping policy: {ex.Message}");
        }
    }
}
