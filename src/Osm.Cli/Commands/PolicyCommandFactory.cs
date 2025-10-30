using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Validation.Tightening;

namespace Osm.Cli.Commands;

internal sealed class PolicyCommandFactory : ICommandFactory
{
    private const string ReportFileName = "policy-decisions.report.json";

    private readonly Option<string?> _reportOption = new("--report", "Path to a policy-decisions.report.json file.");
    private readonly Option<string?> _rootOption = new("--root", () => "out", "Directory to search for pipeline outputs.");
    private readonly Option<string[]> _moduleOption = new("--module", "Filter by module (repeatable).")
    {
        AllowMultipleArgumentsPerToken = true
    };
    private readonly Option<string[]> _schemaOption = new("--schema", "Filter by schema (repeatable).")
    {
        AllowMultipleArgumentsPerToken = true
    };
    private readonly Option<string[]> _rationaleOption = new("--rationale", "Filter by rationale (repeatable).")
    {
        AllowMultipleArgumentsPerToken = true
    };
    private readonly Option<string[]> _severityOption = new("--severity", "Filter diagnostics by severity (repeatable).")
    {
        AllowMultipleArgumentsPerToken = true
    };
    private readonly Option<PolicyExplainFormat> _formatOption = new("--format", () => PolicyExplainFormat.Table, "Output format (table or json).");

    public Command Create()
    {
        var policy = new Command("policy", "Inspect tightening policy artifacts.");
        policy.AddCommand(CreateExplainCommand());
        return policy;
    }

    private Command CreateExplainCommand()
    {
        var explain = new Command("explain", "Display tightening decisions from a pipeline run.");
        explain.AddOption(_reportOption);
        explain.AddOption(_rootOption);
        explain.AddOption(_moduleOption);
        explain.AddOption(_schemaOption);
        explain.AddOption(_rationaleOption);
        explain.AddOption(_severityOption);
        explain.AddOption(_formatOption);
        explain.SetHandler(async context => await ExecuteExplainAsync(context).ConfigureAwait(false));
        return explain;
    }

