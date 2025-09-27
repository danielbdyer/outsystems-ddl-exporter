using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Domain.Abstractions;

namespace Osm.Dmm;

public sealed class DmmParser
{
    public Result<IReadOnlyList<DmmTable>> Parse(TextReader reader)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        var parser = new TSql150Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(reader, out var errors);
        if (errors is { Count: > 0 })
        {
            var message = string.Join(Environment.NewLine, errors.Select(e => $"{e.Line}:{e.Column} {e.Message}"));
            return Result<IReadOnlyList<DmmTable>>.Failure(ValidationError.Create("dmm.parse.failed", message));
        }

        var builder = new TableModelBuilder();
        fragment.Accept(builder);
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

        public override void Visit(CreateTableStatement node)
        {
            var key = GetKey(node.SchemaObjectName);
            var accumulator = GetOrCreate(key);

            foreach (var column in node.Definition.ColumnDefinitions)
            {
                accumulator.Columns.Add(new DmmColumn(
                    column.ColumnIdentifier.Value,
                    Script(column.DataType),
                    IsNullable(column)));
            }

            foreach (var constraint in node.Definition.TableConstraints.OfType<UniqueConstraintDefinition>())
            {
                if (constraint.IsPrimaryKey)
                {
                    accumulator.PrimaryKeyColumns.Clear();
                    accumulator.PrimaryKeyColumns.AddRange(constraint.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers.Last().Value));
                }
            }
        }

        public override void Visit(AlterTableAddTableElementStatement node)
        {
            var key = GetKey(node.SchemaObjectName);
            var accumulator = GetOrCreate(key);
            foreach (var constraint in node.Definition.TableConstraints.OfType<UniqueConstraintDefinition>())
            {
                if (constraint.IsPrimaryKey)
                {
                    accumulator.PrimaryKeyColumns.Clear();
                    accumulator.PrimaryKeyColumns.AddRange(constraint.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers.Last().Value));
                }
            }
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

        private string Script(DataTypeReference dataType)
        {
            _generator.GenerateScript(dataType, out var script);
            return script.Trim();
        }
    }

    private sealed class DmmTableAccumulator
    {
        public DmmTableAccumulator(string schema, string name)
        {
            Schema = schema;
            Name = name;
        }

        public string Schema { get; }

        public string Name { get; }

        public List<DmmColumn> Columns { get; } = new();

        public List<string> PrimaryKeyColumns { get; } = new();

        public DmmTable ToTable()
        {
            return new DmmTable(Schema, Name, Columns, PrimaryKeyColumns);
        }
    }
}
