using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Osm.Smo;

namespace Osm.Dmm;

public sealed record SmoDmmLensRequest(SmoModel Model, SmoBuildOptions Options);

public sealed class SmoDmmLens : IDmmLens<SmoDmmLensRequest>
{
    private readonly PerTableWriter _perTableWriter;
    private readonly ScriptDomDmmLens _scriptLens;

    public SmoDmmLens()
        : this(new PerTableWriter(), new ScriptDomDmmLens())
    {
    }

    public SmoDmmLens(PerTableWriter perTableWriter, ScriptDomDmmLens scriptLens)
    {
        _perTableWriter = perTableWriter ?? throw new ArgumentNullException(nameof(perTableWriter));
        _scriptLens = scriptLens ?? throw new ArgumentNullException(nameof(scriptLens));
    }

    public Result<IReadOnlyList<DmmTable>> Project(SmoDmmLensRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Model is null)
        {
            throw new ArgumentNullException(nameof(request.Model));
        }

        var options = request.Options ?? SmoBuildOptions.Default;
        var namingMetadata = request.Model.Tables.ToDictionary(
            table => table.ToCoordinate(),
            table => new TableNamingMetadata(table.OriginalModule, table.LogicalName),
            TableCoordinate.OrdinalIgnoreCaseComparer);
        var physicalOverrides = CreatePhysicalNamingOverrides(request.Model);
        if (physicalOverrides.Count > 0)
        {
            if (options.NamingOverrides.IsEmpty)
            {
                var overrideOptions = NamingOverrideOptions.Create(physicalOverrides);
                if (overrideOptions.IsSuccess)
                {
                    options = options.WithNamingOverrides(overrideOptions.Value);
                }
            }
            else
            {
                var missingPhysicalOverrides = physicalOverrides
                    .Where(rule => rule.Schema is not null && rule.PhysicalName is not null)
                    .Where(rule => !options.NamingOverrides.TryGetTableOverride(
                        rule.Schema!.Value.Value,
                        rule.PhysicalName!.Value.Value,
                        out _))
                    .Where(rule =>
                    {
                        var coordinateResult = TableCoordinate.Create(
                            rule.Schema!.Value.Value,
                            rule.PhysicalName!.Value.Value);

                        if (coordinateResult.IsFailure ||
                            !namingMetadata.TryGetValue(coordinateResult.Value, out var metadata))
                        {
                            return true;
                        }

                        if (string.IsNullOrWhiteSpace(metadata.LogicalName))
                        {
                            return true;
                        }

                        return !options.NamingOverrides.TryGetEntityOverride(
                            metadata.Module,
                            metadata.LogicalName,
                            out _);
                    })
                    .ToArray();

                if (missingPhysicalOverrides.Length > 0)
                {
                    options = options.WithNamingOverrides(options.NamingOverrides.MergeWith(missingPhysicalOverrides));
                }
            }
        }
        var builder = new StringBuilder();

        foreach (var table in request.Model.Tables)
        {
            var writeResult = _perTableWriter.Generate(table, options);
            foreach (var statement in SplitStatements(writeResult.Script))
            {
                var sanitized = NormalizeStatement(statement.Trim());
                if (string.IsNullOrWhiteSpace(sanitized) || IsTriggerStatement(sanitized))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.AppendLine(sanitized);
            }
        }

        if (builder.Length == 0)
        {
            return Result<IReadOnlyList<DmmTable>>.Success(Array.Empty<DmmTable>());
        }

        using var reader = new StringReader(builder.ToString());
        var parsedResult = _scriptLens.Project(reader);
        if (parsedResult.IsFailure)
        {
            return parsedResult;
        }

