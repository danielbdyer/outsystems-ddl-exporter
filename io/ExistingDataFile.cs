using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DbChange.Kernel;
using ColumnType = DbChange.Kernel.ColumnType;

namespace DbChange.Io;

/// <summary>
/// An environment's existing data as a file (V3_MILESTONES.md §3.1), dbchange.existing-data/1, which synthetic-copy up reads with
/// --existing-data and predict writes from M2: JSON holding each precondition's state, the precondition written whole (its kind, its table
/// and columns as QUOTENAME writes them, and what its kind holds: a Fits's two types with their five facets, a ForeignKey's parent and
/// pairs, a Unique's filter and the columns the filter reads, a Check's expression), whether its table holds a row, whether a row breaks
/// it, and the classes of a Fits or a Unique by the words io/AggregateQueries.cs writes; and each constraint the environment holds
/// disabled or untrusted (kernel TrustState). It holds names and booleans, and no value of any row. The schema is closed: a key it does
/// not name, a value of another form, a state or a constraint given twice, and a state no table can hold (a TableEmpty whose violation
/// differs from its rows, a violating precondition on a table that holds no row, a class of another kind, a disabled constraint read as
/// trusted) are each existing-data.malformed, naming the JSON path and quoting no value. <see cref="Text"/> writes the canonical form,
/// which <see cref="FingerprintOf"/> hashes, so one existing data has one fingerprint however its file was written.
/// </summary>
public static class ExistingDataFile
{
    public const string Schema = "dbchange.existing-data/1";

    /// <summary>The classes of Fits, then of Unique, as io/AggregateQueries.cs names and orders them, each with its RowClasses
    /// flag.</summary>
    private static readonly (string Word, RowClasses Flag, bool Fits)[] Classes = [.. Enum.GetValues<AggregateQueries.Class>()
        .Select(c => (AggregateQueries.Word(c), Flag(c), c <= AggregateQueries.Class.LossyRoundTrip))];

    /// <summary>The RowClasses flag of a class io/AggregateQueries.cs counts: the two enumerations name the classes in one order.</summary>
    internal static RowClasses Flag(AggregateQueries.Class @class) => (RowClasses)(1 << (int)@class);

    /// <summary>The existing data a file holds, or existing-data.unreadable where it cannot be read, or existing-data.malformed.</summary>
    public static Result<ExistingData> Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (Write.FileSystemFailure(e))
        {
            return new Error("existing-data.unreadable", path + " cannot be read: " + e.Message.TrimEnd('.') + ".",
                "Name the existing data's file by its path from the working directory, after --existing-data.");
        }

