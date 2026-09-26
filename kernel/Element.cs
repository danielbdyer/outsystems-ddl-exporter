using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Estate.Kernel;

/// <summary>
/// A property's value as io/Ssdt.ReadModel reads it from DacFx: a boolean, an integer, a string, an enumeration's member with
/// its enumeration type, T-SQL text DacFx holds as a script (a module's definition, a default's or a check's expression, a deploy
/// script), or null. The cases are closed. A script is kept as written, so a change confined to a password literal is a change
/// (law 3′); the value a password form sets is left out where text leaves the tool, never here (decision 2.27). Equality and order
/// are ordinal and culture-free: null first, then booleans, integers, strings, enumerations and scripts, each in its own order.
/// </summary>
public abstract record Value : IComparable<Value>
{
    private Value()
    {
    }

    public T Match<T>(Func<bool, T> boolean, Func<long, T> integer, Func<string, T> text, Func<string, string, T> enumeration, Func<string, T> script, Func<T> none) =>
        this switch
        {
            Boolean b => boolean(b.IsTrue),
            Integer i => integer(i.Number),
            Text t => text(t.Content),
            Enumeration e => enumeration(e.Type, e.Member),
            Script s => script(s.Content),
            Null => none(),
            _ => throw new UnreachableException(),
        };

    public int CompareTo(Value? other) => (this, other) switch
    {
        (_, null) => 1,
        (Boolean a, Boolean b) => a.IsTrue.CompareTo(b.IsTrue),
        (Integer a, Integer b) => a.Number.CompareTo(b.Number),
        (Text a, Text b) => string.CompareOrdinal(a.Content, b.Content),
        (Enumeration a, Enumeration b) => string.CompareOrdinal(a.Type, b.Type) is var t and not 0 ? t : string.CompareOrdinal(a.Member, b.Member),
        (Script a, Script b) => string.CompareOrdinal(a.Content, b.Content),
        _ => Rank(this).CompareTo(Rank(other)),
    };

    /// <summary>Culture-free: <c>true</c>, <c>-12</c>, <c>'it''s'</c>, <c>SqlDataType.NVarChar</c>, a script as its text quoted, <c>NULL</c>.</summary>
    public sealed override string ToString() => Match(
        b => b ? "true" : "false",
        n => n.ToString(CultureInfo.InvariantCulture),
        s => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'",
        (type, member) => type + "." + member,
        s => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'",
        () => "NULL");

    private static int Rank(Value value) => value.Match(_ => 1, _ => 2, _ => 3, (_, _) => 4, _ => 5, () => 0);

    public sealed record Boolean(bool IsTrue) : Value;

    public sealed record Integer(long Number) : Value;

    public sealed record Text(string Content) : Value;

    public sealed record Enumeration(string Type, string Member) : Value;

    /// <summary>T-SQL text DacFx holds as a script, as written: compared exactly, and printed through the password printer alone.</summary>
    public sealed record Script(string Content) : Value;

    public sealed record Null : Value;
}

/// <summary>
/// An element's identity: its DacFx type name and its name path. A top-level object's path is its one- or two-part
/// Name; a composed child's is its parent's key and its own one-part Name, so a column is keyed under its table and
/// renders as <c>Column [dbo].[Customer].[Email]</c>. Keys sort by path a level at a time (name, then type), so an
/// object sorts just before the objects keyed under it. No property has a setter, so a key stays valid.
/// </summary>
public sealed record ElementKey : IComparable<ElementKey>
{
    private ElementKey(ElementKey? parent, string type, Name name) => (Parent, Type, Name) = (parent, type, name);

    public ElementKey? Parent { get; }

    public string Type { get; }

    public Name Name { get; }

    public string Path => Parent is null ? Name.ToString() : Parent.Path + "." + Name;

    public static Result<ElementKey> Of(string type, Name name) =>
        Invalid(type, name) is { } error ? error : new ElementKey(null, type, name);

    public static Result<ElementKey> Of(ElementKey parent, string type, Name name) =>
        (Invalid(type, name) ?? (name.Schema is null ? null : new Error(
            "element.child-name", $"{name} names a {type} of {parent} in two parts.", "Name an object keyed under its parent by its own one-part name.")))
            is { } error ? error : new ElementKey(parent, type, name);

