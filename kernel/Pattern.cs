using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DbChange.Kernel;

/// <summary>How a change holds an element, closed: in both models with a property or relationship that differs (altered), in the after
/// model alone (created), in the before model alone (dropped), or under the new key a refactorlog entry gives it (renamed). A recognize:
/// clause and a classification write each as its lowercase word; the members sort as those words do.</summary>
public enum ChangeKind
{
    Altered,
    Created,
    Dropped,
    Renamed,
}

/// <summary>What a recognize: condition tests of one property or relationship, closed: anything (any, written Length:any), a change
/// (changes, written Length), an integer that shrinks (falls) or grows (rises), a change from one value to another (from, written
/// Nullable:true→false), or the element's value (is, written Nullable=true). The members sort as their words do.</summary>
public enum ConditionTest
{
    Any,
    Changes,
    Falls,
    From,
    Is,
    Rises,
}

/// <summary>The lowercase word a recognize: block and a classification write for each change kind and condition test.</summary>
public static class PatternWords
{
    // CS8524 (an enum value no member names) is disabled for these switches alone; CS8509, a named member without an arm, stays an error.
#pragma warning disable CS8524
    public static string Word(this ChangeKind kind) => kind switch
    {
        ChangeKind.Altered => "altered",
        ChangeKind.Created => "created",
        ChangeKind.Dropped => "dropped",
        ChangeKind.Renamed => "renamed",
    };

    public static string Word(this ConditionTest test) => test switch
    {
        ConditionTest.Any => "any",
        ConditionTest.Changes => "changes",
        ConditionTest.Falls => "falls",
        ConditionTest.From => "from",
        ConditionTest.Is => "is",
        ConditionTest.Rises => "rises",
    };
#pragma warning restore CS8524

    /// <summary>The change kind a match: line writes as <paramref name="word"/>, or null for a word that names none.</summary>
    internal static ChangeKind? Kind(string word) =>
        Enum.GetValues<ChangeKind>().Cast<ChangeKind?>().FirstOrDefault(k => k!.Value.Word() == word);

    /// <summary>The condition test a condition writes after its colon (any, rises or falls), or null for a word that names none.</summary>
    internal static ConditionTest? Test(string word) =>
        Enum.GetValues<ConditionTest>().Cast<ConditionTest?>().FirstOrDefault(t => t!.Value.Word() == word);
}

/// <summary>
/// How an operation is recognized in a change (V3_MILESTONES.md §2.1 rule 2: the patterns are data): the recognize: block of the
/// operation's file, tests/Golden/patterns/&lt;operation&gt;.md until M7 moves each into knowledge/ops/&lt;operation&gt;.md. The block is a
/// line reading <c>recognize:</c> and the indented lines under it, up to the first line that is blank or not indented, so it may stand in
/// a fenced block of prose: one <c>match:</c> line per clause, any one of which recognizes the operation, and one <c>ships:</c> line, how
/// the operation ships, the shape Outcome.Predict names when its change blocks. A clause is a kind (created, dropped, renamed or altered),
/// a DacFx element type, and conditions, each on one property or relationship: <c>Nullable=true</c>, the element's value as Value prints
/// it (<c>NULL</c> where the element carries none); and, on an altered element alone, <c>Length</c> (it changes), <c>Length:any</c> (it
/// may change), <c>Length:rises</c> or <c>Length:falls</c> (an integer that grows or shrinks) and <c>Nullable:true→false</c> (from one
/// value to another). A clause recognizes an alteration only when its conditions account for every property and relationship the
/// alteration changes, so a column made longer and NOT NULL at once is neither widen nor make-mandatory.
/// </summary>
public sealed record Pattern(string Operation, SortedArray<Pattern.Clause> Clauses, string Ships) : IComparable<Pattern>
{
    private const string Grammar =
        "Write each clause as match: created, dropped, renamed or altered, a DacFx element type and its conditions, and one ships: line.";

    private static readonly Regex OperationName = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private static readonly Regex TypeName = new(@"^[A-Za-z]\w*$", RegexOptions.CultureInvariant);

