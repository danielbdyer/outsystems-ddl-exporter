using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Domain.Abstractions;

namespace Osm.Dmm;

public sealed class ScriptDomDmmLens : IDmmLens<TextReader>
{
    private static readonly Regex ExtendedPropertyRegex = new(
        "sp_(?:add|update)extendedproperty\\s*@name\\s*=\\s*N'(?<name>[^']+)'\\s*,\\s*@value\\s*=\\s*N'(?<value>(?:''|[^'])*)'.*?@level0type\\s*=\\s*N'SCHEMA'\\s*,\\s*@level0name\\s*=\\s*N'(?<schema>[^']+)'.*?@level1type\\s*=\\s*N'TABLE'\\s*,\\s*@level1name\\s*=\\s*N'(?<table>[^']+)'(?<columnSection>.*?@level2type\\s*=\\s*N'COLUMN'\\s*,\\s*@level2name\\s*=\\s*N'(?<column>[^']+)')?",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public Result<IReadOnlyList<DmmTable>> Project(TextReader reader)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        var script = reader.ReadToEnd();
        var parser = new TSql150Parser(initialQuotedIdentifiers: true);
        using var textReader = new StringReader(script);
        var fragment = parser.Parse(textReader, out var errors);
        if (errors is { Count: > 0 })
        {
            var message = string.Join(Environment.NewLine, errors.Select(e => $"{e.Line}:{e.Column} {e.Message}"));
            return Result<IReadOnlyList<DmmTable>>.Failure(ValidationError.Create("dmm.parse.failed", message));
        }

        var builder = new TableModelBuilder();
        fragment.Accept(builder);
        builder.ApplyExtendedProperties(script);
        return Result<IReadOnlyList<DmmTable>>.Success(builder.Build());
    }

    private sealed class TableModelBuilder : TSqlFragmentVisitor
    {
        private readonly Dictionary<string, DmmTableAccumulator> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Sql150ScriptGenerator _generator = new(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Lowercase,
            IncludeSemicolons = false,
            SqlVersion = SqlVersion.Sql150,
        });