    private async Task ExecuteExplainAsync(InvocationContext context)
    {
        var parseResult = context.ParseResult;
        var reportOverride = parseResult.GetValueForOption(_reportOption);
        var searchRoot = parseResult.GetValueForOption(_rootOption);
        var format = parseResult.GetValueForOption(_formatOption);

        string? reportPath;
        if (!string.IsNullOrWhiteSpace(reportOverride))
        {
            reportPath = Path.GetFullPath(reportOverride!);
            if (!File.Exists(reportPath))
            {
                CommandConsole.WriteErrorLine(context.Console, $"[error] Report '{reportPath}' was not found.");
                context.ExitCode = 1;
                return;
            }
        }
        else
        {
            reportPath = FindLatestReport(searchRoot);
            if (reportPath is null)
            {
                CommandConsole.WriteErrorLine(context.Console, $"[error] Could not locate {ReportFileName}. Provide --report or adjust --root.");
                context.ExitCode = 1;
                return;
            }
        }

        PolicyDecisionReport report;
        try
        {
            var json = await File.ReadAllTextAsync(reportPath, context.GetCancellationToken()).ConfigureAwait(false);
            report = JsonSerializer.Deserialize<PolicyDecisionReport>(json, PolicyDecisionReportJson.GetSerializerOptions())
                ?? throw new JsonException("Report deserialized to null.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CommandConsole.WriteErrorLine(context.Console, $"[error] Failed to read report '{reportPath}': {ex.Message}");
            context.ExitCode = 1;
            return;
        }

        if (!TryBuildFilter(parseResult, context.Console, out var filter))
        {
            context.ExitCode = 1;
            return;
        }

        var filteredColumns = report.Columns
            .Where(column => MatchesColumn(column, filter))
            .ToList();

        var filteredUniques = report.UniqueIndexes
            .Where(index => MatchesUnique(index, filter))
            .ToList();

        var filteredForeignKeys = report.ForeignKeys
            .Where(foreignKey => MatchesForeignKey(foreignKey, filter))
            .ToList();

        var filteredDiagnostics = filter.Rationales.Count == 0
            ? report.Diagnostics.Where(diagnostic => MatchesDiagnostic(diagnostic, filter)).ToList()
            : new List<TighteningDiagnostic>();

        var relativeReportPath = GetRelativePath(reportPath);
        CommandConsole.WriteLine(context.Console, $"Report: {relativeReportPath}");

        if (filter.HasAny)
        {
            CommandConsole.WriteLine(context.Console, BuildFilterSummary(filter));
        }

        var reportDirectory = Path.GetDirectoryName(reportPath)!;
        var htmlPath = Path.Combine(reportDirectory, "report.html");
        string? reportLinkBase = null;
        if (File.Exists(htmlPath))
        {
            reportLinkBase = GetRelativePath(htmlPath);
        }

        if (format == PolicyExplainFormat.Json)
        {
            EmitJson(context.Console, filteredColumns, filteredUniques, filteredForeignKeys, filteredDiagnostics, filter, relativeReportPath, reportLinkBase);
        }
        else
        {
            EmitTableOutput(context.Console, filteredColumns, filteredUniques, filteredForeignKeys, filteredDiagnostics, reportLinkBase);
        }

        context.ExitCode = 0;
    }

    private static string? FindLatestReport(string? root)
    {
        var searchRoot = string.IsNullOrWhiteSpace(root) ? "." : root!;
        var normalizedRoot = Path.GetFullPath(searchRoot);
        if (!Directory.Exists(normalizedRoot))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(normalizedRoot, ReportFileName, SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool TryBuildFilter(ParseResult parseResult, IConsole console, out ExplainFilter filter)
    {
        var modules = BuildSet(parseResult.GetValueForOption(_moduleOption));
        var schemas = BuildSet(parseResult.GetValueForOption(_schemaOption));
        var rationales = BuildSet(parseResult.GetValueForOption(_rationaleOption));
        var severityValues = parseResult.GetValueForOption(_severityOption) ?? Array.Empty<string>();
        var severities = new HashSet<TighteningDiagnosticSeverity>();

        foreach (var value in severityValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!Enum.TryParse<TighteningDiagnosticSeverity>(value, true, out var severity))
            {
                CommandConsole.WriteErrorLine(console, $"[error] Unknown severity '{value}'. Use Warning or Error.");
                filter = ExplainFilter.Empty;
                return false;
            }

            severities.Add(severity);
        }

        filter = new ExplainFilter(modules, schemas, rationales, severities);
        return true;
    }

    private static HashSet<string> BuildSet(string[]? values)
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

    private static bool MatchesColumn(ColumnDecisionReport column, ExplainFilter filter)
    {
        if (!MatchesModule(column.Module, filter.Modules))
        {
            return false;
        }

        if (!MatchesSchema(column.Column.Schema.Value, filter.Schemas))
        {
            return false;
        }

        if (!MatchesRationales(column.Rationales, filter.Rationales))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesUnique(UniqueIndexDecisionReport unique, ExplainFilter filter)
    {
        if (!MatchesModule(unique.Module, filter.Modules))
        {
            return false;
        }

        if (!MatchesSchema(unique.Index.Schema.Value, filter.Schemas))
        {
            return false;
        }

        if (!MatchesRationales(unique.Rationales, filter.Rationales))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesForeignKey(ForeignKeyDecisionReport foreignKey, ExplainFilter filter)
    {
        if (!MatchesModule(foreignKey.Module, filter.Modules))
        {
            return false;
        }

        if (!MatchesSchema(foreignKey.Column.Schema.Value, filter.Schemas))
        {
            return false;
        }

        if (!MatchesRationales(foreignKey.Rationales, filter.Rationales))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesDiagnostic(TighteningDiagnostic diagnostic, ExplainFilter filter)
    {
        if (!MatchesModule(diagnostic.CanonicalModule, filter.Modules))
        {
            return false;
        }

        if (!MatchesSchema(diagnostic.CanonicalSchema, filter.Schemas))
        {
            return false;
        }

        if (filter.Severities.Count > 0 && !filter.Severities.Contains(diagnostic.Severity))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesModule(string? module, HashSet<string> filter)
        => filter.Count == 0 || (module is not null && filter.Contains(module));

    private static bool MatchesSchema(string? schema, HashSet<string> filter)
    {
        if (filter.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(schema))
        {
            return false;
        }

        return filter.Contains(schema);
    }

    private static bool MatchesRationales(ImmutableArray<string> rationales, HashSet<string> filter)
    {
        if (filter.Count == 0)
        {
            return true;
        }

        if (rationales.IsDefaultOrEmpty)
        {
            return false;
        }

        var actual = new HashSet<string>(rationales, StringComparer.OrdinalIgnoreCase);
        return filter.All(actual.Contains);
    }

    private static void EmitTableOutput(
        IConsole console,
        IReadOnlyList<ColumnDecisionReport> columns,
        IReadOnlyList<UniqueIndexDecisionReport> uniques,
        IReadOnlyList<ForeignKeyDecisionReport> foreignKeys,
        IReadOnlyList<TighteningDiagnostic> diagnostics,
        string? reportLinkBase)
    {
        if (columns.Count == 0 && uniques.Count == 0 && foreignKeys.Count == 0 && diagnostics.Count == 0)
        {
            CommandConsole.WriteLine(console, "No policy decisions matched the provided filters.");
            return;
        }

        CommandConsole.WriteLine(console, "Column decisions:");
        var columnRows = columns
            .Select(column => (IReadOnlyList<string>)new string[]
            {
                column.Module ?? "—",
                column.Column.Schema.Value,
                column.Column.Table.Value,
                column.Column.Column.Value,
                DescribeColumnAction(column),
                column.Rationales.IsDefaultOrEmpty ? "—" : string.Join(", ", column.Rationales),
                BuildLink(reportLinkBase, PolicyDecisionLinkBuilder.CreateColumnAnchor(column.Column)) ?? "—"
            })
            .ToList();
        CommandConsole.EmitTable(console, new[] { "Module", "Schema", "Table", "Column", "Action", "Rationales", "Report" }, columnRows);

        CommandConsole.WriteLine(console, string.Empty);
        CommandConsole.WriteLine(console, "Unique index decisions:");
        var uniqueRows = uniques
            .Select(unique => (IReadOnlyList<string>)new string[]
            {
                unique.Module ?? "—",
                unique.Index.Schema.Value,
                unique.Index.Table.Value,
                unique.Index.Index.Value,
                DescribeUniqueAction(unique),
                unique.Rationales.IsDefaultOrEmpty ? "—" : string.Join(", ", unique.Rationales),
                BuildLink(reportLinkBase, PolicyDecisionLinkBuilder.CreateUniqueIndexAnchor(unique.Index)) ?? "—"
            })
            .ToList();
        CommandConsole.EmitTable(console, new[] { "Module", "Schema", "Table", "Index", "Action", "Rationales", "Report" }, uniqueRows);

        CommandConsole.WriteLine(console, string.Empty);
        CommandConsole.WriteLine(console, "Foreign key decisions:");
        var foreignRows = foreignKeys
            .Select(foreignKey => (IReadOnlyList<string>)new string[]
            {
                foreignKey.Module ?? "—",
                foreignKey.Column.Schema.Value,
                foreignKey.Column.Table.Value,
                foreignKey.Column.Column.Value,
                DescribeForeignKeyAction(foreignKey),
                foreignKey.Rationales.IsDefaultOrEmpty ? "—" : string.Join(", ", foreignKey.Rationales),
                BuildLink(reportLinkBase, PolicyDecisionLinkBuilder.CreateForeignKeyAnchor(foreignKey.Column)) ?? "—"
            })
            .ToList();
        CommandConsole.EmitTable(console, new[] { "Module", "Schema", "Table", "Column", "Action", "Rationales", "Report" }, foreignRows);

        CommandConsole.WriteLine(console, string.Empty);
        CommandConsole.WriteLine(console, "Diagnostics:");
        var diagnosticRows = diagnostics
            .Select(diagnostic => (IReadOnlyList<string>)new string[]
            {
                diagnostic.CanonicalModule ?? "—",
                diagnostic.CanonicalSchema ?? "—",
                diagnostic.CanonicalPhysicalName ?? "—",
                diagnostic.Code,
                diagnostic.Severity.ToString(),
                diagnostic.Message,
                BuildLink(reportLinkBase, PolicyDecisionLinkBuilder.CreateDiagnosticAnchor(diagnostic)) ?? "—"
            })
            .ToList();
        CommandConsole.EmitTable(console, new[] { "Module", "Schema", "Object", "Code", "Severity", "Message", "Report" }, diagnosticRows);
    }

    private static void EmitJson(
        IConsole console,
        IReadOnlyList<ColumnDecisionReport> columns,
        IReadOnlyList<UniqueIndexDecisionReport> uniques,
        IReadOnlyList<ForeignKeyDecisionReport> foreignKeys,
        IReadOnlyList<TighteningDiagnostic> diagnostics,
        ExplainFilter filter,
        string reportPath,
        string? reportLinkBase)
    {
        var payload = new
        {
            report = reportPath,
            reportHtml = reportLinkBase,
            filters = new
            {
                modules = filter.Modules.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                schemas = filter.Schemas.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                rationales = filter.Rationales.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                severities = filter.Severities.OrderBy(static value => value).Select(static value => value.ToString()).ToArray()
            },
            columns = columns.Select(column => new
            {
                module = column.Module,
                schema = column.Column.Schema.Value,
                table = column.Column.Table.Value,
                column = column.Column.Column.Value,
                action = DescribeColumnAction(column),
                makeNotNull = column.MakeNotNull,
                requiresRemediation = column.RequiresRemediation,
                rationales = column.Rationales.IsDefaultOrEmpty ? Array.Empty<string>() : column.Rationales.ToArray(),
                reportLink = BuildLink(reportLinkBase, PolicyDecisionLinkBuilder.CreateColumnAnchor(column.Column))
            }).ToArray(),
            uniqueIndexes = uniques.Select(unique => new
            {
                module = unique.Module,
                schema = unique.Index.Schema.Value,
                table = unique.Index.Table.Value,
                index = unique.Index.Index.Value,
                action = DescribeUniqueAction(unique),
                enforceUnique = unique.EnforceUnique,
                requiresRemediation = unique.RequiresRemediation,
                rationales = unique.Rationales.IsDefaultOrEmpty ? Array.Empty<string>() : unique.Rationales.ToArray(),
                reportLink = BuildLink(reportLinkBase, PolicyDecisionLinkBuilder.CreateUniqueIndexAnchor(unique.Index))
            }).ToArray(),
            foreignKeys = foreignKeys.Select(foreignKey => new
            {
                module = foreignKey.Module,
                schema = foreignKey.Column.Schema.Value,
                table = foreignKey.Column.Table.Value,
                column = foreignKey.Column.Column.Value,
                action = DescribeForeignKeyAction(foreignKey),
                createConstraint = foreignKey.CreateConstraint,
                rationales = foreignKey.Rationales.IsDefaultOrEmpty ? Array.Empty<string>() : foreignKey.Rationales.ToArray(),
                reportLink = BuildLink(reportLinkBase, PolicyDecisionLinkBuilder.CreateForeignKeyAnchor(foreignKey.Column))
            }).ToArray(),
            diagnostics = diagnostics.Select(diagnostic => new
            {
                module = diagnostic.CanonicalModule,
                schema = diagnostic.CanonicalSchema,
                objectName = diagnostic.CanonicalPhysicalName,
                code = diagnostic.Code,
                severity = diagnostic.Severity.ToString(),
                message = diagnostic.Message,
                reportLink = BuildLink(reportLinkBase, PolicyDecisionLinkBuilder.CreateDiagnosticAnchor(diagnostic))
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        CommandConsole.WriteLine(console, json);
    }

    private static string DescribeColumnAction(ColumnDecisionReport column)
    {
        if (column.MakeNotNull && column.RequiresRemediation)
        {
            return "Tighten to NOT NULL (remediation)";
        }

        if (column.MakeNotNull)
        {
            return "Tighten to NOT NULL";
        }

        if (column.RequiresRemediation)
        {
            return "Remediation required";
        }

        return "No change";
    }

    private static string DescribeUniqueAction(UniqueIndexDecisionReport unique)
    {
        if (unique.EnforceUnique && unique.RequiresRemediation)
        {
            return "Enforce unique (remediation)";
        }

        if (unique.EnforceUnique)
        {
            return "Enforce unique";
        }

        if (unique.RequiresRemediation)
        {
            return "Remediation required";
        }

        return "No change";
    }

    private static string DescribeForeignKeyAction(ForeignKeyDecisionReport foreignKey)
        => foreignKey.CreateConstraint ? "Create constraint" : "Skip";

    private static string? BuildLink(string? reportLinkBase, string anchor)
    {
        if (reportLinkBase is null)
        {
            return null;
        }

        var basePath = string.IsNullOrWhiteSpace(reportLinkBase) || reportLinkBase == "."
            ? "report.html"
            : reportLinkBase;
        return PolicyDecisionLinkBuilder.BuildReportLink(basePath, anchor);
    }

    private static string GetRelativePath(string path)
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
        if (string.IsNullOrWhiteSpace(relative) || relative == ".")
        {
            return Path.GetFileName(path);
        }

        return relative;
    }

    private static string BuildFilterSummary(ExplainFilter filter)
    {
        var parts = new List<string>();
        if (filter.Modules.Count > 0)
        {
            parts.Add("modules=" + string.Join(",", filter.Modules.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
        }

        if (filter.Schemas.Count > 0)
        {
            parts.Add("schemas=" + string.Join(",", filter.Schemas.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
        }

        if (filter.Rationales.Count > 0)
        {
            parts.Add("rationales=" + string.Join(",", filter.Rationales.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
        }

        if (filter.Severities.Count > 0)
        {
            parts.Add("severities=" + string.Join(",", filter.Severities.OrderBy(static value => value).Select(static value => value.ToString())));
        }

        return "Filters: " + string.Join("; ", parts);
    }

    private sealed record ExplainFilter(
        HashSet<string> Modules,
        HashSet<string> Schemas,
        HashSet<string> Rationales,
        HashSet<TighteningDiagnosticSeverity> Severities)
    {
        public static ExplainFilter Empty { get; } = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<TighteningDiagnosticSeverity>());

        public bool HasAny => Modules.Count > 0 || Schemas.Count > 0 || Rationales.Count > 0 || Severities.Count > 0;
    }

    private enum PolicyExplainFormat
    {
        Table,
        Json
    }
}
