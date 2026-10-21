using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// The repository's objects that reach another server (DECISIONS.md, 2026-10-21). A declared block runs as one batch on the copy's
/// connection, with triggers firing, so a view, a function or a procedure of the repository that reads or writes a linked server
/// reaches it for the block that names it, and a DML trigger that does reaches it for the block that writes its table or view. A view, a
/// scalar or table-valued function, a procedure or a DML trigger of the base's or the change's model, or one a pre- or post-deploy
/// script creates, reaches another server when its script holds a name with a server part, OPENQUERY, OPENROWSET, OPENDATASOURCE or
/// EXECUTE ... AT; when it is bound to an assembly (AS EXTERNAL NAME, or a function, procedure or trigger of the model with no T-SQL
/// body), since an EXTERNAL_ACCESS or UNSAFE assembly can open any connection; when TSql160Parser does not read a model module's
/// script, each SQLCMD variable read as a name, since nothing then shows it reaches none; or when it names an object that reaches one, a
/// synonym of the models for a name on another server among them, or writes a table or a view whose trigger does, followed to a fixed
/// point. Names compare as Outside compares a block's: ignoring case, one in one part matching an object of any schema. A deploy script
/// TSql160Parser does not read gives no module.
/// </summary>
public static partial class Witnesses
{
    /// <summary>A SQLCMD variable, $(Name), which a script may write for a linked server or a database, read as a name of its length.
    /// </summary>
    private static readonly Regex Variable = new(@"\$\(" + SqlCmdVariable.NamePattern + @"\)", RegexOptions.CultureInvariant);

    /// <summary>An object of the repository that reaches another server: its key, and for a trigger the table or the view it fires
    /// on.</summary>
    public sealed record Remote(ElementKey Key, Name? FiresOn);

    /// <summary>
    /// A module of the models as its script reads: its key; for a trigger, the table or view it fires on; whether the script reaches
    /// another server itself; each object it names; and each table or view it writes.
    /// </summary>
    private sealed record Module(ElementKey Key, Name? FiresOn, bool Direct, IReadOnlyList<(string? Schema, string Name)> Names,
        IReadOnlyList<(string? Schema, string Name)> Writes);

    /// <summary>The objects of the models, the modules their deploy scripts create, and the synonyms their scripts create for a name on
    /// another server, that reach another server, as the class says, in the order of their keys.</summary>
    private static IReadOnlyList<Remote> RemotesOf(IReadOnlyList<Element> elements, IEnumerable<Name> linkedSynonyms)
    {
        var modules = elements.Select(ModuleOf).OfType<Module>().Concat(ScriptedOf(elements))
            .Concat(linkedSynonyms.Select(s => ElementKey.Of("Synonym", s))
                .OfType<Result<ElementKey>.Ok>().Select(k => new Module(k.Value, null, true, [], [])))
            .ToList();
        var remote = modules.Where(m => m.Direct).ToHashSet();
        for (var reached = Reached(modules, remote); reached.Count > 0; reached = Reached(modules, remote))
        {
            remote.UnionWith(reached);
        }

        return [.. remote.Select(m => new Remote(m.Key, m.FiresOn)).Distinct().OrderBy(r => r.Key)];
    }

    /// <summary>The modules not yet held to reach another server that name an object that does, or write a table or a view whose trigger
    /// does.</summary>
    private static List<Module> Reached(IReadOnlyList<Module> modules, HashSet<Module> remote) =>
        [.. modules.Where(m => !remote.Contains(m)
            && (m.Names.Any(n => remote.Any(r => r.FiresOn is null && Is(r.Key.Name, n.Schema, n.Name)))
                || m.Writes.Any(w => remote.Any(r => r.FiresOn is { } table && Is(table, w.Schema, w.Name)))))];

    /// <summary>
    /// A view's, a function's, a procedure's or a DML trigger's script as read, or a synonym for a name with a server part, or one
    /// whose name does not parse; null for any other element. A script TSql160Parser does not read reaches another server, and so does a
    /// function, a procedure or a trigger with no script, which an assembly binds.
    /// </summary>
    private static Module? ModuleOf(Element element)
    {
        string? Text(string property) => element.Properties.FirstOrDefault(p => p.Name == property)?.Value switch
        {
            Value.Script script => script.Content,
            Value.Text text => text.Content,
            _ => null,
        };
        var key = element.Key;
        if (key.Type == "Synonym")
        {
            return Text("ForObjectName") is { } target && TSql.NameParts(target) is not { Length: < 4 }
                ? new Module(key, null, true, [], []) : null;
        }

        var script = key.Type switch
        {
            "View" => Text("SelectStatement"),
            "ScalarFunction" or "TableValuedFunction" or "Procedure" or "DmlTrigger" => Text("Definition"),
            _ => null,
        };
        var firesOn = key.Type == "DmlTrigger"
            ? element.Relationships.FirstOrDefault(r => r.Name == "TriggerObject")?.Targets.FirstOrDefault()?.Key.Name : null;
        if (script is null)
        {
            return key.Type is "ScalarFunction" or "TableValuedFunction" or "Procedure" or "DmlTrigger"
                ? new Module(key, firesOn, true, [], []) : null;
        }
        var tree = TSql.Parse(Variable.Replace(script, m => new string('x', m.Length)), out var errors);
        var reach = new Reach();
        if (errors.Count == 0)
        {
            tree.Accept(reach);
        }

        return new Module(key, firesOn, errors.Count > 0 || reach.Direct, reach.Names, reach.Writes);
    }