        return Parse(text, path);
    }

    /// <summary>The existing data <paramref name="text"/> holds, read for the file <paramref name="source"/> names.</summary>
    internal static Result<ExistingData> Parse(string text, string source)
    {
        try
        {
            var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
                ?? throw new Malformed("$", "is no JSON object");
            Keys(root, "$", ["schema", "states", "trust"], ["schema", "states"]);
            if (Text(root, "$", "schema") != Schema)
            {
                throw new Malformed("$.schema", "is not " + Schema);
            }

            var states = Items(root, "$", "states").Select((s, i) => State(s, Place("$.states", i))).ToList();
            var trust = root["trust"] is null ? [] : Items(root, "$", "trust").Select((t, i) => Trust(t, Place("$.trust", i))).ToList();
            Once(states.Select(s => s.Precondition), "$.states", "a state of one precondition");
            Once(trust.Select(t => t.Constraint), "$.trust", "a state of one constraint");
            return new ExistingData(SortedArray.Of(states), SortedArray.Of(trust));
        }
        catch (JsonException e)
        {
            return new Error("existing-data.malformed", source + " is not JSON" + (e.LineNumber is { } line
                    ? string.Create(CultureInfo.InvariantCulture, $" at line {line + 1}, byte {e.BytePositionInLine + 1}")
                    : ", or gives one key twice in an object") + ".",
                "Correct the file at that place, or read the environment's existing data again.");
        }
        catch (Malformed m)
        {
            return new Error("existing-data.malformed", source + " holds no " + Schema + " existing data: " + m.Path + " " + m.What + ".",
                "Correct the file at " + m.Path + ", or read the environment's existing data again.");
        }
    }

    /// <summary>The existing data in its canonical form: the states, then the trust states, each in its own order, as JSON dbchange
    /// writes it.</summary>
    public static string Text(ExistingData existing) => Json.Text(new JsonObject
    {
        ["schema"] = Schema,
        ["states"] = new JsonArray([.. existing.States.Select(StateJson)]),
        ["trust"] = new JsonArray([.. existing.Trust.Select(t => new JsonObject
        {
            ["constraint"] = t.Constraint.Name.ToString(), ["type"] = t.Constraint.Type, ["disabled"] = t.Disabled,
            ["untrusted"] = t.Untrusted,
        })]),
    });

    /// <summary>A precondition's state as the file writes it: the precondition, whether its table holds a row, whether a row breaks it,
    /// and its classes by their words.</summary>
    public static JsonObject StateJson(PreconditionState state) => new()
    {
        ["precondition"] = PreconditionJson(state.Precondition), ["populated"] = state.Populated, ["violating"] = state.Violating,
        ["classes"] = new JsonArray([.. Classes.Where(c => state.Classes.HasFlag(c.Flag)).Select(c => (JsonNode?)c.Word)]),
    };

    /// <summary>The fingerprint of an existing data: of its canonical form.</summary>
    public static Fingerprint FingerprintOf(ExistingData existing) => Fingerprint.Of(Text(existing));

    /// <summary>A precondition as the file writes it: its kind, its table and its columns, and what its kind holds.</summary>
    public static JsonObject PreconditionJson(Precondition p)
    {
        var json = new JsonObject
        {
            ["kind"] = p.Kind.Word,
            ["table"] = p.Table.ToString(), ["columns"] = new JsonArray([.. p.Columns.Select(c => (JsonNode?)c.ToString())]),
        };
        switch (p.Kind)
        {
            case PreconditionKind.Fits fits:
                (json["before"], json["after"]) = (TypeJson(fits.Before), TypeJson(fits.After));
                break;
            case PreconditionKind.ForeignKey foreign:
                json["parent"] = foreign.Parent.ToString();
                json["pairs"] = new JsonArray([.. foreign.Pairs.Select(pair => new JsonObject
                {
                    ["column"] = pair.Column.ToString(), ["referenced"] = pair.Referenced.ToString(),
                })]);
                break;
            case PreconditionKind.Unique unique:
                json["filter"] = unique.Filter;
                json["filterColumns"] = new JsonArray([.. unique.FilterColumns.Select(c => (JsonNode?)c.ToString())]);
                break;
            case PreconditionKind.Check check:
                json["expression"] = check.Expression;
                break;
        }

        return json;
    }

    private static JsonObject TypeJson(ColumnType type) => new()
    {
        ["type"] = type.Type.ToString(), ["length"] = type.Length, ["isMax"] = type.IsMax, ["precision"] = type.Precision,
        ["scale"] = type.Scale, ["collation"] = type.Collation,
    };

    private static PreconditionState State(JsonNode? node, string at)
    {
        var state = Object(node, at);
        Keys(state, at, ["precondition", "populated", "violating", "classes"], ["precondition", "populated", "violating"]);
        var precondition = PreconditionOf(state["precondition"], at + ".precondition");
        var (populated, violating) = (Boolean(state, at, "populated"), Boolean(state, at, "violating"));
        var classes = RowClasses.None;
        foreach (var (item, i) in (state["classes"] is null ? [] : Items(state, at, "classes")).Select((c, i) => (c, i)))
        {
            var word = Value(item, Place(at + ".classes", i));
            var known = Classes.Where(c => c.Word == word
                && (c.Fits ? precondition.Kind is PreconditionKind.Fits : precondition.Kind is PreconditionKind.Unique)).ToList();
            classes |= known is [var (_, flag, _)] ? flag
                : throw new Malformed(Place(at + ".classes", i), "is no class io/AggregateQueries.cs counts for the precondition's kind");
        }

        return precondition.Kind is PreconditionKind.TableEmpty && populated != violating
                ? throw new Malformed(at,
                    "answers a TableEmpty violating otherwise than populated, which BlockOnPossibleDataLoss reads as one")
            : violating && !populated ? throw new Malformed(at, "answers a precondition violating on a table that holds no row")
            : new PreconditionState(precondition, populated, violating) { Classes = classes };
    }

    private static Precondition PreconditionOf(JsonNode? node, string at)
    {
        var json = Object(node, at);
        var kind = Text(json, at, "kind");
        string[] common = ["kind", "table", "columns"];
        var (keys, required) = kind switch
        {
            "TableEmpty" or "NotNull" => (common, new[] { "kind", "table" }),
            "Fits" => ([.. common, "before", "after"], ["kind", "table", "columns", "before", "after"]),
            "ForeignKey" => ([.. common, "parent", "pairs"], ["kind", "table", "columns", "parent", "pairs"]),
            "Unique" => ([.. common, "filter", "filterColumns"], ["kind", "table", "columns"]),
            "Check" => ([.. common, "expression"], ["kind", "table", "expression"]),
            _ => throw new Malformed(at + ".kind", "is none of TableEmpty, NotNull, Fits, ForeignKey, Unique and Check"),
        };
        Keys(json, at, keys, required);
        PreconditionKind made = kind switch
        {
            "TableEmpty" => new PreconditionKind.TableEmpty(),
            "NotNull" => new PreconditionKind.NotNull(),
            "Fits" => new PreconditionKind.Fits(TypeOf(json["before"], at + ".before"), TypeOf(json["after"], at + ".after")),
            "ForeignKey" => new PreconditionKind.ForeignKey(NameOf(json["parent"], at + ".parent"),
                SortedArray.Of(Items(json, at, "pairs").Select((p, i) => Pair(p, Place(at + ".pairs", i))))),
            "Unique" => new PreconditionKind.Unique(json["filter"] is null ? null : Nullable(json["filter"], at + ".filter"),
                json["filterColumns"] is null ? [] : Names(json, at, "filterColumns")),
            _ => new PreconditionKind.Check(Text(json, at, "expression")),
        };
        return new Precondition(NameOf(json["table"], at + ".table"), json["columns"] is null ? [] : Names(json, at, "columns"), made);
    }

    private static PreconditionKind.ForeignKey.Pair Pair(JsonNode? node, string at)
    {
        var pair = Object(node, at);
        Keys(pair, at, ["column", "referenced"], ["column", "referenced"]);
        return new(NameOf(pair["column"], at + ".column"), NameOf(pair["referenced"], at + ".referenced"));
    }

    private static ColumnType TypeOf(JsonNode? node, string at)
    {
        var type = Object(node, at);
        string[] keys = ["type", "length", "isMax", "precision", "scale", "collation"];
        Keys(type, at, keys, keys);
        return new ColumnType(NameOf(type["type"], at + ".type"), Count(type, at, "length"), Boolean(type, at, "isMax"),
            Count(type, at, "precision"), Count(type, at, "scale"), Nullable(type["collation"], at + ".collation"));
    }

    private static TrustState Trust(JsonNode? node, string at)
    {
        var trust = Object(node, at);
        string[] keys = ["constraint", "type", "disabled", "untrusted"];
        Keys(trust, at, keys, keys);
        var type = Text(trust, at, "type");
        if (type is not ("ForeignKeyConstraint" or "CheckConstraint"))
        {
            throw new Malformed(at + ".type", "is neither ForeignKeyConstraint nor CheckConstraint");
        }

        var (disabled, untrusted) = (Boolean(trust, at, "disabled"), Boolean(trust, at, "untrusted"));
        return disabled && !untrusted ? throw new Malformed(at, "reads a disabled constraint as trusted, which SQL Server never holds")
            : ElementKey.Of(type, NameOf(trust["constraint"], at + ".constraint")) is Result<ElementKey>.Ok(var key)
                ? new TrustState(key, disabled, untrusted)
                : throw new Malformed(at + ".constraint", "is no constraint's name");
    }

    /// <summary>A name as QUOTENAME writes it, of one or two parts, each of 1 to 128 characters.</summary>
    private static Name NameOf(JsonNode? node, string at) =>
        TSql.NameParts(Value(node, at)) is { Length: 1 or 2 } parts
            && (parts.Length == 1 ? Name.Of(parts[0]) : Name.Of(parts[0], parts[1])) is Result<Name>.Ok(var name)
            ? name : throw new Malformed(at, "is no name of one or two parts as QUOTENAME writes it, such as [dbo].[Customer]");

    private static SortedArray<Name> Names(JsonObject json, string at, string key) =>
        SortedArray.Of(Items(json, at, key).Select((n, i) => NameOf(n, Place(at + "." + key, i))));

    private static void Keys(JsonObject json, string at, string[] known, string[] required)
    {
        if (json.Select(p => p.Key).FirstOrDefault(k => !known.Contains(k, StringComparer.Ordinal)) is { } unknown)
        {
            throw new Malformed(at, "holds a key the schema does not name" + (unknown.All(char.IsAsciiLetter) ? ", " + unknown : ""));
        }

        if (required.FirstOrDefault(k => json[k] is null && !json.ContainsKey(k)) is { } missing)
        {
            throw new Malformed(at + "." + missing, "is missing");
        }
    }

    private static void Once<T>(IEnumerable<T> items, string at, string what)
    {
        if (items.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1) is not null)
        {
            throw new Malformed(at, "gives " + what + " twice");
        }
    }

    private static JsonObject Object(JsonNode? node, string at) => node as JsonObject ?? throw new Malformed(at, "is no JSON object");

    private static JsonArray Items(JsonObject json, string at, string key) =>
        json[key] as JsonArray ?? throw new Malformed(at + "." + key, "is no JSON array");

    private static string Text(JsonObject json, string at, string key) => Value(json[key], at + "." + key);

    private static string Value(JsonNode? node, string at) =>
        node is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0
            ? text : throw new Malformed(at, "is no string that is not empty");

    private static string? Nullable(JsonNode? node, string at) => node is null ? null : Value(node, at);

    private static bool Boolean(JsonObject json, string at, string key) =>
        json[key] is JsonValue value && value.TryGetValue(out bool flag) ? flag : throw new Malformed(at + "." + key, "is no boolean");

    private static long Count(JsonObject json, string at, string key) =>
        json[key] is JsonValue value && value.TryGetValue(out long n) && n >= 0 ? n
        : throw new Malformed(at + "." + key, "is no integer from 0");

    private static string Place(string at, int i) => at + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>Where the file departs from the schema, and how; read into existing-data.malformed.</summary>
    private sealed class Malformed(string path, string what) : Exception(path + " " + what)
    {
        public string Path { get; } = path;

        public string What { get; } = what;
    }
}