    public int CompareTo(ElementKey? other)
    {
        var (mine, theirs) = (Chain(this), other is null ? [] : Chain(other));
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            if ((mine[i].Name.CompareTo(theirs[i].Name) is var n and not 0 ? n : string.CompareOrdinal(mine[i].Type, theirs[i].Type)) is var c and not 0)
            {
                return c;
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString() => Type + " " + Path;

    /// <summary>Whether two keys name one object under <paramref name="collation"/>: the same type at every level, the same depth, and each level's name matching.</summary>
    public bool Matches(ElementKey other, Collation collation)
    {
        var (mine, theirs) = (Chain(this), Chain(other));
        return mine.Length == theirs.Length && mine.Zip(theirs).All(pair => pair.First.Type == pair.Second.Type && pair.First.Name.Matches(pair.Second.Name, collation));
    }

    /// <summary>Keys as one under <paramref name="collation"/>, for a dictionary or a set: equality by <see cref="Matches"/>, and a hash that agrees with it.</summary>
    public static IEqualityComparer<ElementKey> Comparer(Collation collation) => collation.IsCaseSensitive ? EqualityComparer<ElementKey>.Default : new IgnoringCase(collation);

    /// <summary>This key, moved under <paramref name="parent"/> as the rename of an ancestor moves it.</summary>
    internal ElementKey Under(ElementKey? parent) => parent == Parent ? this : new ElementKey(parent, Type, Name);

    private static ElementKey[] Chain(ElementKey key) => key.Parent is null ? [key] : [.. Chain(key.Parent), key];

    /// <summary>Key equality under a case-insensitive collation: each level's type as it is and its name ignoring case.</summary>
    private sealed class IgnoringCase(Collation collation) : IEqualityComparer<ElementKey>
    {
        public bool Equals(ElementKey? x, ElementKey? y) => x is null ? y is null : y is not null && x.Matches(y, collation);

        public int GetHashCode(ElementKey key)
        {
            var hash = new HashCode();
            foreach (var level in Chain(key))
            {
                hash.Add(level.Type, StringComparer.Ordinal);
                hash.Add(level.Name.Schema, StringComparer.OrdinalIgnoreCase);
                hash.Add(level.Name.Base, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }
    }

    private static Error? Invalid(string type, Name name) =>
        string.IsNullOrWhiteSpace(type) ? new Error("element.type-blank", "An element's type is blank.", "Give the DacFx type name, such as Column.")
        : name == default ? new Error("element.name-missing", $"A {type} has no name.", "Make the name with Name.Of.")
        : null;
}

/// <summary>
/// One object of a model, as io/Ssdt.ReadModel reads it from DacFx for every consumer: its key, its properties as
/// (name, value) and its relationships as (name, the target keys in DacFx's order), each sorted by name, so the order
/// DacFx gives them in never matters. A relationship with no target is no relationship. The deploy scripts and the
/// refactorlog entries are elements too, each of its own type. A SortedArray of elements, sorted by key, is a model.
/// </summary>
public sealed record Element : IComparable<Element>
{
    public const string PreDeploymentScript = "PreDeploymentScript";
    public const string PostDeploymentScript = "PostDeploymentScript";
    public const string RefactorLogOperation = "RefactorLogOperation";

    private Element(ElementKey key, SortedArray<Property> properties, SortedArray<Relationship> relationships) =>
        (Key, Properties, Relationships) = (key, properties, relationships);

    public ElementKey Key { get; }

    public SortedArray<Property> Properties { get; }

    public SortedArray<Relationship> Relationships { get; }

    /// <summary>The value of the named property, or null when the element does not carry it.</summary>
    public Value? this[string property] => Properties.FirstOrDefault(p => p.Name == property)?.Value;

    public static Result<Element> Of(ElementKey key, IEnumerable<Property> properties, IEnumerable<Relationship> relationships)
    {
        var (ps, rs) = (SortedArray.Of(properties), SortedArray.Of(relationships.Where(r => r.Targets.Count > 0)));
        return (Repeated([.. ps.Select(p => p.Name)], key, "property") ?? Repeated([.. rs.Select(r => r.Name)], key, "relationship")) is { } error
            ? error
            : new Element(key, ps, rs);
    }

    /// <summary>The package's pre-deploy script, with the text the build inlined as its Text property.</summary>
    public static Element PreDeploy(string text) => Script(PreDeploymentScript, "PreDeploy", text);

    /// <summary>The package's post-deploy script, with the text the build inlined as its Text property.</summary>
    public static Element PostDeploy(string text) => Script(PostDeploymentScript, "PostDeploy", text);

    /// <summary>A refactorlog entry: keyed by its operation key, with the properties the file gives it (ElementName, ElementType, ParentElementName, NewName).</summary>
    public static Result<Element> RefactorLogEntry(string operationKey, IEnumerable<Property> properties) =>
        Name.Of(operationKey).Bind(name => ElementKey.Of(RefactorLogOperation, name)).Bind(key => Of(key, properties, []));

    public int CompareTo(Element? other) =>
        other is null ? 1
        : Key.CompareTo(other.Key) is var k and not 0 ? k
        : SortedArray.Compare(Properties, other.Properties) is var p and not 0 ? p
        : SortedArray.Compare(Relationships, other.Relationships);

    private static Element Script(string type, string name, string text) =>
        Known(Of(Known(ElementKey.Of(type, Known(Name.Of(name)))), [new Property("Text", new Value.Script(text))], []));

    private static T Known<T>(Result<T> result) => result.Match(value => value, error => throw new UnreachableException(error.Message));

    private static Error? Repeated(string[] names, ElementKey key, string what) => names
        .Where((name, i) => string.IsNullOrWhiteSpace(name) || (i > 0 && names[i - 1] == name))
        .Select(name => new Error("element." + what + "-name", $"{key} has a {what} named '{name}' that is blank or repeated.", $"Give each {what} of an element one distinct name."))
        .FirstOrDefault();

    public sealed record Property(string Name, Value Value) : IComparable<Property>
    {
        public int CompareTo(Property? other) =>
            other is null ? 1 : string.CompareOrdinal(Name, other.Name) is var c and not 0 ? c : Value.CompareTo(other.Value);
    }

    public sealed record Relationship(string Name, SortedArray<Relationship.Target> Targets) : IComparable<Relationship>
    {
        /// <summary>A relationship whose targets keep the order given, as DacFx gives a key's columns.</summary>
        public static Relationship Of(string name, IEnumerable<ElementKey> targets) => new(name, SortedArray.Of(targets.Select((key, i) => new Target(i, key))));

        public int CompareTo(Relationship? other) =>
            other is null ? 1 : string.CompareOrdinal(Name, other.Name) is var c and not 0 ? c : SortedArray.Compare(Targets, other.Targets);

        /// <summary>One target of a relationship: its position in DacFx's order, and its key.</summary>
        public sealed record Target(int Position, ElementKey Key) : IComparable<Target>
        {
            public int CompareTo(Target? other) =>
                other is null ? 1 : Position.CompareTo(other.Position) is var c and not 0 ? c : Key.CompareTo(other.Key);
        }
    }
}
