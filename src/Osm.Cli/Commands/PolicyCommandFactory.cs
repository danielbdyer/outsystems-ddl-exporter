using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Cli.Commands;

internal sealed class PolicyCommandFactory : ICommandFactory
{
    private readonly Option<string?> _pathOption = new("--path", "Path to a policy-decision-report.json or policy-decisions.json file.");
    private readonly Option<string> _searchRootOption = new("--out", () => "out", "Pipeline output directory to search for policy decision reports.");
    private readonly Option<string[]> _moduleOption = CreateMultiValueOption("--module", "Filter results to modules with matching names.");
    private readonly Option<string[]> _schemaOption = CreateMultiValueOption("--schema", "Filter decisions to specific schemas.");
    private readonly Option<string[]> _rationaleOption = CreateMultiValueOption("--rationale", "Filter decisions to those containing any of the supplied rationales.");
    private readonly Option<string[]> _severityOption = CreateMultiValueOption("--severity", "Filter diagnostics by severity (Info, Warning).");
    private readonly Option<string> _formatOption = new("--format", () => "table", "Output format (table or json).");

    public Command Create()
    {
        var explain = new Command("explain", "Explain tightening policy decisions from recent pipeline runs.")
        {
            _pathOption,
            _searchRootOption,
            _moduleOption,
            _schemaOption,
            _rationaleOption,
            _severityOption,
            _formatOption
        };

        explain.SetHandler(async context => await ExecuteExplainAsync(context).ConfigureAwait(false));

        var policy = new Command("policy", "Inspect tightening policy artifacts.")
        {
            explain
        };

        return policy;
    }

    private static Option<string[]> CreateMultiValueOption(string alias, string description)
    {
        var option = new Option<string[]>(alias, description)
        {
            AllowMultipleArgumentsPerToken = true
        };

        return option;
    }

    private async Task ExecuteExplainAsync(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var parseResult = context.ParseResult;
        var console = context.Console;
        var cancellationToken = context.GetCancellationToken();

        var explicitPath = parseResult.GetValueForOption(_pathOption);
        string? resolvedPath = null;

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            resolvedPath = ResolveExplicitPath(explicitPath!);
            if (resolvedPath is null)
            {
                CommandConsole.WriteErrorLine(console, $"[error] File or directory '{explicitPath}' does not exist.");
                context.ExitCode = 1;
                return;
            }
        }
        else
        {
            var root = parseResult.GetValueForOption(_searchRootOption);
            resolvedPath = FindLatestReport(root);
            if (resolvedPath is null)
            {
                CommandConsole.WriteErrorLine(console, $"[error] No policy decision reports were found under '{root}'.");
                context.ExitCode = 1;
                return;
            }
        }

