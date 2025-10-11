using System;
using System.Collections.Generic;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Smo;

namespace Osm.Pipeline.Application;

internal static class NamingOverridesResolver
{
    public static Result<NamingOverrideOptions> Resolve(string? rawOverrides, NamingOverrideOptions existingOverrides)
    {
        if (string.IsNullOrWhiteSpace(rawOverrides))
        {
            return existingOverrides;
        }

        var separators = new[] { ';', ',' };
        var tokens = rawOverrides.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return existingOverrides;
        }

        var parsedOverrides = new List<NamingOverrideRule>();
        foreach (var token in tokens)
        {
            var assignment = token.Split('=', 2, StringSplitOptions.TrimEntries);
            if (assignment.Length != 2)
            {
                return ValidationError.Create("cli.rename.invalidFormat", $"Invalid table rename '{token}'. Expected format source=OverrideName.");
            }

            var source = assignment[0];
            var replacement = assignment[1];

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(replacement))
            {
                return ValidationError.Create("cli.rename.missingValue", "Naming overrides must include both source and replacement values.");
            }

            string? schema = null;
            string? tableName = null;
            string? module = null;
            string? entity = null;

            var segments = source.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                segments = new[] { source };
            }

            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                if (segment.Contains("::", StringComparison.Ordinal))
                {
                    var logicalParts = segment.Split("::", 2, StringSplitOptions.TrimEntries);
                    if (logicalParts.Length != 2 || string.IsNullOrWhiteSpace(logicalParts[1]))
                    {
                        return ValidationError.Create("cli.rename.invalidLogical", $"Invalid logical source '{segment}'. Expected format Module::Entity.");
                    }

                    module = string.IsNullOrWhiteSpace(logicalParts[0]) ? null : logicalParts[0];
                    entity = logicalParts[1];
                }
                else if (segment.Contains('.', StringComparison.Ordinal))
                {
                    var physicalParts = segment.Split('.', 2, StringSplitOptions.TrimEntries);
                    if (physicalParts.Length != 2 || string.IsNullOrWhiteSpace(physicalParts[1]))
                    {
                        return ValidationError.Create("cli.rename.invalidTable", $"Invalid table source '{segment}'. Expected schema.table or table.");
                    }

                    schema = physicalParts[0];
                    tableName = physicalParts[1];
                }
                else
                {
                    tableName = segment;
                }
            }

            var overrideResult = NamingOverrideRule.Create(schema, tableName, module, entity, replacement);
            if (overrideResult.IsFailure)
            {
                return Result<NamingOverrideOptions>.Failure(overrideResult.Errors);
            }

            parsedOverrides.Add(overrideResult.Value);
        }

        return existingOverrides.MergeWith(parsedOverrides);
    }
}