        public IReadOnlyList<DmmTable> Build()
        {
            return _tables.Values
                .Select(acc => acc.ToTable())
                .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void ApplyExtendedProperties(string script)
        {
            foreach (Match match in ExtendedPropertyRegex.Matches(script))
            {
                var propertyName = match.Groups["name"].Value;
                if (!string.Equals(propertyName, "MS_Description", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var schema = match.Groups["schema"].Value;
                var table = match.Groups["table"].Value;
                var value = Unescape(match.Groups["value"].Value);
                var accumulator = GetOrCreate(schema, table);
                if (match.Groups["column"].Success)
                {
                    var columnName = match.Groups["column"].Value;
                    var column = accumulator.GetOrCreateColumn(columnName);
                    column.Description = value;
                }
                else
                {
                    accumulator.Description = value;
                }
            }
        }

        public override void ExplicitVisit(IfStatement node)
        {
            node.ThenStatement?.Accept(this);
            node.ElseStatement?.Accept(this);
        }

        public override void ExplicitVisit(BeginEndBlockStatement node)
        {
            node.StatementList?.Accept(this);
        }

        public override void Visit(CreateTableStatement node)
        {
            var accumulator = GetOrCreate(node.SchemaObjectName);
            foreach (var column in node.Definition.ColumnDefinitions)
            {
                var columnAccumulator = accumulator.GetOrCreateColumn(column.ColumnIdentifier.Value);
                columnAccumulator.DataType = Canonicalize(Script(column.DataType));
                columnAccumulator.IsNullable = IsNullable(column);
                columnAccumulator.DefaultExpression = ExtractDefaultExpression(column);
                columnAccumulator.Collation = NormalizeCollation(column.Collation?.Value);
            }

            foreach (var constraint in node.Definition.TableConstraints)
            {
                ProcessConstraint(accumulator, constraint);
            }
        }

        public override void Visit(AlterTableAddTableElementStatement node)
        {
            var accumulator = GetOrCreate(node.SchemaObjectName);
            foreach (var constraint in node.Definition.TableConstraints)
            {
                ProcessConstraint(accumulator, constraint);
            }
        }

        public override void Visit(CreateIndexStatement node)
        {
            var accumulator = GetOrCreate(node.OnName);
            var indexName = node.Name?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(indexName))
            {
                return;
            }

            var index = accumulator.GetOrCreateIndex(indexName);
            index.Reset();
            index.IsUnique = node.Unique;

            foreach (var column in node.Columns)
            {
                var columnName = column.Column.MultiPartIdentifier.Identifiers.Last().Value;
                var isDescending = column.SortOrder == SortOrder.Descending;
                index.KeyColumns.Add(new DmmIndexColumn(columnName, isDescending));
            }

            if (node.IncludeColumns is { Count: > 0 } includeColumns)
            {
                foreach (var include in includeColumns)
                {
                    var columnName = include.MultiPartIdentifier.Identifiers.Last().Value;
                    index.IncludedColumns.Add(new DmmIndexColumn(columnName, IsDescending: false));
                }
            }

            index.FilterDefinition = node.FilterPredicate is null
                ? null
                : CanonicalizeExpression(Script(node.FilterPredicate));
            ApplyOptions(index, node.IndexOptions);
            index.IsDisabled = false;
        }

        public override void Visit(AlterIndexStatement node)
        {
            var accumulator = GetOrCreate(node.OnName);
            var indexName = node.Name?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(indexName))
            {
                return;
            }

            var index = accumulator.GetOrCreateIndex(indexName);
            switch (node.AlterIndexType)
            {
                case AlterIndexType.Disable:
                    index.IsDisabled = true;
                    break;
                case AlterIndexType.Rebuild:
                case AlterIndexType.Reorganize:
                    index.IsDisabled = false;
                    break;
            }
        }

        private void ProcessConstraint(DmmTableAccumulator accumulator, ConstraintDefinition constraint)
        {
            switch (constraint)
            {
                case UniqueConstraintDefinition unique when unique.IsPrimaryKey:
                    accumulator.PrimaryKeyColumns.Clear();
                    accumulator.PrimaryKeyColumns.AddRange(unique.Columns.Select(static c => c.Column.MultiPartIdentifier.Identifiers.Last().Value));
                    break;
                case UniqueConstraintDefinition unique:
                    ProcessUniqueConstraint(accumulator, unique);
                    break;
                case DefaultConstraintDefinition defaultConstraint:
                    ApplyDefaultConstraint(accumulator, defaultConstraint);
                    break;
                case ForeignKeyConstraintDefinition foreignKey:
                    ProcessForeignKey(accumulator, foreignKey);
                    break;
            }
        }

        private void ProcessUniqueConstraint(DmmTableAccumulator accumulator, UniqueConstraintDefinition constraint)
        {
            var name = constraint.ConstraintIdentifier?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var index = accumulator.GetOrCreateIndex(name);
            index.Reset();
            index.IsUnique = true;
            index.IsDisabled = constraint.IsEnforced.HasValue && !constraint.IsEnforced.Value;

            foreach (var column in constraint.Columns)
            {
                var columnName = column.Column.MultiPartIdentifier.Identifiers.Last().Value;
                var isDescending = column.SortOrder == SortOrder.Descending;
                index.KeyColumns.Add(new DmmIndexColumn(columnName, isDescending));
            }

            ApplyOptions(index, constraint.IndexOptions);
        }

        private void ApplyDefaultConstraint(DmmTableAccumulator accumulator, DefaultConstraintDefinition constraint)
        {
            if (constraint is null)
            {
                return;
            }

            var columnName = constraint.Column?.Value;
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return;
            }

            if (constraint.Expression is not { } expression)
            {
                return;
            }

            var column = accumulator.GetOrCreateColumn(columnName);
            column.DefaultExpression = NormalizeDefaultExpression(Script(expression));
        }

        private void ProcessForeignKey(DmmTableAccumulator accumulator, ForeignKeyConstraintDefinition constraint)
        {
            var name = constraint.ConstraintIdentifier?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var foreignKey = accumulator.GetOrCreateForeignKey(name);
            var column = constraint.Columns.FirstOrDefault();
            var referencedColumn = constraint.ReferencedTableColumns.FirstOrDefault();

            foreignKey.Column = column?.Value ?? string.Empty;
            foreignKey.ReferencedColumn = referencedColumn?.Value ?? string.Empty;

            var referencedTable = constraint.ReferenceTableName;
            foreignKey.ReferencedSchema = referencedTable.SchemaIdentifier?.Value ?? "dbo";
            foreignKey.ReferencedTable = referencedTable.BaseIdentifier.Value;
            foreignKey.DeleteAction = constraint.DeleteAction.ToString();
            if (constraint.IsEnforced.HasValue)
            {
                foreignKey.IsNotTrusted = !constraint.IsEnforced.Value;
            }
        }