    /// <summary>
    /// The views, functions, procedures and DML triggers the pre- and post-deploy scripts create, by CREATE, ALTER or CREATE OR ALTER,
    /// each read as <see cref="ModuleOf"/> reads a module of the model, its name in dbo where it names no schema; none from a script
    /// TSql160Parser does not read once its SQLCMD directives are dropped and each variable is read as a name of its length.
    /// </summary>
    private static IEnumerable<Module> ScriptedOf(IEnumerable<Element> elements) =>
        elements.Where(e => e.Key.Type is Element.PreDeploymentScript or Element.PostDeploymentScript)
            .Select(e => e["Text"] is Value.Script { Content: var text } ? text : null).OfType<string>()
            .SelectMany(text =>
            {
                var tree = TSql.Parse(Variable.Replace(SqlCmdVariable.WithoutDirectives(LineEndings.Lf(text), keepLines: true),
                    m => new string('x', m.Length)), out var errors);
                var scripted = new Scripted();
                if (errors.Count == 0)
                {
                    tree.Accept(scripted);
                }

                return scripted.Modules;
            });

    /// <summary>The table or view a statement writes, as it names it: in two parts, or in one, as the alias of a table its FROM names or
    /// as its own name; none for a target of another kind, a rowset function or a table variable.</summary>
    private static IEnumerable<(string? Schema, string Name)> Written(TableReference? target, FromClause? from) =>
        target is NamedTableReference { SchemaObject: { BaseIdentifier.Value: var name } named }
            ? [named.SchemaIdentifier is null && Aliased(from?.TableReferences ?? [], name) is { BaseIdentifier.Value: var table } aliased
                ? (aliased.SchemaIdentifier?.Value, table) : (named.SchemaIdentifier?.Value, name)]
            : [];

    /// <summary>Whether a name a script gives, in one part or two, names the object given, as a case-insensitive database resolves it.
    /// </summary>
    private static bool Is(Name named, string? schema, string? name) =>
        Same(named.Base, name) && (schema is null || Same(named.Schema, schema));

    /// <summary>What a module's script reaches and names, as the class says.</summary>
    private sealed class Reach : TSqlFragmentVisitor
    {
        public bool Direct { get; private set; }

        public List<(string? Schema, string Name)> Names { get; } = [];

        public List<(string? Schema, string Name)> Writes { get; } = [];

        public override void Visit(SchemaObjectName node)
        {
            Direct |= node.ServerIdentifier is not null;
            if (node.BaseIdentifier?.Value is { } name)
            {
                Names.Add((node.SchemaIdentifier?.Value, name));
            }
        }

        public override void Visit(FunctionCall node)
        {
            if (node.CallTarget is MultiPartIdentifierCallTarget { MultiPartIdentifier.Identifiers: [var schema] })
            {
                Names.Add((schema.Value, node.FunctionName.Value));
            }
        }

        public override void Visit(OpenQueryTableReference node) => Direct = true;

        public override void Visit(OpenRowsetTableReference node) => Direct = true;

        public override void Visit(BulkOpenRowset node) => Direct = true;

        public override void Visit(AdHocDataSource node) => Direct = true;

        public override void Visit(ExecuteSpecification node) => Direct |= node.LinkedServer is not null;

        public override void Visit(MethodSpecifier node) => Direct = true;

        public override void Visit(InsertSpecification node) => Writes.AddRange(Written(node.Target, null));

        public override void Visit(UpdateSpecification node) => Writes.AddRange(Written(node.Target, node.FromClause));

        public override void Visit(DeleteSpecification node) => Writes.AddRange(Written(node.Target, node.FromClause));

        public override void Visit(MergeSpecification node) => Writes.AddRange(Written(node.Target, null));
    }

    /// <summary>The modules a deploy script creates, as <see cref="ScriptedOf"/> says.</summary>
    private sealed class Scripted : TSqlFragmentVisitor
    {
        public List<Module> Modules { get; } = [];

        public override void Visit(ViewStatementBody node) => Add("View", node.SchemaObjectName, null, node);

        public override void Visit(ProcedureStatementBody node) => Add("Procedure", node.ProcedureReference.Name, null, node);

        public override void Visit(FunctionStatementBody node) =>
            Add(node.ReturnType is ScalarFunctionReturnType ? "ScalarFunction" : "TableValuedFunction", node.Name, null, node);

        public override void Visit(TriggerStatementBody node)
        {
            if (node.TriggerObject.TriggerScope == TriggerScope.Normal && Named(node.TriggerObject.Name) is { } table)
            {
                Add("DmlTrigger", node.Name, table, node);
            }
        }

        private static Name? Named(SchemaObjectName name) =>
            Name.Of(name.SchemaIdentifier?.Value ?? "dbo", name.BaseIdentifier.Value) is Result<Name>.Ok(var named) ? named : null;

        private void Add(string type, SchemaObjectName name, Name? firesOn, TSqlFragment statement)
        {
            if (Named(name) is { } named && ElementKey.Of(type, named) is Result<ElementKey>.Ok(var key))
            {
                var reach = new Reach();
                statement.Accept(reach);
                Modules.Add(new Module(key, firesOn, reach.Direct, reach.Names, reach.Writes));
            }
        }
    }
}