    private static readonly Regex Written = new(
        @"^(?<property>[A-Za-z]\w*)(?:=(?<is>\S+)|:(?<test>any|rises|falls)|:(?<from>[^→\s]+)→(?<to>\S+))?$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The pattern the recognize: block of <paramref name="text"/> writes for <paramref name="operation"/>, the file's name less .md; null
    /// for a file without a block, such as an operation no change shows. A block that breaks the grammar, or an operation named in other
    /// than lowercase words joined by hyphens, is the error pattern.malformed, naming the operation's file and the line.
    /// </summary>
    public static Result<Pattern?> Parse(string operation, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var at = Array.FindIndex(lines, l => l.Trim() == "recognize:");
        if (at < 0)
        {
            return Result.Ok<Pattern?>(null);
        }

        if (!OperationName.IsMatch(operation))
        {
            return Malformed(operation, at, "names its operation '" + operation + "', which is not lowercase words joined by hyphens");
        }

        var (clauses, ships) = (new List<Clause>(), (string?)null);
        for (var n = at + 1; n < lines.Length && lines[n].Length > 0 && char.IsWhiteSpace(lines[n][0]) && lines[n].Trim().Length > 0; n++)
        {
            var line = lines[n].Trim();
            if (line.StartsWith("ships:", StringComparison.Ordinal) && ships is null && line[6..].Trim() is { Length: > 0 } shape)
            {
                ships = shape;
            }
            else if (!line.StartsWith("match:", StringComparison.Ordinal) || Clause.Parse(line[6..]) is not { } clause)
            {
                return Malformed(operation, n, "holds '" + line + "', which is no match: clause and no first ships: line");
            }
            else
            {
                clauses.Add(clause);
            }
        }

        return clauses.Count == 0 ? Malformed(operation, at, "holds no match: line")
            : ships is null ? Malformed(operation, at, "holds no ships: line")
            : new Pattern(operation, SortedArray.Of(clauses), ships);
    }

    /// <summary>Whether a clause recognizes the entry: an element the change creates, drops or renames (its alteration null), or one it
    /// alters.</summary>
    internal bool Recognizes(ChangeKind kind, Element element, Change.Alteration? alteration) =>
        Clauses.Any(c => c.Holds(kind, element, alteration));

    public int CompareTo(Pattern? other) =>
        other is null ? 1
        : string.CompareOrdinal(Operation, other.Operation) is var o and not 0 ? o
        : SortedArray.Compare(Clauses, other.Clauses) is var c and not 0 ? c
        : string.CompareOrdinal(Ships, other.Ships);

    private static Error Malformed(string operation, int line, string what) => new("pattern.malformed",
        operation + ".md: the recognize: block at line " + (line + 1).ToString(CultureInfo.InvariantCulture) + " " + what + ".",
        Grammar);

    /// <summary>One match: line: the kind of change, the element type, and its conditions.</summary>
    public sealed record Clause(ChangeKind Kind, string Type, SortedArray<Condition> Conditions) : IComparable<Clause>
    {
        public int CompareTo(Clause? other) =>
            other is null ? 1
            : Kind.CompareTo(other.Kind) is var k and not 0 ? k
            : string.CompareOrdinal(Type, other.Type) is var t and not 0 ? t
            : SortedArray.Compare(Conditions, other.Conditions);

        /// <summary>The clause a match: line writes, or null when the line breaks the grammar.</summary>
        internal static Clause? Parse(string written)
        {
            var words = written.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || PatternWords.Kind(words[0]) is not { } kind || !TypeName.IsMatch(words[1]))
            {
                return null;
            }

            var conditions = words[2..].Select(w => Written.Match(w)).Select(m => !m.Success ? null
                : m.Groups["is"].Success ? new Condition(m.Groups["property"].Value, ConditionTest.Is, null, m.Groups["is"].Value)
                : m.Groups["test"].Success ? PatternWords.Test(m.Groups["test"].Value) is { } test
                    ? new Condition(m.Groups["property"].Value, test, null, null) : null
                : m.Groups["from"].Success
                    ? new Condition(m.Groups["property"].Value, ConditionTest.From, m.Groups["from"].Value, m.Groups["to"].Value)
                : new Condition(m.Groups["property"].Value, ConditionTest.Changes, null, null)).ToList();
            return conditions.Any(c => c is null || (kind != ChangeKind.Altered && c.Test != ConditionTest.Is)) ? null
                : new Clause(kind, words[1], SortedArray.Of(conditions.OfType<Condition>()));
        }

        internal bool Holds(ChangeKind kind, Element element, Change.Alteration? alteration) =>
            kind == Kind && element.Key.Type == Type && Conditions.All(c => c.Holds(element, alteration))
            && (alteration is null || alteration.Properties.Select(p => p.Name).Concat(alteration.Relationships.Select(r => r.Name))
                .All(changed => Conditions.Any(c => c.Property == changed && c.Test != ConditionTest.Is)));
    }

    /// <summary>
    /// One condition of a clause on one property or relationship: the test (is, changes, any, rises, falls or from) and the values it
    /// names, as Value prints them.
    /// </summary>
    public sealed record Condition(string Property, ConditionTest Test, string? From, string? To) : IComparable<Condition>
    {
        public int CompareTo(Condition? other) =>
            other is null ? 1
            : string.CompareOrdinal(Property, other.Property) is var p and not 0 ? p
            : Test.CompareTo(other.Test) is var t and not 0 ? t
            : string.CompareOrdinal(From, other.From) is var f and not 0 ? f
            : string.CompareOrdinal(To, other.To);

        internal bool Holds(Element element, Change.Alteration? alteration)
        {
            var property = alteration?.Properties.FirstOrDefault(p => p.Name == Property);
            var changed = property is not null || alteration?.Relationships.Any(r => r.Name == Property) == true;
            // CS8524 is disabled for this switch alone, as in PatternWords; a test without an arm stays the error CS8509.
#pragma warning disable CS8524
            return Test switch
            {
                ConditionTest.Is => Printed(element[Property]) == To,
                ConditionTest.Any => true,
                ConditionTest.Changes => changed,
                ConditionTest.Rises => property is { Before: Value.Integer b, After: Value.Integer a } && a.Number > b.Number,
                ConditionTest.Falls => property is { Before: Value.Integer b, After: Value.Integer a } && a.Number < b.Number,
                ConditionTest.From => property is not null && Printed(property.Before) == From && Printed(property.After) == To,
            };
#pragma warning restore CS8524
        }

        private static string Printed(Value? value) => value?.ToString() ?? "NULL";
    }
}
