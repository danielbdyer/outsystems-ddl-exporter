using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.Dac.Model;
using Permission = Microsoft.SqlServer.Dac.Model.Permission;

namespace DbChange.Io;

public static partial class Ssdt
{
    /// <summary>
    /// The properties Elements leaves out, each holding a password or a secret: SQL Server never returns one, so DacFx makes up a new
    /// value for a login's password each time it loads a model from a database, and a package carries whatever its script wrote. Besides
    /// the passwords and credential secrets: a symmetric key's KEY_SOURCE and IDENTITY_VALUE, from which SQL Server derives the key; a
    /// linked server's provider string (sp_addlinkedserver's @provstr) and an external data source's CONNECTION_OPTIONS, each a connection
    /// string whose documented form carries PWD=, left out whole, so an edit to either is not seen. DacFx 170.5.96's metadata marks none of
    /// them as secret, so the list is kept here; Io.Tests' ModelElementsTests plants each, and lists every other string-typed property
    /// DacFx declares with the reason it is not a secret. DacFx fills these static fields when its model schema initializes, which the
    /// first TSqlModel a process makes does, so the list is made on first use, after one.
    /// </summary>
    internal static IReadOnlySet<ModelPropertyClass> Secrets => Secret.Value;

    private static readonly Lazy<HashSet<ModelPropertyClass>> Secret = new(() =>
    {
        new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions()).Dispose();
        return
        [
            Login.Password, User.Password, ApplicationRole.Password, MasterKey.Password, AsymmetricKey.Password,
            SymmetricKeyPassword.Password,
            Certificate.EncryptionPassword, Certificate.PrivateKeyDecryptionPassword, Certificate.PrivateKeyEncryptionPassword,
            Credential.Secret, DatabaseCredential.Secret, LinkedServerLogin.LinkedServerPassword, SignatureEncryptionMechanism.Password,
            SymmetricKey.KeySource, SymmetricKey.IdentityValue, LinkedServer.ProviderString, ExternalDataSource.ConnectionOptions,
        ];
    });

    /// <summary>
    /// A model read whole into elements (§2.1 rule 1); the renames its package's refactorlog records, as Change.Between takes them, which a
    /// database's model has none of; and the errors DacFx found loading the model (TSqlModel.GetModelErrors: a script it cannot parse),
    /// each a note and never a refusal, since the rest of the model reads.
    /// </summary>
    public sealed record ModelElements(SortedArray<Element> Elements, SortedArray<Rename> Renames,
        SortedArray<DacFxMessage> Errors = default)
    {
        /// <summary>A model.error note for each error, naming the model's source.</summary>
        public IEnumerable<Finding> Notes(string source) => Errors.Select(error => Finding.Note("model.error", source,
            "DacFx found " + error + " in the model of " + source
                + "; the rest of the model reads whole, and the object the error concerns may lack what DacFx could not read."));
    }

    /// <summary>
    /// The collation a model's names compare under (decision 2.26): its DatabaseOptions element's Collation property, which Elements reads
    /// from a package as its project's default collation and from a database as the database's; the comparison that follows no database
    /// when the model has none.
    /// </summary>
    public static Result<Collation> CollationOf(SortedArray<Element> elements) =>
        elements.FirstOrDefault(e => e.Key.Type == "DatabaseOptions")?["Collation"] is Value.Text { Content: var name } ? Collation.Of(name)
            : Result.Ok(Collation.CaseSensitive);

    /// <summary>
    /// The package's model read into elements, with its deploy scripts and its refactorlog's operations, as
    /// <see cref="ReadModel(TSqlModel, string?, string?, IReadOnlyList{RefactorLogOperation})"/> reads them.
    /// </summary>
    public static Result<ModelElements> ReadModel(Package package) =>
        ReadModel(package.Model, package.PreDeploy, package.PostDeploy, package.Refactors);

    /// <summary>
    /// A model read into elements, one element for each deploy script and each refactorlog operation, and the operations' renames. An
    /// operation's element type, written as DacFx serializes it (SqlSimpleColumn), is read through the one map of DacFx's types
    /// (ModelTypes), so a type the model no longer holds still keys, and the rename to it is a drop and an add.
    /// </summary>
    internal static Result<ModelElements> ReadModel(TSqlModel model, string? preDeploy, string? postDeploy,
        IReadOnlyList<RefactorLogOperation> refactors) => ModelObjects(model).Bind(objects =>
    {
        var scripts = new[] { preDeploy is { } pre ? Element.PreDeploy(LineEndings.Lf(pre)) : null,
            postDeploy is { } post ? Element.PostDeploy(LineEndings.Lf(post)) : null }.OfType<Element>();
        return Result.All(refactors.Select(Entry)).Bind(entries =>
            Result.All(refactors.Where(r => r.Kind != RefactorOperationKind.Other).Select(Renaming))
                .Map(renames => new ModelElements(SortedArray.Of(objects.Select(o => o.Element).Concat(scripts).Concat(entries)),
                    SortedArray.Of(renames), Errors(model))));
    });

    /// <summary>
    /// The errors DacFx found loading a model (TSqlModel.GetModelErrors): measured on DacFx 170.5.96, a script it cannot parse (SQL46010);
    /// an unresolved reference, such as a user whose login is gone, is Validate's to find, which is not called, and a database whose user
    /// lost its login extracts that user with no login.
    /// </summary>
    private static SortedArray<DacFxMessage> Errors(TSqlModel model) => SortedArray.Of(model.GetModelErrors().Select(e =>
        new DacFxMessage(e.Severity == ModelErrorSeverity.Warning ? DacFxMessageType.Warning : DacFxMessageType.Error, e.Prefix,
            e.ErrorCode, e.Message, null)));

    /// <summary>
    /// A model read whole, no code per type (§1 fact 6): each user-defined top-level object but the two grants to public SQL Server
    /// makes in every new database (<see cref="Default"/>) and, depth first, what its composing relationships reach, each object
    /// once, with every property its type declares that the model's platform carries (a Sql110 model's column has no GraphType) but a
    /// password or a secret (<see cref="Secrets"/>), a module's Definition as
    /// written too, and every relationship's targets in DacFx's order; a target's own property (an index column's Ascending) is
    /// Relationship[position].Property. A key is the name while it has one or two parts and nothing
    /// composes the object; else the parent's key (the composer, or the hierarchical parent: an index's table, a grant's securable)
    /// and the name parts the parent's name does not hold. An unnamed default, check, unique or foreign key constraint on exactly one
    /// column is keyed under that column by the relationship that names it (TargetColumn, ExpressionDependencies, Columns), so it
    /// moves with the column's rename, and a column added beside it, or a table whose columns a database holds in another order
    /// (a publish under IgnoreColumnOrder appends a column the project inserts), leaves its key as it was. Any other unnamed object
    /// (a primary key, a constraint on several columns) is keyed by the relationship to its table. Several unnamed objects of one
    /// type under one parent are numbered from 1 in the order of the names they reference, then of their own values, never by a
    /// generated name or DacFx's order; a package and the database it was published to hold the same names, so they number alike,
    /// and a rename of a column that a constraint on several columns references can renumber that constraint and its siblings. SQL
    /// Server normalizes a check's text, so two checks on one column may number apart in a package and its database. Two objects
    /// keyed alike are the error model.duplicate-key; an unresolved reference is keyed as the type Unresolved. Models are compared only
    /// between like sources and, for databases, like identities: SQL Server shows a server-scoped login only to a reader with permission on
    /// it (sysadmin, VIEW ANY DEFINITION, or its own), and a db_datareader login holding VIEW DEFINITION read Query Store's database
    /// options differently from sa when measured on 2026-09-24.
    /// </summary>
    internal static Result<SortedArray<Element>> ReadModel(TSqlModel model) =>
        ModelObjects(model).Map(objects => SortedArray.Of(objects.Select(o => o.Element)));

    /// <summary>
    /// A grant SQL Server makes in every new database, copying it from model: VIEW ANY COLUMN ENCRYPTION KEY DEFINITION and VIEW ANY
    /// COLUMN MASTER KEY DEFINITION to public, which Always Encrypted's client drivers read. A database holds both whatever its project
    /// says, and a project imported from a database may hold them too, so Elements leaves both out of every model; a REVOKE of either
    /// is therefore not seen.
    /// </summary>
    private static bool Default(TSqlObject o) => o.ObjectType == Permission.TypeClass
        && o.GetProperty<PermissionAction>(Permission.PermissionAction) == PermissionAction.Grant
        && !o.GetProperty<bool>(Permission.WithGrantOption)
        && o.GetProperty<PermissionType>(Permission.PermissionType) is PermissionType.ViewAnyColumnEncryptionKeyDefinition
            or PermissionType.ViewAnyColumnMasterKeyDefinition
        && o.GetReferenced(Permission.Grantee, DacQueryScopes.All).ToArray() is [var grantee] && grantee.Name.Parts is [var role]
        && string.Equals(role, "public", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Each object of the model as an element, with the object's name as model.xml writes it ([dbo].[Customer].[Email]), null for an
    /// unnamed object.
    /// </summary>
    private static Result<IReadOnlyList<(Element Element, string? Name)>> ModelObjects(TSqlModel model)
    {
        var composers = new Dictionary<TSqlObject, (TSqlObject Parent, string Relationship)>();
        var reached = new HashSet<TSqlObject>();
        void Descend(TSqlObject o)
        {
            var composed = o.ObjectType.Relationships.Where(r => r.Type == RelationshipType.Composing)
                .SelectMany(r => o.GetReferenced(r, DacQueryScopes.All).Select(c => (r, c)));
            foreach (var (r, child) in reached.Add(o) ? composed : [])
            {
                composers[child] = (o, r.Name);
                Descend(child);
            }
        }

        model.GetObjects(DacQueryScopes.UserDefined).Where(o => !Default(o)).ToList().ForEach(Descend);

        // The relationship through which each type of unnamed constraint that can sit on one column names its columns.
        var on = new Dictionary<ModelTypeClass, ModelRelationshipClass>
        {
            [DefaultConstraint.TypeClass] = DefaultConstraint.TargetColumn,
            [CheckConstraint.TypeClass] = CheckConstraint.ExpressionDependencies,
            [UniqueConstraint.TypeClass] = UniqueConstraint.Columns, [ForeignKeyConstraint.TypeClass] = ForeignKeyConstraint.Columns,
        };
        (TSqlObject? Parent, string Relationship) Anchor(TSqlObject o) => composers.TryGetValue(o, out var composer) ? composer
            : !o.Name.HasName && on.TryGetValue(o.ObjectType, out var through)
                && o.GetReferenced(through, DacQueryScopes.All).Where(c => c.ObjectType == Column.TypeClass).Distinct()
                    .ToArray() is [var column]
                ? (column, through.Name)
            : o.GetParent(DacQueryScopes.All) is not { } parent ? (null, o.ObjectType.Name)
            : (parent, o.ObjectType.Relationships.FirstOrDefault(r =>
                r.Type == RelationshipType.Hierarchical
                && o.GetReferenced(r, DacQueryScopes.All).Contains(parent))?.Name ?? o.ObjectType.Name);

        string References(TSqlObject o) => string.Join('\n', o.ObjectType.Relationships.Where(r => r.Type != RelationshipType.Composing)
            .SelectMany(r => o.GetReferencedRelationshipInstances(r, DacExternalQueryScopes.All).Select(i => r.Name + " " + i.ObjectName)));
        var secrets = Secrets;

        // A property the model's platform does not carry reads as DacFx's default (measured: a Sql110 column's GraphType 0 and IsHidden
        // false; four of DatabaseOptions' on Sql160), a value SQL Server never held, so it is not read.
        var platform = Enum.TryParse<TSqlPlatforms>(model.Version.ToString(), out var carried) ? carried : TSqlPlatforms.All;
        IEnumerable<ModelPropertyClass> Kept(IEnumerable<ModelPropertyClass> declared) =>
            declared.Where(p => !secrets.Contains(p) && (p.SupportedPlatforms & platform) != 0);
        string Values(TSqlObject o) =>
            string.Join('\n', Kept(o.ObjectType.Properties).Select(p => p.Name + " " + ValueOf(() => o.GetProperty(p), p.DataType)));
        var unnamed = reached.Where(o => !o.Name.HasName).GroupBy(o => (Anchor: Anchor(o), Type: o.ObjectType.Name))
            .SelectMany(g => g.OrderBy(References, StringComparer.Ordinal).ThenBy(Values, StringComparer.Ordinal)
                .Select((o, i) => (Object: o,
                    Name: g.Count() == 1 ? g.Key.Anchor.Relationship
                    : string.Create(CultureInfo.InvariantCulture, $"{g.Key.Anchor.Relationship} {i + 1}"))))
            .ToDictionary(u => u.Object, u => u.Name);

        var keys = new Dictionary<TSqlObject, Result<ElementKey>>();
        Result<ElementKey> Key(TSqlObject o) => keys.TryGetValue(o, out var key) ? key : keys[o] = KeyOf(o);
        Result<ElementKey> KeyOf(TSqlObject o)
        {
            string[] own = o.Name.HasName ? [.. o.Name.Parts] : [unnamed.GetValueOrDefault(o) ?? Anchor(o).Relationship];
            var parent = o.Name.HasName && own.Length <= 2 && !composers.ContainsKey(o) ? null : Anchor(o).Parent;
            return parent is null ? ModelTypes.Key(o.ObjectType.Name, own, null)
                : Key(parent).Bind(home => ModelTypes.Key(o.ObjectType.Name, ModelTypes.Beneath(own, [.. parent.Name.Parts]), home));
        }

        Result<Element> ElementOf(TSqlObject o)
        {
            var relationships = o.ObjectType.Relationships.Select(r =>
                (Class: r, Instances: o.GetReferencedRelationshipInstances(r, DacExternalQueryScopes.All).ToArray())).ToArray();
            var properties = Kept(o.ObjectType.Properties).Select(p => (p.Name, Value: ValueOf(() => o.GetProperty(p), p.DataType)))
                .Append((Name: "Definition",
                    Value: Module(o.ObjectType) ? ScriptOf(() => o.TryGetScript(out var script) ? script : null) : null))
                .Concat(relationships.SelectMany(r => r.Instances.SelectMany((i, n) => Kept(r.Class.Properties).Select(p =>
                    (Name: string.Create(CultureInfo.InvariantCulture, $"{r.Class.Name}[{n}].{p.Name}"),
                        Value: ValueOf(() => i.GetProperty(p), p.DataType))))))
                .Where(p => p.Value is not null).Select(p => new Element.Property(p.Name, p.Value!));
            var targets = relationships.Select(r => Result.All(r.Instances.Select(i => i.Object is { } target ? Key(target)
                    : ModelTypes.Key("Unresolved", [.. i.ObjectName.ExternalParts ?? [], .. i.ObjectName.Parts], null)))
                .Map(to => Element.Relationship.Of(r.Class.Name, to)));
            return Key(o).Bind(key => Result.All(targets).Bind(rs => Element.Of(key, properties, rs)));
        }

        return Result.All(reached.Select(o => ElementOf(o).Map(e => (Element: e,
                Name: o.Name.HasName ? ModelTypes.Key(o.ObjectType.Name, [.. o.Name.Parts], null).Match<string?>(k => k.Path, _ => null)
                    : null))))
            .Bind(elements => elements.GroupBy(e => e.Element.Key).FirstOrDefault(g => g.Count() > 1) is not { } alike ? Result.Ok(elements)
                : new Error("model.duplicate-key",
                    $"{alike.Count()} {alike.Key.Type} objects of the model are keyed alike, as {alike.Key}: "
                        + $"{string.Join(", ", alike.Select(e => e.Name ?? "unnamed"))}.",
                    "Report the model's source with this error: a key names one object, "
                        + "so io/Ssdt.ReadModel keys this type ambiguously, a defect in dbchange."));
    }

    /// <summary>
    /// An object's property as the kernel's closed Value, or null where DacFx cannot read it, and Elements skips it. A property DacFx
    /// declares as its SqlScriptProperty (a default's or a check's expression, a computed column's, an extended property's value) is
    /// T-SQL text and reads as a Value.Script, which the printer alone prints (decision 2.27). An enumeration reaches an untyped read
    /// as its integer, so the declared type names its member; any other type (a double, as a spatial index's bounds) is its
    /// invariant string, so no value is dropped for its type. Text has CRLF and a lone CR made LF.
    /// </summary>
    internal static Value? ValueOf(TSqlObject o, ModelPropertyClass property) => ValueOf(() => o.GetProperty(property), property.DataType);

    /// <summary>
    /// A module's script (a procedure's, a function's, a trigger's body) as a Value.Script, or null where DacFx gives none.
    /// </summary>
    private static Value? ScriptOf(Func<string?> read) => read() is { } script ? new Value.Script(LineEndings.Lf(script)) : null;

    private static Value? ValueOf(Func<object?> read, Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        try
        {
            var value = read();
            return value switch
            {
                null => new Value.Null(),
                bool b => new Value.Boolean(b),
                string s when type.Name == "SqlScriptProperty" => new Value.Script(LineEndings.Lf(s)),
                string s => new Value.Text(LineEndings.Lf(s)),
                Enum or sbyte or byte or short or ushort or int or uint or long when type.IsEnum =>
                    new Value.Enumeration(type.Name, Enum.Format(type, Enum.ToObject(type, value), "G")),
                sbyte or byte or short or ushort or int or uint or long =>
                    new Value.Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                IFormattable f => new Value.Text(f.ToString(null, CultureInfo.InvariantCulture)),
                _ => new Value.Text(LineEndings.Lf(value.ToString() ?? "")),
            };
        }
        catch (DacModelException)
        {
            return null;
        }
    }

    /// <summary>
    /// A module, whose body DacFx reads for BodyDependencies and holds in no property of its script type: a procedure, a function, a
    /// trigger (a view's is SelectStatement).
    /// </summary>
    private static bool Module(ModelTypeClass type) =>
        type.Relationships.Any(r => r.Name == "BodyDependencies") && type.Properties.All(p => p.DataType.Name != "SqlScriptProperty");
}
