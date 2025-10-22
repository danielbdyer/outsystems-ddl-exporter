using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Smo.PerTableEmission;

namespace Osm.Smo;

public sealed class PerTableWriter
{
    private readonly SmoContext _context;
    private readonly CreateTableStatementBuilder _createTableStatementBuilder;
    private readonly IndexScriptBuilder _indexScriptBuilder;
    private readonly ExtendedPropertyScriptBuilder _extendedPropertyScriptBuilder;
    private readonly SqlScriptFormatter _sqlScriptFormatter;

    public PerTableWriter()
        : this(new SmoContext(), new SqlScriptFormatter())
    {
    }

    private PerTableWriter(SmoContext context, SqlScriptFormatter sqlScriptFormatter)
        : this(
            context,
            new CreateTableStatementBuilder(sqlScriptFormatter),
            new IndexScriptBuilder(sqlScriptFormatter),
            new ExtendedPropertyScriptBuilder(sqlScriptFormatter),
            sqlScriptFormatter)
    {
    }

    internal PerTableWriter(
        SmoContext context,
        CreateTableStatementBuilder createTableStatementBuilder,
        IndexScriptBuilder indexScriptBuilder,
        ExtendedPropertyScriptBuilder extendedPropertyScriptBuilder,
        SqlScriptFormatter sqlScriptFormatter)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _createTableStatementBuilder = createTableStatementBuilder ?? throw new ArgumentNullException(nameof(createTableStatementBuilder));
        _indexScriptBuilder = indexScriptBuilder ?? throw new ArgumentNullException(nameof(indexScriptBuilder));
        _extendedPropertyScriptBuilder = extendedPropertyScriptBuilder ?? throw new ArgumentNullException(nameof(extendedPropertyScriptBuilder));
        _sqlScriptFormatter = sqlScriptFormatter ?? throw new ArgumentNullException(nameof(sqlScriptFormatter));
    }

    public PerTableWriteResult Generate(
        SmoTableDefinition table,
        SmoBuildOptions options,
        IReadOnlyList<PerTableHeaderItem>? tableHeaderItems = null,
        CancellationToken cancellationToken = default)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var effectiveTableName = options.NamingOverrides.GetEffectiveTableName(
            table.Schema,
            table.Name,
            table.LogicalName,
            table.OriginalModule);

        var columnNameMap = BuildColumnNameMap(table);

        var statement = _createTableStatementBuilder.BuildCreateTableStatement(table, effectiveTableName, options);
        var inlineForeignKeys = _createTableStatementBuilder.AddForeignKeys(statement, table, effectiveTableName, options, out var foreignKeyTrustLookup);
        var tableScript = Script(statement, foreignKeyTrustLookup, options.Format);

        var statements = new List<string> { tableScript };
        var indexNames = ImmutableArray.CreateBuilder<string>();

        if (!options.EmitBareTableOnly)
        {
            foreach (var index in table.Indexes)
            {
                if (index.IsPrimaryKey)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var indexName = _sqlScriptFormatter.ResolveConstraintName(index.Name, table.Name, table.LogicalName, effectiveTableName);
                var indexStatement = _indexScriptBuilder.BuildCreateIndexStatement(table, index, effectiveTableName, indexName, options.Format);
                var indexScript = Script(indexStatement, format: options.Format);
                statements.Add(indexScript);
                indexNames.Add(indexName);

                if (index.Metadata.IsDisabled)
                {
                    var disableStatement = _indexScriptBuilder.BuildDisableIndexStatement(table, effectiveTableName, indexName, options.Format);
                    var disableScript = Script(disableStatement, format: options.Format);
                    statements.Add(disableScript);
                }
            }
        }

        var includesExtendedProperties = false;
        if (!options.EmitBareTableOnly)
        {
            var extendedPropertyScripts = _extendedPropertyScriptBuilder.BuildExtendedPropertyScripts(table, effectiveTableName, options.Format);
            if (!extendedPropertyScripts.IsDefaultOrEmpty)
            {
                statements.AddRange(extendedPropertyScripts);
                includesExtendedProperties = true;
            }

            var triggerScripts = BuildTriggerScripts(table, effectiveTableName, options.Format, columnNameMap);
            if (!triggerScripts.IsDefaultOrEmpty)
            {
                statements.AddRange(triggerScripts);
            }
        }

        var orderedIndexNames = indexNames.ToImmutable();
        if (!orderedIndexNames.IsDefaultOrEmpty)
        {
            orderedIndexNames = orderedIndexNames.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var foreignKeyNames = inlineForeignKeys;
        if (!foreignKeyNames.IsDefaultOrEmpty)
        {
            foreignKeyNames = foreignKeyNames.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var script = _sqlScriptFormatter.JoinStatements(statements, options.Format);
        var header = BuildHeader(options.Header, tableHeaderItems);
        if (!string.IsNullOrEmpty(header))
        {
            script = string.Concat(header, Environment.NewLine, Environment.NewLine, script);
        }

        return new PerTableWriteResult(
            effectiveTableName,
            script,
            orderedIndexNames,
            foreignKeyNames,
            includesExtendedProperties);
    }

    private static string? BuildHeader(PerTableHeaderOptions headerOptions, IReadOnlyList<PerTableHeaderItem>? tableItems)
    {
        if (headerOptions is null || !headerOptions.Enabled)
        {
            return null;
        }

        var items = new List<PerTableHeaderItem>();

        void Add(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            items.Add(PerTableHeaderItem.Create(label, value));
        }

        Add("Source", headerOptions.Source);
        Add("Profile", headerOptions.Profile);
        Add("Decisions", headerOptions.Decisions);

        if (!string.IsNullOrWhiteSpace(headerOptions.FingerprintHash))
        {
            var label = string.IsNullOrWhiteSpace(headerOptions.FingerprintAlgorithm)
                ? "Fingerprint"
                : $"{headerOptions.FingerprintAlgorithm} Fingerprint";
            Add(label, headerOptions.FingerprintHash);
        }

        if (!headerOptions.AdditionalItems.IsDefaultOrEmpty)
        {
            foreach (var item in headerOptions.AdditionalItems)
            {
                if (string.IsNullOrEmpty(item.Label) || string.IsNullOrEmpty(item.Value))
                {
                    continue;
                }

                items.Add(item);
            }
        }

        if (tableItems is { Count: > 0 })
        {
            foreach (var item in tableItems)
            {
                if (item is null || string.IsNullOrEmpty(item.Label) || string.IsNullOrEmpty(item.Value))
                {
                    continue;
                }

                items.Add(item.Normalize());
            }
        }

        if (items.Count == 0)
        {
            return null;
        }

        var ordered = items
            .Select(static item => item.Normalize())
            .OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("/*");
        foreach (var item in ordered)
        {
            builder.Append("    ");
            builder.Append(item.Label);
            builder.Append(": ");
            builder.AppendLine(item.Value);
        }

        builder.Append("*/");
        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, string> BuildColumnNameMap(SmoTableDefinition table)
    {
        if (table.Columns.Length == 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.PhysicalName) || string.IsNullOrWhiteSpace(column.Name))
            {
                continue;
            }

            builder[column.PhysicalName] = column.Name;
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<string> BuildTriggerScripts(
        SmoTableDefinition table,
        string effectiveTableName,
        SmoFormatOptions format,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        if (table.Triggers.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var scripts = ImmutableArray.CreateBuilder<string>(table.Triggers.Length);
        foreach (var trigger in table.Triggers)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"-- Trigger: {trigger.Name} (disabled: {trigger.IsDisabled.ToString().ToLowerInvariant()})");

            if (!string.IsNullOrWhiteSpace(trigger.Definition))
            {
                var rewritten = RewriteTriggerDefinition(trigger.Definition.Trim(), trigger, effectiveTableName, format, columnNameMap);
                builder.AppendLine(rewritten);
            }

            if (trigger.IsDisabled)
            {
                var schemaIdentifier = _sqlScriptFormatter.QuoteIdentifier(trigger.Schema, format);
                var tableIdentifier = _sqlScriptFormatter.QuoteIdentifier(effectiveTableName, format);
                var triggerIdentifier = _sqlScriptFormatter.QuoteIdentifier(trigger.Name, format);
                builder.AppendLine($"ALTER TABLE {schemaIdentifier}.{tableIdentifier} DISABLE TRIGGER {triggerIdentifier};");
            }

            scripts.Add(builder.ToString().TrimEnd());
        }

        return scripts.ToImmutable();
    }

    private string RewriteTriggerDefinition(
        string definition,
        SmoTriggerDefinition trigger,
        string effectiveTableName,
        SmoFormatOptions format,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        var rewritten = definition;

        var schemaIdentifier = _sqlScriptFormatter.QuoteIdentifier(trigger.Schema, format);
        var physicalTableIdentifier = _sqlScriptFormatter.QuoteIdentifier(trigger.Table, format);
        var effectiveTableIdentifier = _sqlScriptFormatter.QuoteIdentifier(effectiveTableName, format);

        rewritten = ReplaceIgnoreCase(rewritten, $"{schemaIdentifier}.{physicalTableIdentifier}", $"{schemaIdentifier}.{effectiveTableIdentifier}");
        rewritten = ReplaceIgnoreCase(rewritten, physicalTableIdentifier, effectiveTableIdentifier);
        rewritten = ReplaceIgnoreCase(rewritten, trigger.Table, effectiveTableName);

        foreach (var pair in columnNameMap)
        {
            var physicalColumn = _sqlScriptFormatter.QuoteIdentifier(pair.Key, format);
            var effectiveColumn = _sqlScriptFormatter.QuoteIdentifier(pair.Value, format);

            rewritten = ReplaceIgnoreCase(rewritten, physicalColumn, effectiveColumn);
            rewritten = ReplaceIgnoreCase(rewritten, pair.Key, pair.Value);
        }

        return rewritten.Trim();
    }

    private static string ReplaceIgnoreCase(string source, string search, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search))
        {
            return source;
        }

        var builder = new StringBuilder(source.Length + Math.Max(0, replacement.Length - search.Length));
        var index = 0;

        while (true)
        {
            var match = source.IndexOf(search, index, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                builder.Append(source, index, source.Length - index);
                break;
            }

            builder.Append(source, index, match - index);
            builder.Append(replacement);
            index = match + search.Length;
        }

        return builder.ToString();
    }

    private string Script(
        TSqlStatement statement,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup = null,
        SmoFormatOptions? format = null)
    {
        _context.ScriptGenerator.GenerateScript(statement, out var script);
        var trimmed = script.Trim();

        var effectiveFormat = format ?? SmoFormatOptions.Default;

        return statement switch
        {
            CreateTableStatement createTable => _sqlScriptFormatter.FormatCreateTableScript(trimmed, createTable, foreignKeyTrustLookup, effectiveFormat),
            _ => effectiveFormat.NormalizeWhitespace ? _sqlScriptFormatter.NormalizeWhitespace(trimmed) : trimmed,
        };
    }

    private string Script(TSqlStatement statement, SmoFormatOptions format)
        => Script(statement, null, format);
}

public sealed record PerTableWriteResult(
    string EffectiveTableName,
    string Script,
    ImmutableArray<string> IndexNames,
    ImmutableArray<string> ForeignKeyNames,
    bool IncludesExtendedProperties);