        var report = await LoadReportAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            CommandConsole.WriteErrorLine(console, $"[error] Failed to load policy decision report from '{resolvedPath}'.");
            context.ExitCode = 1;
            return;
        }

        var moduleFilter = BuildFilterSet(parseResult.GetValueForOption(_moduleOption));
        var schemaFilter = BuildFilterSet(parseResult.GetValueForOption(_schemaOption));
        var rationaleFilter = BuildFilterSet(parseResult.GetValueForOption(_rationaleOption));
        var severityFilterResult = BuildSeverityFilter(parseResult.GetValueForOption(_severityOption));
        if (!severityFilterResult.IsSuccess)
        {
            CommandConsole.WriteErrorLine(console, severityFilterResult.Error!);
            context.ExitCode = 1;
            return;
        }

        var severityFilter = severityFilterResult.Value;
        var format = parseResult.GetValueForOption(_formatOption) ?? "table";

        var filteredReport = FilterReport(report, moduleFilter, schemaFilter, rationaleFilter, severityFilter);

        var htmlReportPath = ResolveHtmlReportPath(resolvedPath);
        var linkPrefix = htmlReportPath is null
            ? null
            : Path.GetRelativePath(Directory.GetCurrentDirectory(), htmlReportPath);

        var summaryLines = ShouldIncludeSummary(moduleFilter, schemaFilter, rationaleFilter, severityFilter)
            ? PolicyDecisionSummaryFormatter.FormatForConsole(filteredReport)
            : ImmutableArray<string>.Empty;
        var moduleRows = BuildModuleRows(filteredReport, moduleFilter);
        var decisionRows = BuildDecisionRows(filteredReport, moduleFilter, schemaFilter, rationaleFilter, linkPrefix);
        var diagnosticRows = BuildDiagnosticRows(filteredReport.Diagnostics, moduleFilter, schemaFilter, severityFilter, linkPrefix);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            EmitJson(console, resolvedPath, htmlReportPath, moduleFilter, schemaFilter, rationaleFilter, severityFilter, summaryLines, moduleRows, decisionRows, diagnosticRows);
            context.ExitCode = 0;
            return;
        }

        EmitTableOutput(console, resolvedPath, htmlReportPath, summaryLines, moduleRows, decisionRows, diagnosticRows);
        context.ExitCode = 0;
    }

    private static void EmitTableOutput(
        IConsole console,
        string reportPath,
        string? htmlReportPath,
        ImmutableArray<string> summaryLines,
        IReadOnlyList<IReadOnlyList<string>> moduleRows,
        IReadOnlyList<IReadOnlyList<string>> decisionRows,
        IReadOnlyList<IReadOnlyList<string>> diagnosticRows)
    {
        CommandConsole.WriteLine(console, $"Policy decision report: {reportPath}");
        if (!string.IsNullOrWhiteSpace(htmlReportPath))
        {
            CommandConsole.WriteLine(console, $"HTML report: {htmlReportPath}");
        }

        if (!summaryLines.IsDefaultOrEmpty)
        {
            CommandConsole.WriteLine(console, "Summary:");
            foreach (var line in summaryLines)
            {
                CommandConsole.WriteLine(console, $"  {line}");
            }
        }

        if (moduleRows.Count > 0)
        {
            CommandConsole.WriteLine(console, "\nModule rollups:");
            CommandConsole.WriteTable(
                console,
                new[] { "Module", "Columns", "Tightened", "Remediation", "Unique Enforced", "Unique Remediation", "FK Created" },
                moduleRows);
        }
        else
        {
            CommandConsole.WriteLine(console, "\nModule rollups: (no matching modules)");
        }

        if (decisionRows.Count > 0)
        {
            CommandConsole.WriteLine(console, "\nDecisions:");
            CommandConsole.WriteTable(
                console,
                new[] { "Type", "Module", "Schema", "Table", "Object", "Action", "Remediation", "Rationales", "Link" },
                decisionRows);
        }
        else
        {
            CommandConsole.WriteLine(console, "\nDecisions: (no matching decisions)");
        }

        if (diagnosticRows.Count > 0)
        {
            CommandConsole.WriteLine(console, "\nDiagnostics:");
            CommandConsole.WriteTable(
                console,
                new[] { "Code", "Module", "Schema", "Object", "Severity", "Message", "Link" },
                diagnosticRows);
        }
        else
        {
            CommandConsole.WriteLine(console, "\nDiagnostics: (no matching diagnostics)");
        }
    }

    private static void EmitJson(
        IConsole console,
        string reportPath,
        string? htmlReportPath,
        IReadOnlyCollection<string> moduleFilter,
        IReadOnlyCollection<string> schemaFilter,
        IReadOnlyCollection<string> rationaleFilter,
        IReadOnlyCollection<TighteningDiagnosticSeverity> severityFilter,
        ImmutableArray<string> summaryLines,
        IReadOnlyList<IReadOnlyList<string>> moduleRows,
        IReadOnlyList<IReadOnlyList<string>> decisionRows,
        IReadOnlyList<IReadOnlyList<string>> diagnosticRows)
    {
        var payload = new
        {
            reportPath,
            htmlReport = htmlReportPath,
            filters = new
            {
                modules = moduleFilter,
                schemas = schemaFilter,
                rationales = rationaleFilter,
                severities = severityFilter.Select(severity => severity.ToString()).ToArray()
            },
            summary = summaryLines,
            modules = moduleRows.Select(row => new
            {
                module = row[0],
                columns = row[1],
                tightened = row[2],
                remediation = row[3],
                uniqueEnforced = row[4],
                uniqueRemediation = row[5],
                foreignKeysCreated = row[6]
            }).ToArray(),
            decisions = decisionRows.Select(row => new
            {
                type = row[0],
                module = row[1],
                schema = row[2],
                table = row[3],
                @object = row[4],
                action = row[5],
                remediation = row[6],
                rationales = row[7],
                link = row[8]
            }).ToArray(),
            diagnostics = diagnosticRows.Select(row => new
            {
                code = row[0],
                module = row[1],
                schema = row[2],
                @object = row[3],
                severity = row[4],
                message = row[5],
                link = row[6]
            }).ToArray()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(payload, options);
        CommandConsole.WriteLine(console, json);
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildModuleRows(PolicyDecisionReport report, HashSet<string> moduleFilter)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var pair in report.ModuleRollups.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (moduleFilter.Count > 0 && !moduleFilter.Contains(pair.Key))
            {
                continue;
            }

            var value = pair.Value;
            rows.Add(new[]
            {
                pair.Key,
                value.ColumnCount.ToString("N0", CultureInfo.InvariantCulture),
                value.TightenedColumnCount.ToString("N0", CultureInfo.InvariantCulture),
                value.RemediationColumnCount.ToString("N0", CultureInfo.InvariantCulture),
                value.UniqueIndexesEnforcedCount.ToString("N0", CultureInfo.InvariantCulture),
                value.UniqueIndexesRequireRemediationCount.ToString("N0", CultureInfo.InvariantCulture),
                value.ForeignKeysCreatedCount.ToString("N0", CultureInfo.InvariantCulture)
            });
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildDecisionRows(
        PolicyDecisionReport report,
        HashSet<string> moduleFilter,
        HashSet<string> schemaFilter,
        HashSet<string> rationaleFilter,
        string? linkPrefix)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (var column in report.Columns)
        {
            var module = ResolveModule(report.ColumnModules, column.Column.ToString());
            if (!MatchesModule(moduleFilter, module) || !MatchesSchema(schemaFilter, column.Column.Schema.Value) || !MatchesRationale(rationaleFilter, column.Rationales))
            {
                continue;
            }

            var anchor = PolicyDecisionLinkBuilder.BuildColumnAnchor(
                column.Column.Schema.Value,
                column.Column.Table.Value,
                column.Column.Column.Value);
            var link = ComposeLink(linkPrefix, anchor);

            rows.Add(new[]
            {
                "Column",
                module,
                column.Column.Schema.Value,
                column.Column.Table.Value,
                column.Column.Column.Value,
                column.MakeNotNull ? "NOT NULL" : "Nullable",
                column.RequiresRemediation ? "Yes" : "No",
                FormatRationales(column.Rationales),
                link
            });
        }

        foreach (var unique in report.UniqueIndexes)
        {
            var module = ResolveModule(report.IndexModules, unique.Index.ToString());
            if (!MatchesModule(moduleFilter, module) || !MatchesSchema(schemaFilter, unique.Index.Schema.Value) || !MatchesRationale(rationaleFilter, unique.Rationales))
            {
                continue;
            }

            var anchor = PolicyDecisionLinkBuilder.BuildUniqueIndexAnchor(
                unique.Index.Schema.Value,
                unique.Index.Table.Value,
                unique.Index.Index.Value);
            var link = ComposeLink(linkPrefix, anchor);

            rows.Add(new[]
            {
                "Unique Index",
                module,
                unique.Index.Schema.Value,
                unique.Index.Table.Value,
                unique.Index.Index.Value,
                unique.EnforceUnique ? "Enforce UNIQUE" : "Skip",
                unique.RequiresRemediation ? "Yes" : "No",
                FormatRationales(unique.Rationales),
                link
            });
        }

        foreach (var foreignKey in report.ForeignKeys)
        {
            var module = ResolveModule(report.ColumnModules, foreignKey.Column.ToString());
            if (!MatchesModule(moduleFilter, module) || !MatchesSchema(schemaFilter, foreignKey.Column.Schema.Value) || !MatchesRationale(rationaleFilter, foreignKey.Rationales))
            {
                continue;
            }

            var anchor = PolicyDecisionLinkBuilder.BuildForeignKeyAnchor(
                foreignKey.Column.Schema.Value,
                foreignKey.Column.Table.Value,
                foreignKey.Column.Column.Value);
            var link = ComposeLink(linkPrefix, anchor);

            rows.Add(new[]
            {
                "Foreign Key",
                module,
                foreignKey.Column.Schema.Value,
                foreignKey.Column.Table.Value,
                foreignKey.Column.Column.Value,
                foreignKey.CreateConstraint ? "Create FK" : "Skip",
                "—",
                FormatRationales(foreignKey.Rationales),
                link
            });
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildDiagnosticRows(
        ImmutableArray<TighteningDiagnostic> diagnostics,
        HashSet<string> moduleFilter,
        HashSet<string> schemaFilter,
        HashSet<TighteningDiagnosticSeverity> severityFilter,
        string? linkPrefix)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (diagnostics.IsDefaultOrEmpty)
        {
            return rows;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null)
            {
                continue;
            }

            if (!MatchesModule(moduleFilter, diagnostic.CanonicalModule)
                || !MatchesSchema(schemaFilter, diagnostic.CanonicalSchema)
                || (severityFilter.Count > 0 && !severityFilter.Contains(diagnostic.Severity)))
            {
                continue;
            }

            var anchor = PolicyDecisionLinkBuilder.BuildDiagnosticAnchor(
                diagnostic.Code,
                diagnostic.CanonicalModule,
                diagnostic.CanonicalSchema,
                diagnostic.CanonicalPhysicalName);
            var link = ComposeLink(linkPrefix, anchor);

            rows.Add(new[]
            {
                diagnostic.Code,
                string.IsNullOrWhiteSpace(diagnostic.CanonicalModule) ? "—" : diagnostic.CanonicalModule,
                diagnostic.CanonicalSchema,
                diagnostic.CanonicalPhysicalName,
                diagnostic.Severity.ToString(),
                diagnostic.Message,
                link
            });
        }

        return rows;
    }

    private static string ComposeLink(string? prefix, string anchor)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return "—";
        }

        return string.IsNullOrWhiteSpace(anchor) ? prefix : $"{prefix}#{anchor}";
    }

    private static string ResolveModule(IReadOnlyDictionary<string, string> modules, string key)
    {
        if (modules is not null && modules.TryGetValue(key, out var module) && !string.IsNullOrWhiteSpace(module))
        {
            return module;
        }

        return "—";
    }

    private static string FormatRationales(ImmutableArray<string> rationales)
    {
        if (rationales.IsDefaultOrEmpty || rationales.Length == 0)
        {
            return "—";
        }

        return string.Join(", ", rationales);
    }

    private static bool MatchesModule(HashSet<string> filter, string module)
        => filter.Count == 0 || filter.Contains(module);

    private static bool MatchesSchema(HashSet<string> filter, string schema)
        => filter.Count == 0 || filter.Contains(schema);

    private static bool MatchesRationale(HashSet<string> filter, ImmutableArray<string> rationales)
    {
        if (filter.Count == 0)
        {
            return true;
        }

        if (rationales.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var rationale in rationales)
        {
            if (filter.Contains(rationale))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildFilterSet(string[]? values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return set;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                set.Add(value.Trim());
            }
        }

        return set;
    }

    private static Result<HashSet<TighteningDiagnosticSeverity>> BuildSeverityFilter(string[]? values)
    {
        var set = new HashSet<TighteningDiagnosticSeverity>();
        if (values is null)
        {
            return Result<HashSet<TighteningDiagnosticSeverity>>.Success(set);
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!Enum.TryParse(value, ignoreCase: true, out TighteningDiagnosticSeverity severity))
            {
                return Result<HashSet<TighteningDiagnosticSeverity>>.Failure($"[error] Unknown severity '{value}'.");
            }

            set.Add(severity);
        }

        return Result<HashSet<TighteningDiagnosticSeverity>>.Success(set);
    }

    private static bool ShouldIncludeSummary(
        HashSet<string> moduleFilter,
        HashSet<string> schemaFilter,
        HashSet<string> rationaleFilter,
        HashSet<TighteningDiagnosticSeverity> severityFilter)
        => moduleFilter.Count == 0
            && schemaFilter.Count == 0
            && rationaleFilter.Count == 0
            && severityFilter.Count == 0;

    private static PolicyDecisionReport FilterReport(
        PolicyDecisionReport report,
        HashSet<string> moduleFilter,
        HashSet<string> schemaFilter,
        HashSet<string> rationaleFilter,
        HashSet<TighteningDiagnosticSeverity> severityFilter)
    {
        var filteredColumns = ImmutableArray.CreateBuilder<ColumnDecisionReport>();
        var filteredUniqueIndexes = ImmutableArray.CreateBuilder<UniqueIndexDecisionReport>();
        var filteredForeignKeys = ImmutableArray.CreateBuilder<ForeignKeyDecisionReport>();
        var filteredDiagnostics = ImmutableArray.CreateBuilder<TighteningDiagnostic>();

        var columnRationales = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var uniqueRationales = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var foreignRationales = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);

        var columnModules = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        var indexModules = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);

        var moduleAggregates = new Dictionary<string, ModuleAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in report.Columns)
        {
            if (column is null)
            {
                continue;
            }

            var module = ResolveModule(report.ColumnModules, column.Column.ToString());
            if (!MatchesModule(moduleFilter, module)
                || !MatchesSchema(schemaFilter, column.Column.Schema.Value)
                || !MatchesRationale(rationaleFilter, column.Rationales))
            {
                continue;
            }

            filteredColumns.Add(column);
            AddRationales(columnRationales, column.Rationales);

            var columnKey = column.Column.ToString();
            if (report.ColumnModules.TryGetValue(columnKey, out var moduleName))
            {
                columnModules[columnKey] = moduleName;
                var aggregate = GetOrAddAggregate(moduleAggregates, moduleName);
                aggregate.AddColumn(column);
            }
        }

        foreach (var uniqueIndex in report.UniqueIndexes)
        {
            if (uniqueIndex is null)
            {
                continue;
            }

            var module = ResolveModule(report.IndexModules, uniqueIndex.Index.ToString());
            if (!MatchesModule(moduleFilter, module)
                || !MatchesSchema(schemaFilter, uniqueIndex.Index.Schema.Value)
                || !MatchesRationale(rationaleFilter, uniqueIndex.Rationales))
            {
                continue;
            }

            filteredUniqueIndexes.Add(uniqueIndex);
            AddRationales(uniqueRationales, uniqueIndex.Rationales);

            var indexKey = uniqueIndex.Index.ToString();
            if (report.IndexModules.TryGetValue(indexKey, out var moduleName))
            {
                indexModules[indexKey] = moduleName;
                var aggregate = GetOrAddAggregate(moduleAggregates, moduleName);
                aggregate.AddUniqueIndex(uniqueIndex);
            }
        }

        foreach (var foreignKey in report.ForeignKeys)
        {
            if (foreignKey is null)
            {
                continue;
            }

            var module = ResolveModule(report.ColumnModules, foreignKey.Column.ToString());
            if (!MatchesModule(moduleFilter, module)
                || !MatchesSchema(schemaFilter, foreignKey.Column.Schema.Value)
                || !MatchesRationale(rationaleFilter, foreignKey.Rationales))
            {
                continue;
            }

            filteredForeignKeys.Add(foreignKey);
            AddRationales(foreignRationales, foreignKey.Rationales);

            var columnKey = foreignKey.Column.ToString();
            if (report.ColumnModules.TryGetValue(columnKey, out var moduleName))
            {
                columnModules[columnKey] = moduleName;
                var aggregate = GetOrAddAggregate(moduleAggregates, moduleName);
                aggregate.AddForeignKey(foreignKey);
            }
        }

        if (!report.Diagnostics.IsDefaultOrEmpty)
        {
            foreach (var diagnostic in report.Diagnostics)
            {
                if (diagnostic is null)
                {
                    continue;
                }

                if (!MatchesModule(moduleFilter, diagnostic.CanonicalModule)
                    || !MatchesSchema(schemaFilter, diagnostic.CanonicalSchema)
                    || (severityFilter.Count > 0 && !severityFilter.Contains(diagnostic.Severity)))
                {
                    continue;
                }

                filteredDiagnostics.Add(diagnostic);
            }
        }

        var moduleRollups = moduleAggregates.Count == 0
            ? ImmutableDictionary<string, ModuleDecisionRollup>.Empty
            : moduleAggregates.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.ToRollup(),
                StringComparer.OrdinalIgnoreCase);

        return new PolicyDecisionReport(
            filteredColumns.ToImmutable(),
            filteredUniqueIndexes.ToImmutable(),
            filteredForeignKeys.ToImmutable(),
            columnRationales.ToImmutable(),
            uniqueRationales.ToImmutable(),
            foreignRationales.ToImmutable(),
            filteredDiagnostics.ToImmutable(),
            moduleRollups,
            report.TogglePrecedence,
            columnModules.ToImmutable(),
            indexModules.ToImmutable(),
            report.Toggles);
    }

    private static void AddRationales(IDictionary<string, int> target, ImmutableArray<string> rationales)
    {
        if (target is null || rationales.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var rationale in rationales)
        {
            if (string.IsNullOrWhiteSpace(rationale))
            {
                continue;
            }

            if (target.TryGetValue(rationale, out var count))
            {
                target[rationale] = count + 1;
            }
            else
            {
                target[rationale] = 1;
            }
        }
    }

    private static ModuleAggregate GetOrAddAggregate(Dictionary<string, ModuleAggregate> aggregates, string module)
    {
        if (aggregates.TryGetValue(module, out var aggregate))
        {
            return aggregate;
        }

        aggregate = new ModuleAggregate();
        aggregates[module] = aggregate;
        return aggregate;
    }

    private static string? ResolveExplicitPath(string path)
    {
        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        return FindLatestReport(path);
    }

    private static string? FindLatestReport(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            root = ".";
        }

        if (!Directory.Exists(root))
        {
            return null;
        }

        var reportCandidates = Directory.EnumerateFiles(root, "policy-decision-report.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();

        if (reportCandidates.Count > 0)
        {
            return reportCandidates[0].FullName;
        }

        reportCandidates = Directory.EnumerateFiles(root, "policy-decisions.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();

        return reportCandidates.Count == 0 ? null : reportCandidates[0].FullName;
    }

    private static async Task<PolicyDecisionReport?> LoadReportAsync(string path, System.Threading.CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        if (string.Equals(Path.GetFileName(path), "policy-decision-report.json", StringComparison.OrdinalIgnoreCase))
        {
            return await JsonSerializer.DeserializeAsync<PolicyDecisionReport>(stream, options, cancellationToken).ConfigureAwait(false);
        }

        var log = await JsonSerializer.DeserializeAsync<PolicyDecisionLogDocument>(stream, options, cancellationToken).ConfigureAwait(false);
        if (log is null)
        {
            return null;
        }

        return ConvertLog(log);
    }

    private static PolicyDecisionReport ConvertLog(PolicyDecisionLogDocument log)
    {
        var columnReports = log.Columns is null
            ? ImmutableArray<ColumnDecisionReport>.Empty
            : log.Columns
                .Where(entry => entry is not null)
                .Select(entry => new ColumnDecisionReport(
                    new ColumnCoordinate(new SchemaName(entry.Schema ?? string.Empty), new TableName(entry.Table ?? string.Empty), new ColumnName(entry.Column ?? string.Empty)),
                    entry.MakeNotNull,
                    entry.RequiresRemediation,
                    entry.Rationales is null ? ImmutableArray<string>.Empty : entry.Rationales.ToImmutableArray()))
                .ToImmutableArray();

        var uniqueReports = log.UniqueIndexes is null
            ? ImmutableArray<UniqueIndexDecisionReport>.Empty
            : log.UniqueIndexes
                .Where(entry => entry is not null)
                .Select(entry => new UniqueIndexDecisionReport(
                    new IndexCoordinate(new SchemaName(entry.Schema ?? string.Empty), new TableName(entry.Table ?? string.Empty), new IndexName(entry.Index ?? string.Empty)),
                    entry.EnforceUnique,
                    entry.RequiresRemediation,
                    entry.Rationales is null ? ImmutableArray<string>.Empty : entry.Rationales.ToImmutableArray()))
                .ToImmutableArray();

        var foreignReports = log.ForeignKeys is null
            ? ImmutableArray<ForeignKeyDecisionReport>.Empty
            : log.ForeignKeys
                .Where(entry => entry is not null)
                .Select(entry => new ForeignKeyDecisionReport(
                    new ColumnCoordinate(new SchemaName(entry.Schema ?? string.Empty), new TableName(entry.Table ?? string.Empty), new ColumnName(entry.Column ?? string.Empty)),
                    entry.CreateConstraint,
                    entry.Rationales is null ? ImmutableArray<string>.Empty : entry.Rationales.ToImmutableArray()))
                .ToImmutableArray();

        var diagnostics = log.Diagnostics is null
            ? ImmutableArray<TighteningDiagnostic>.Empty
            : log.Diagnostics
                .Where(entry => entry is not null)
                .Select(entry => new TighteningDiagnostic(
                    entry.Code ?? string.Empty,
                    entry.Message ?? string.Empty,
                    ParseSeverity(entry.Severity),
                    entry.LogicalName ?? string.Empty,
                    entry.CanonicalModule ?? string.Empty,
                    entry.CanonicalSchema ?? string.Empty,
                    entry.CanonicalPhysicalName ?? string.Empty,
                    entry.Candidates is null
                        ? ImmutableArray<TighteningDuplicateCandidate>.Empty
                        : entry.Candidates
                            .Where(candidate => candidate is not null)
                            .Select(candidate => new TighteningDuplicateCandidate(
                                candidate.Module ?? string.Empty,
                                candidate.Schema ?? string.Empty,
                                candidate.PhysicalName ?? string.Empty))
                            .ToImmutableArray(),
                    entry.ResolvedByOverride))
                .ToImmutableArray();

        var moduleRollups = log.ModuleRollups is null
            ? ImmutableDictionary<string, ModuleDecisionRollup>.Empty
            : log.ModuleRollups.ToImmutableDictionary(
                pair => pair.Key,
                pair => new ModuleDecisionRollup(
                    pair.Value.ColumnCount,
                    pair.Value.TightenedColumnCount,
                    pair.Value.RemediationColumnCount,
                    pair.Value.UniqueIndexCount,
                    pair.Value.UniqueIndexesEnforcedCount,
                    pair.Value.UniqueIndexesRequireRemediationCount,
                    pair.Value.ForeignKeyCount,
                    pair.Value.ForeignKeysCreatedCount,
                    ToImmutableDictionary(pair.Value.ColumnRationales),
                    ToImmutableDictionary(pair.Value.UniqueIndexRationales),
                    ToImmutableDictionary(pair.Value.ForeignKeyRationales)),
                StringComparer.OrdinalIgnoreCase);

        var togglePrecedence = log.TogglePrecedence is null
            ? ImmutableDictionary<string, ToggleExportValue>.Empty
            : log.TogglePrecedence.ToImmutableDictionary(
                pair => pair.Key,
                pair => new ToggleExportValue(ConvertToggleValue(pair.Value.Value), ConvertToggleSource(pair.Value.Source)),
                StringComparer.OrdinalIgnoreCase);

        var columnModules = BuildColumnModuleMap(log.Columns, log.ForeignKeys);
        var indexModules = BuildModuleMap(log.UniqueIndexes);

        return new PolicyDecisionReport(
            columnReports,
            uniqueReports,
            foreignReports,
            ToImmutableDictionary(log.ColumnRationales),
            ToImmutableDictionary(log.UniqueIndexRationales),
            ToImmutableDictionary(log.ForeignKeyRationales),
            diagnostics,
            moduleRollups,
            togglePrecedence,
            columnModules,
            indexModules,
            TighteningToggleSnapshot.Create(TighteningOptions.Default));
    }

    private static ImmutableDictionary<string, string> BuildColumnModuleMap(
        IReadOnlyList<PolicyDecisionLogColumnDocument>? columns,
        IReadOnlyList<PolicyDecisionLogForeignKeyDocument>? foreignKeys)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);

        if (columns is not null)
        {
            foreach (var entry in columns)
            {
                if (entry is null)
                {
                    continue;
                }

                var key = BuildKey(entry.Schema, entry.Table, entry.Column);
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(entry.Module))
                {
                    continue;
                }

                builder[key] = entry.Module!;
            }
        }

        if (foreignKeys is not null)
        {
            foreach (var entry in foreignKeys)
            {
                if (entry is null)
                {
                    continue;
                }

                var key = BuildKey(entry.Schema, entry.Table, entry.Column);
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(entry.Module))
                {
                    continue;
                }

                builder[key] = entry.Module!;
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, string> BuildModuleMap(IReadOnlyList<PolicyDecisionLogUniqueIndexDocument>? indexes)
    {
        if (indexes is null)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in indexes)
        {
            if (entry is null)
            {
                continue;
            }

            var key = BuildKey(entry.Schema, entry.Table, entry.Index);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(entry.Module))
            {
                continue;
            }

            builder[key] = entry.Module!;
        }

        return builder.ToImmutable();
    }

    private static string BuildKey(string? schema, string? table, string? name)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return string.Join('.', schema, table, name);
    }

    private static ImmutableDictionary<string, int> ToImmutableDictionary(IReadOnlyDictionary<string, int>? source)
    {
        if (source is null)
        {
            return ImmutableDictionary<string, int>.Empty;
        }

        return source.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static TighteningDiagnosticSeverity ParseSeverity(string? value)
        => Enum.TryParse(value, ignoreCase: true, out TighteningDiagnosticSeverity severity) ? severity : TighteningDiagnosticSeverity.Info;

    private static ToggleSource ConvertToggleSource(int value)
        => Enum.IsDefined(typeof(ToggleSource), value) ? (ToggleSource)value : ToggleSource.Default;

    private static object? ConvertToggleValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        return value;
    }

    private static string? ResolveHtmlReportPath(string reportPath)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = ".";
        }

        var candidate = Path.Combine(directory, "report.html");
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    private sealed record Result<T>(bool IsSuccess, T Value, string? Error)
    {
        public static Result<T> Success(T value) => new(true, value, null);

        public static Result<T> Failure(string error) => new(false, default!, error);
    }

    private sealed class ModuleAggregate
    {
        private readonly Dictionary<string, int> _columnRationales = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _uniqueRationales = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _foreignRationales = new(StringComparer.Ordinal);

        public int ColumnCount;
        public int TightenedColumnCount;
        public int RemediationColumnCount;
        public int UniqueIndexCount;
        public int UniqueIndexesEnforcedCount;
        public int UniqueIndexesRequireRemediationCount;
        public int ForeignKeyCount;
        public int ForeignKeysCreatedCount;

        public void AddColumn(ColumnDecisionReport column)
        {
            ColumnCount++;
            if (column.MakeNotNull)
            {
                TightenedColumnCount++;
            }

            if (column.RequiresRemediation)
            {
                RemediationColumnCount++;
            }

            AddRationales(_columnRationales, column.Rationales);
        }

        public void AddUniqueIndex(UniqueIndexDecisionReport uniqueIndex)
        {
            UniqueIndexCount++;
            if (uniqueIndex.EnforceUnique)
            {
                UniqueIndexesEnforcedCount++;
            }

            if (uniqueIndex.RequiresRemediation)
            {
                UniqueIndexesRequireRemediationCount++;
            }

            AddRationales(_uniqueRationales, uniqueIndex.Rationales);
        }

        public void AddForeignKey(ForeignKeyDecisionReport foreignKey)
        {
            ForeignKeyCount++;
            if (foreignKey.CreateConstraint)
            {
                ForeignKeysCreatedCount++;
            }

            AddRationales(_foreignRationales, foreignKey.Rationales);
        }

        public ModuleDecisionRollup ToRollup()
            => new(
                ColumnCount,
                TightenedColumnCount,
                RemediationColumnCount,
                UniqueIndexCount,
                UniqueIndexesEnforcedCount,
                UniqueIndexesRequireRemediationCount,
                ForeignKeyCount,
                ForeignKeysCreatedCount,
                _columnRationales.ToImmutableDictionary(StringComparer.Ordinal),
                _uniqueRationales.ToImmutableDictionary(StringComparer.Ordinal),
                _foreignRationales.ToImmutableDictionary(StringComparer.Ordinal));
    }

    private sealed record PolicyDecisionLogDocument(
        IReadOnlyDictionary<string, int>? ColumnRationales,
        IReadOnlyDictionary<string, int>? UniqueIndexRationales,
        IReadOnlyDictionary<string, int>? ForeignKeyRationales,
        IReadOnlyDictionary<string, ModuleDecisionRollupDocument>? ModuleRollups,
        IReadOnlyDictionary<string, ToggleEntryDocument>? TogglePrecedence,
        IReadOnlyList<PolicyDecisionLogColumnDocument>? Columns,
        IReadOnlyList<PolicyDecisionLogUniqueIndexDocument>? UniqueIndexes,
        IReadOnlyList<PolicyDecisionLogForeignKeyDocument>? ForeignKeys,
        IReadOnlyList<PolicyDecisionLogDiagnosticDocument>? Diagnostics);

    private sealed record ModuleDecisionRollupDocument(
        int ColumnCount,
        int TightenedColumnCount,
        int RemediationColumnCount,
        int UniqueIndexCount,
        int UniqueIndexesEnforcedCount,
        int UniqueIndexesRequireRemediationCount,
        int ForeignKeyCount,
        int ForeignKeysCreatedCount,
        IReadOnlyDictionary<string, int>? ColumnRationales,
        IReadOnlyDictionary<string, int>? UniqueIndexRationales,
        IReadOnlyDictionary<string, int>? ForeignKeyRationales);

    private sealed record ToggleEntryDocument(object? Value, int Source);

    private sealed record PolicyDecisionLogColumnDocument(
        string? Schema,
        string? Table,
        string? Column,
        bool MakeNotNull,
        bool RequiresRemediation,
        IReadOnlyList<string>? Rationales,
        string? Module);

    private sealed record PolicyDecisionLogUniqueIndexDocument(
        string? Schema,
        string? Table,
        string? Index,
        bool EnforceUnique,
        bool RequiresRemediation,
        IReadOnlyList<string>? Rationales,
        string? Module);

    private sealed record PolicyDecisionLogForeignKeyDocument(
        string? Schema,
        string? Table,
        string? Column,
        bool CreateConstraint,
        IReadOnlyList<string>? Rationales,
        string? Module);

    private sealed record PolicyDecisionLogDiagnosticDocument(
        string? Code,
        string? Message,
        string? Severity,
        string? LogicalName,
        string? CanonicalModule,
        string? CanonicalSchema,
        string? CanonicalPhysicalName,
        IReadOnlyList<PolicyDecisionLogDiagnosticCandidateDocument>? Candidates,
        bool ResolvedByOverride);

    private sealed record PolicyDecisionLogDiagnosticCandidateDocument(
        string? Module,
        string? Schema,
        string? PhysicalName);
}