        return Result<IReadOnlyList<DmmTable>>.Success(AlignTableOrdering(request.Model, options, parsedResult.Value));
    }

    private static IEnumerable<string> SplitStatements(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            yield break;
        }

        var builder = new StringBuilder(script.Length);
        using var reader = new StringReader(script);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                continue;
            }

            builder.AppendLine(line);
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static bool IsTriggerStatement(string statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return false;
        }

        if (statement.StartsWith("--", StringComparison.Ordinal))
        {
            // Commentary preceding the trigger definition is emitted alongside the CREATE TRIGGER batch.
            return statement.IndexOf("trigger", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return statement.IndexOf("create trigger", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeStatement(string statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return string.Empty;
        }

        return Regex.Replace(
            statement,
            @"COLLATE\s+\[(?<name>[^\]]+)\]",
            static match => $"COLLATE {match.Groups["name"].Value}",
            RegexOptions.IgnoreCase);
    }

    private static IReadOnlyList<NamingOverrideRule> CreatePhysicalNamingOverrides(SmoModel model)
    {
        if (model is null || model.Tables.Length == 0)
        {
            return Array.Empty<NamingOverrideRule>();
        }

        var overrides = new List<NamingOverrideRule>(model.Tables.Length);
        foreach (var table in model.Tables)
        {
            var ruleResult = NamingOverrideRule.Create(table.Schema, table.Name, module: null, logicalName: null, target: table.Name);
            if (ruleResult.IsSuccess)
            {
                overrides.Add(ruleResult.Value);
            }
        }

        return overrides;
    }

    private static IReadOnlyList<DmmTable> AlignTableOrdering(SmoModel model, SmoBuildOptions options, IReadOnlyList<DmmTable> tables)
    {
        if (model.Tables.Length == 0 || tables.Count == 0)
        {
            return tables;
        }

        var metadata = new Dictionary<TableCoordinate, TableMetadata>(
            model.Tables.Length,
            TableCoordinate.OrdinalIgnoreCaseComparer);
        foreach (var table in model.Tables)
        {
            var key = table.ToCoordinate();
            if (metadata.ContainsKey(key))
            {
                continue;
            }

            var columnOrder = new Dictionary<string, int>(table.Columns.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < table.Columns.Length; i++)
            {
                var column = table.Columns[i];
                if (!string.IsNullOrWhiteSpace(column.Name))
                {
                    columnOrder[column.Name] = i;
                }
            }

            var primaryKey = table.Indexes.FirstOrDefault(index => index.IsPrimaryKey);
            var primaryKeyColumns = primaryKey is null
                ? Array.Empty<string>()
                : primaryKey.Columns
                    .OrderBy(column => column.Ordinal)
                    .Select(column => column.Name)
                    .ToArray();

            var tableMetadata = new TableMetadata(columnOrder, primaryKeyColumns);
            metadata[key] = tableMetadata;

            var effectiveName = options.NamingOverrides.GetEffectiveTableName(
                table.Schema,
                table.Name,
                table.LogicalName,
                table.OriginalModule);
            var effectiveCoordinateResult = TableCoordinate.Create(table.Schema, effectiveName);
            if (effectiveCoordinateResult.IsSuccess && !metadata.ContainsKey(effectiveCoordinateResult.Value))
            {
                metadata[effectiveCoordinateResult.Value] = tableMetadata;
            }
        }

        var adjusted = new List<DmmTable>(tables.Count);
        foreach (var table in tables)
        {
            var coordinateResult = TableCoordinate.Create(table.Schema, table.Name);
            if (coordinateResult.IsFailure ||
                !metadata.TryGetValue(coordinateResult.Value, out var tableMetadata))
            {
                adjusted.Add(table);
                continue;
            }

            var orderedColumns = table.Columns;
            if (tableMetadata.ColumnOrder.Count > 0 && table.Columns.Count > 1)
            {
                orderedColumns = table.Columns
                    .OrderBy(column => tableMetadata.ColumnOrder.TryGetValue(column.Name, out var index) ? index : int.MaxValue)
                    .ThenBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            var primaryKeyColumns = tableMetadata.PrimaryKeyColumns.Count > 0
                ? tableMetadata.PrimaryKeyColumns.ToArray()
                : table.PrimaryKeyColumns.ToArray();

            adjusted.Add(new DmmTable(
                table.Schema,
                table.Name,
                orderedColumns,
                primaryKeyColumns,
                table.Indexes,
                table.ForeignKeys,
                table.Description));
        }

        return adjusted;
    }

    private sealed record TableMetadata(
        IReadOnlyDictionary<string, int> ColumnOrder,
        IReadOnlyList<string> PrimaryKeyColumns);

    private sealed record TableNamingMetadata(string Module, string LogicalName);
}