        private void ApplyOptions(DmmIndexAccumulator index, IList<IndexOption>? options)
        {
            if (options is null)
            {
                return;
            }

            foreach (var option in options)
            {
                switch (option)
                {
                    case IndexStateOption state:
                        var enabled = state.OptionState == OptionState.On;
                        switch (state.OptionKind)
                        {
                            case IndexOptionKind.PadIndex:
                                index.PadIndex = enabled;
                                break;
                            case IndexOptionKind.IgnoreDupKey:
                                index.IgnoreDuplicateKey = enabled;
                                break;
                            case IndexOptionKind.AllowRowLocks:
                                index.AllowRowLocks = enabled;
                                break;
                            case IndexOptionKind.AllowPageLocks:
                                index.AllowPageLocks = enabled;
                                break;
                            case IndexOptionKind.StatisticsNoRecompute:
                                index.StatisticsNoRecompute = enabled;
                                break;
                        }

                        break;
                    case IndexExpressionOption expression when expression.OptionKind == IndexOptionKind.FillFactor && expression.Expression is IntegerLiteral literal:
                        if (int.TryParse(literal.Value, out var fillFactor))
                        {
                            index.FillFactor = fillFactor;
                        }

                        break;
                }
            }
        }

        private DmmTableAccumulator GetOrCreate(SchemaObjectName name)
        {
            var key = GetKey(name);
            return GetOrCreate(key);
        }

        private DmmTableAccumulator GetOrCreate(string schema, string table)
        {
            var key = $"{schema}.{table}";
            return GetOrCreate(key);
        }

        private DmmTableAccumulator GetOrCreate(string key)
        {
            if (!_tables.TryGetValue(key, out var accumulator))
            {
                var parts = key.Split('.', 2);
                accumulator = new DmmTableAccumulator(parts[0], parts[1]);
                _tables[key] = accumulator;
            }

            return accumulator;
        }

        private string Script(TSqlFragment fragment)
        {
            _generator.GenerateScript(fragment, out var script);
            return script.Trim();
        }

        private static string Canonicalize(string dataType)
        {
            if (string.IsNullOrWhiteSpace(dataType))
            {
                return string.Empty;
            }

            var lower = dataType.Trim().ToLowerInvariant();
            lower = Regex.Replace(lower, "\\s+", " ");
            lower = Regex.Replace(lower, "\\s*\\(\\s*", "(");
            lower = Regex.Replace(lower, "\\s*,\\s*", ",");
            lower = Regex.Replace(lower, "\\s*\\)\\s*", ")");

            if (lower.StartsWith("numeric", StringComparison.Ordinal))
            {
                lower = "decimal" + lower["numeric".Length..];
            }

            return lower;
        }

        private static string? CanonicalizeExpression(string? expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return null;
            }

