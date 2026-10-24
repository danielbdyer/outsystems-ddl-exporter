using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Dac.Model;

namespace DbChangeLite;

public sealed record TableShape(string Schema, string Name, IReadOnlyDictionary<string, bool> Nullable, bool HasIdentity)
{
    public string Quoted => Names.Quote(Schema, Name);
}

public sealed class Model
{
    private static readonly ModelTypeClass[] Listed =
    [
        Microsoft.SqlServer.Dac.Model.Table.TypeClass, View.TypeClass, Procedure.TypeClass, ScalarFunction.TypeClass, TableValuedFunction.TypeClass, Microsoft.SqlServer.Dac.Model.Index.TypeClass, Sequence.TypeClass,
        Synonym.TypeClass, DmlTrigger.TypeClass, Schema.TypeClass,
    ];

    private Model(IReadOnlyDictionary<string, TableShape> tables, IReadOnlyList<(string Type, string Name)> objects, IReadOnlyList<(string Table, string Name, string Expression)> checkConstraints,
        IReadOnlySet<string> defaulted, string collation)
    {
        Tables = tables;
        Objects = objects;
        CheckConstraints = checkConstraints;
        Defaulted = defaulted;
        Collation = collation;
    }

    public string Collation { get; }

    public IReadOnlySet<string> Defaulted { get; }

    public IReadOnlyDictionary<string, TableShape> Tables { get; }

    public IReadOnlyList<(string Type, string Name)> Objects { get; }

    public IReadOnlyList<(string Table, string Name, string Expression)> CheckConstraints { get; }

    public static Model Load(string dacpac)
    {
        using var model = DacFx.Guard(() => TSqlModel.LoadFromDacpac(dacpac, new ModelLoadOptions { LoadAsScriptBackedModel = true }), "package.unreadable", dacpac);
        var tables = new Dictionary<string, TableShape>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in model.GetObjects(DacQueryScopes.UserDefined, Microsoft.SqlServer.Dac.Model.Table.TypeClass))
        {
            var columns = table.GetReferenced(Microsoft.SqlServer.Dac.Model.Table.Columns).ToList();
            var nullable = columns.ToDictionary(c => c.Name.Parts[^1], c => c.GetProperty<bool>(Column.Nullable), StringComparer.OrdinalIgnoreCase);
            var shape = new TableShape(table.Name.Parts[0], table.Name.Parts[1], nullable, columns.Any(c => c.GetProperty<bool>(Column.IsIdentity)));
            tables[shape.Quoted] = shape;
        }

        var objects = Listed.SelectMany(type => model.GetObjects(DacQueryScopes.UserDefined, type).Select(o => (type.Name, Names.Quote(o.Name.Parts))))
            .OrderBy(o => o.Item1, StringComparer.Ordinal).ThenBy(o => o.Item2, StringComparer.Ordinal).ToList();
        var collation = model.GetObjects(DacQueryScopes.All, DatabaseOptions.TypeClass).Select(o => o.GetProperty(DatabaseOptions.Collation) as string).FirstOrDefault(c => c is { Length: > 0 });
        var checks = model.GetObjects(DacQueryScopes.UserDefined, CheckConstraint.TypeClass)
            .Select(c => (Names.Quote(c.GetReferenced(CheckConstraint.Host).First().Name.Parts), Names.Quote(c.Name.Parts), c.GetProperty(CheckConstraint.Expression) as string ?? ""))
            .ToList();
        var defaulted = model.GetObjects(DacQueryScopes.UserDefined, DefaultConstraint.TypeClass)
            .SelectMany(d => d.GetReferenced(DefaultConstraint.TargetColumn).Select(c => Names.Quote(c.Name.Parts)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new Model(tables, objects, checks, defaulted, collation ?? "");
    }

    public TableShape? Shape(string schema, string name) => Tables.GetValueOrDefault(Names.Quote(schema, name));

    public bool? ColumnNullable(string schema, string table, string column) =>
        Shape(schema, table) is { } shape && shape.Nullable.TryGetValue(column, out var nullable) ? nullable : null;
}

public static class Names
{
    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public static string Quote(string schema, string name) => Quote(schema) + "." + Quote(name);

    public static string Quote(IEnumerable<string> parts) => string.Join('.', parts.Select(Quote));

    public static string Literal(string text) => "N'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    public static IReadOnlyList<string> Parts(string name) => new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true).ParseSchemaObjectName(new System.IO.StringReader(name), out var errors) is { } parsed && errors.Count == 0
        ? parsed.Identifiers.Select(i => i.Value).ToList() : name.Split('.').Select(p => p.Trim('[', ']')).ToList();
}
