using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Osm.Domain.Configuration;

namespace Osm.Pipeline.Configuration;

internal sealed class ModuleFilterSectionReader
{
    public ModuleFilterSectionReadResult Read(JsonElement root, string baseDirectory)
    {
        if (!root.TryGetProperty("model", out var element))
        {
            return ModuleFilterSectionReadResult.NotPresent;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            if (ConfigurationJsonHelpers.TryReadPathProperty(root, "model", baseDirectory, out var legacyPath))
            {
                return ModuleFilterSectionReadResult.FromPath(legacyPath);
            }

            return ModuleFilterSectionReadResult.NotPresent;
        }

        string? modelPath = null;
        if (ConfigurationJsonHelpers.TryReadPathProperty(element, "path", baseDirectory, out var resolved))
        {
            modelPath = resolved;
        }

        var modules = new List<string>();
        var moduleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entityFilters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var validationOverrides = new Dictionary<string, ModuleValidationOverrideConfiguration>(StringComparer.OrdinalIgnoreCase);
        bool? includeSystem = null;
        bool? includeInactive = null;

        if (element.TryGetProperty("modules", out var modulesElement))
        {
            if (modulesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var moduleElement in modulesElement.EnumerateArray())
                {
                    if (moduleElement.ValueKind == JsonValueKind.String)
                    {
                        AddModuleName(moduleElement.GetString());
                    }
                    else if (moduleElement.ValueKind == JsonValueKind.Object)
                    {
                        if (!moduleElement.TryGetProperty("name", out var nameElement)
                            || nameElement.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var moduleName = AddModuleName(nameElement.GetString());
                        if (moduleName is null)
                        {
                            continue;
                        }

                        if (moduleElement.TryGetProperty("entities", out var entitiesElement))
                        {
                            var parsedEntities = ParseEntityNames(entitiesElement);
                            if (parsedEntities is null)
                            {
                                entityFilters.Remove(moduleName);
                            }
                            else
                            {
                                entityFilters[moduleName] = parsedEntities.Count > 0
                                    ? parsedEntities.ToArray()
                                    : Array.Empty<string>();
                            }
                        }

                        var moduleOverride = ModuleValidationOverrideConfiguration.Empty;
                        var hasOverride = false;

                        if (moduleElement.TryGetProperty("allowMissingPrimaryKey", out var pkElement))
                        {
                            var primaryKeyEntities = ParseOverrideEntities(pkElement, out var pkAll);
                            moduleOverride = moduleOverride.Merge(new ModuleValidationOverrideConfiguration(
                                primaryKeyEntities.ToArray(),
                                pkAll,
                                Array.Empty<string>(),
                                AllowMissingSchemaForAll: false));
                            hasOverride |= pkAll || primaryKeyEntities.Count > 0;
                        }

                        if (moduleElement.TryGetProperty("allowMissingSchema", out var schemaElement))
                        {
                            var schemaEntities = ParseOverrideEntities(schemaElement, out var schemaAll);
                            moduleOverride = moduleOverride.Merge(new ModuleValidationOverrideConfiguration(
                                Array.Empty<string>(),
                                AllowMissingPrimaryKeyForAll: false,
                                schemaEntities.ToArray(),
                                schemaAll));
                            hasOverride |= schemaAll || schemaEntities.Count > 0;
                        }

                        if (hasOverride)
                        {
                            validationOverrides[moduleName] = moduleOverride;
                        }
                    }
                }
            }
            else if (modulesElement.ValueKind == JsonValueKind.String)
            {
                var raw = modulesElement.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var separators = new[] { ';', ',', '|' };
                    foreach (var token in raw.Split(separators, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = token.Trim();
                        AddModuleName(trimmed);
                    }
                }
            }
        }

        if (element.TryGetProperty("includeSystemModules", out var includeElement)
            && ConfigurationJsonHelpers.TryParseBoolean(includeElement, out var parsedInclude))
        {
            includeSystem = parsedInclude;
        }

        if (element.TryGetProperty("includeInactiveModules", out var inactiveElement)
            && ConfigurationJsonHelpers.TryParseBoolean(inactiveElement, out var parsedInactive))
        {
            includeInactive = parsedInactive;
        }

        var moduleFilter = new ModuleFilterConfiguration(
            modules.Count > 0 ? modules.ToArray() : Array.Empty<string>(),
            includeSystem,
            includeInactive,
            entityFilters,
            validationOverrides);

        return new ModuleFilterSectionReadResult(modelPath, moduleFilter, true);

        string? AddModuleName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return null;
            }

            var trimmed = rawName.Trim();
            if (moduleSet.Add(trimmed))
            {
                modules.Add(trimmed);
            }

            return trimmed;
        }
    }

    private static List<string>? ParseEntityNames(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.Null => null,
            JsonValueKind.False => new List<string>(),
            JsonValueKind.String => ParseEntityString(element.GetString()),
            JsonValueKind.Array => ParseEntityArray(element),
            _ => new List<string>()
        };

        static List<string>? ParseEntityArray(JsonElement arrayElement)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in arrayElement.EnumerateArray())
            {
                if (child.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var parsed = ParseEntityString(child.GetString());
                if (parsed is null)
                {
                    return null;
                }

                foreach (var value in parsed)
                {
                    if (seen.Add(value))
                    {
                        list.Add(value);
                    }
                }
            }

            return list;
        }

        static List<string>? ParseEntityString(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            var separators = new[] { ';', ',', '|' };
            var tokens = raw.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var token in tokens)
            {
                var trimmed = token.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                if (string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (seen.Add(trimmed))
                {
                    list.Add(trimmed);
                }
            }

            return list;
        }
    }

    private static List<string> ParseOverrideEntities(JsonElement element, out bool appliesToAll)
    {
        appliesToAll = false;
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var separators = new[] { ';', ',', '|' };

        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                appliesToAll = true;
                break;
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;
            case JsonValueKind.String:
                ParseOverrideTokens(element.GetString(), seen, list, ref appliesToAll, separators);
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (child.ValueKind == JsonValueKind.String)
                    {
                        ParseOverrideTokens(child.GetString(), seen, list, ref appliesToAll, separators);
                    }
                    else if (child.ValueKind == JsonValueKind.True)
                    {
                        appliesToAll = true;
                    }
                }

                break;
            default:
                break;
        }

        if (appliesToAll)
        {
            list.Clear();
        }

        return list;
    }

    private static void ParseOverrideTokens(
        string? raw,
        HashSet<string> seen,
        List<string> values,
        ref bool appliesToAll,
        char[] separators)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var tokens = raw.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var trimmed = token.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase))
            {
                appliesToAll = true;
                continue;
            }

            if (seen.Add(trimmed))
            {
                values.Add(trimmed);
            }
        }
    }
}

internal readonly record struct ModuleFilterSectionReadResult(string? ModelPath, ModuleFilterConfiguration ModuleFilter, bool HasValue)
{
    public static ModuleFilterSectionReadResult NotPresent { get; } = new(null, ModuleFilterConfiguration.Empty, false);

    public static ModuleFilterSectionReadResult FromPath(string modelPath)
        => new(modelPath, ModuleFilterConfiguration.Empty, true);
}