            var normalized = expression.Trim().ToLowerInvariant();
            normalized = Regex.Replace(normalized, "\\s+", " ");
            return normalized;
        }

        private static string GetKey(SchemaObjectName name)
        {
            var schema = name.SchemaIdentifier?.Value ?? "dbo";
            var table = name.BaseIdentifier.Value;
            return $"{schema}.{table}";
        }

        private static bool IsNullable(ColumnDefinition column)
        {
            var notNull = column.Constraints.OfType<NullableConstraintDefinition>().Any(c => !c.Nullable);
            return !notNull;
        }

        private string? ExtractDefaultExpression(ColumnDefinition column)
        {
            if (column.DefaultConstraint?.Expression is not { } expression)
            {
                return null;
            }

            return NormalizeDefaultExpression(Script(expression));
        }

        private static string? NormalizeDefaultExpression(string? expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return null;
            }

            var trimmed = expression.Trim();
            return Regex.Replace(trimmed, "\\s+", " ");
        }

        private static string? NormalizeCollation(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim();
            if (normalized.Length > 2 && normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal))
            {
                normalized = normalized[1..^1];
            }

            return normalized;
        }

        private static string Unescape(string value) => value.Replace("''", "'");
    }

    private sealed class DmmTableAccumulator
    {
        private readonly Dictionary<string, DmmColumnAccumulator> _columnsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DmmColumnAccumulator> _columnsInOrder = new();
        private readonly Dictionary<string, DmmIndexAccumulator> _indexes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DmmForeignKeyAccumulator> _foreignKeys = new(StringComparer.OrdinalIgnoreCase);

        public DmmTableAccumulator(string schema, string name)
        {
            Schema = schema;
            Name = name;
        }

        public string Schema { get; }

        public string Name { get; }

        public string? Description { get; set; }

        public List<string> PrimaryKeyColumns { get; } = new();

        public DmmColumnAccumulator GetOrCreateColumn(string name)
        {
            if (!_columnsByName.TryGetValue(name, out var column))
            {
                column = new DmmColumnAccumulator(name);
                _columnsByName[name] = column;
                _columnsInOrder.Add(column);
            }

            return column;
        }

        public DmmIndexAccumulator GetOrCreateIndex(string name)
        {
            if (!_indexes.TryGetValue(name, out var index))
            {
                index = new DmmIndexAccumulator(name);
                _indexes[name] = index;
            }

            return index;
        }

        public DmmForeignKeyAccumulator GetOrCreateForeignKey(string name)
        {
            if (!_foreignKeys.TryGetValue(name, out var foreignKey))
            {
                foreignKey = new DmmForeignKeyAccumulator(name);
                _foreignKeys[name] = foreignKey;
            }

            return foreignKey;
        }

        public DmmTable ToTable()
        {
            var columns = _columnsInOrder
                .Select(column => column.ToColumn())
                .ToArray();

            var indexes = _indexes.Values
                .Select(index => index.ToIndex())
                .OrderBy(index => index.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var foreignKeys = _foreignKeys.Values
                .Select(foreignKey => foreignKey.ToForeignKey())
                .OrderBy(fk => fk.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new DmmTable(Schema, Name, columns, PrimaryKeyColumns.ToArray(), indexes, foreignKeys, Description);
        }
    }

    private sealed class DmmColumnAccumulator
    {
        public DmmColumnAccumulator(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string DataType { get; set; } = string.Empty;

        public bool IsNullable { get; set; } = true;

        public string? Description { get; set; }

        public string? DefaultExpression { get; set; }

        public string? Collation { get; set; }

        public DmmColumn ToColumn()
            => new(Name, DataType, IsNullable, DefaultExpression, Collation, Description);
    }

    private sealed class DmmIndexAccumulator
    {
        public DmmIndexAccumulator(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool IsUnique { get; set; }

        public List<DmmIndexColumn> KeyColumns { get; } = new();

        public List<DmmIndexColumn> IncludedColumns { get; } = new();

        public string? FilterDefinition { get; set; }

        public bool IsDisabled { get; set; }

        public bool? PadIndex { get; set; }

        public int? FillFactor { get; set; }

        public bool? IgnoreDuplicateKey { get; set; }

        public bool? AllowRowLocks { get; set; }

        public bool? AllowPageLocks { get; set; }

        public bool? StatisticsNoRecompute { get; set; }

        public void Reset()
        {
            KeyColumns.Clear();
            IncludedColumns.Clear();
            FilterDefinition = null;
            IsUnique = false;
            PadIndex = null;
            FillFactor = null;
            IgnoreDuplicateKey = null;
            AllowRowLocks = null;
            AllowPageLocks = null;
            StatisticsNoRecompute = null;
            IsDisabled = false;
        }

        public DmmIndex ToIndex()
        {
            return new DmmIndex(
                Name,
                IsUnique,
                KeyColumns.ToArray(),
                IncludedColumns.ToArray(),
                FilterDefinition,
                IsDisabled,
                new DmmIndexOptions(PadIndex, FillFactor, IgnoreDuplicateKey, AllowRowLocks, AllowPageLocks, StatisticsNoRecompute));
        }
    }

    private sealed class DmmForeignKeyAccumulator
    {
        public DmmForeignKeyAccumulator(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Column { get; set; } = string.Empty;

        public string ReferencedSchema { get; set; } = "dbo";

        public string ReferencedTable { get; set; } = string.Empty;

        public string ReferencedColumn { get; set; } = string.Empty;

        public string DeleteAction { get; set; } = "NoAction";

        public bool IsNotTrusted { get; set; }

        public DmmForeignKey ToForeignKey()
        {
            return new DmmForeignKey(Name, Column, ReferencedSchema, ReferencedTable, ReferencedColumn, DeleteAction, IsNotTrusted);
        }
    }
}
