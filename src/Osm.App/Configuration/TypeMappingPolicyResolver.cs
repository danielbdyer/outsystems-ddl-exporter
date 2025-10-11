using System;
using System.IO;
using Osm.Domain.Abstractions;
using Osm.Smo.TypeMapping;

namespace Osm.App.Configuration;

public static class TypeMappingPolicyResolver
{
    public static Result<TypeMappingPolicy> Resolve(CliConfiguration configuration, string? configPath)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var policy = TypeMappingPolicy.LoadDefault();
        var overridePath = configuration.TypeMapping.Path;
        if (string.IsNullOrWhiteSpace(overridePath))
        {
            return Result<TypeMappingPolicy>.Success(policy);
        }

        var resolvedPath = ResolvePath(overridePath!, configPath);
        if (!File.Exists(resolvedPath))
        {
            return ValidationError.Create(
                "cli.config.typemap.missing",
                $"Type mapping override '{resolvedPath}' was not found.");
        }

        try
        {
            var overridePolicy = TypeMappingPolicy.LoadFromFile(resolvedPath);
            policy = policy.Merge(overridePolicy);
        }
        catch (Exception ex)
        {
            return ValidationError.Create(
                "cli.config.typemap.invalid",
                $"Failed to load type mapping override '{resolvedPath}': {ex.Message}");
        }

        return Result<TypeMappingPolicy>.Success(policy);
    }

    private static string ResolvePath(string path, string? configPath)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            return Path.GetFullPath(path);
        }

        var baseDirectory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(baseDirectory!, path));
    }
}
