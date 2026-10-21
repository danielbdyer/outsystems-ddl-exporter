using System;
using System.Linq;
using CsCheck;
using DbChange.Kernel;
using DbChange.Tests;

namespace DbChange.Io.Tests;

/// <summary>
/// Generators for what predict writes (WP 2.5): preconditions of every kind, the six the property read finds and the seven only a plan's
/// statements show, each in a state a count or a person can give it (a TableEmpty violating exactly when populated, no row broken on a
/// table that holds none, no Scan or Rebuild violating, and the classes of its own kind alone, on a table that holds rows); trust states,
/// a disabled constraint untrusted as SQL Server holds it; and predictions, each an environment's existing data with its counts and the
/// claim's provenance, whose existing data fingerprint is the one that existing data has. Names draw from an alphabet holding a closing
/// bracket, which QUOTENAME doubles and the file's parser must undo.
/// </summary>
internal static class PredictionProperties
{
    private static readonly Gen<string> Part = Gen.Char["abA]"].Array[1, 4].Select(cs => new string(cs));

    private static readonly Gen<Name> Column = Part.Select(p => Expect.Value(Name.Of(p)));

    private static readonly Gen<Name> Table =
        Gen.Select(Gen.OneOfConst("dbo", "sales"), Part).Select((schema, part) => Expect.Value(Name.Of(schema, part)));

    private static readonly Gen<SortedArray<Name>> Columns = Column.Array[0, 3].Select(cs => SortedArray.Of(cs.Distinct()));

    private static readonly Gen<ColumnType> Type =
        Gen.Select(Gen.OneOfConst("int", "nvarchar", "decimal", "datetime2"), Gen.Long[0, 8000], Gen.Bool, Gen.Long[0, 38])
            .SelectMany(t => Gen.Select(Gen.Long[0, 7], Gen.OneOfConst<string?>(null, "Latin1_General_CS_AS"))
                .Select((scale, collation) => new ColumnType(Expect.Value(Name.Of(t.Item1)), t.Item2, t.Item3, t.Item4, scale, collation)));

    private static readonly Gen<string> Expression = Gen.OneOfConst("([Total]>=(0))", "([Code] like 'ABC%')", "(N'x''y' <> [Name])");

    private static readonly Gen<ScriptPart> Script = Gen.OneOfConst(ScriptPart.PreDeploy, ScriptPart.PostDeploy);

    private static Gen<Name?> NoneOr(Gen<Name> name) => Gen.OneOf(Gen.Const((Name?)null), name.Select(n => (Name?)n));

    /// <summary>A kind of each of the thirteen, with what it holds.</summary>
    private static readonly Gen<PreconditionKind> Kinds = Gen.OneOf(
        Gen.Const<PreconditionKind>(new PreconditionKind.TableEmpty()),
        Gen.Const<PreconditionKind>(new PreconditionKind.NotNull()),
        Gen.Select(Type, Type).Select((before, after) => (PreconditionKind)new PreconditionKind.Fits(before, after)),
        Gen.Select(Table, Gen.Select(Column, Column).Array[1, 2]).Select((parent, pairs) => (PreconditionKind)new PreconditionKind.ForeignKey(
            parent, SortedArray.Of(pairs.DistinctBy(p => p.Item1).Select(p => new PreconditionKind.ForeignKey.Pair(p.Item1, p.Item2))))),
        Gen.Select(Gen.OneOfConst<string?>(null, "([CategoryId] IS NOT NULL)"), Columns)
            .Select((filter, columns) => (PreconditionKind)new PreconditionKind.Unique(filter, filter is null ? [] : columns)),
        Expression.Select(e => (PreconditionKind)new PreconditionKind.Check(e)),
        Gen.Select(NoneOr(Column), Gen.Bool).Select((index, clustered) => (PreconditionKind)new PreconditionKind.KeyLength(index, clustered)),
        Gen.Const<PreconditionKind>(new PreconditionKind.Rebuild()),
        Expression.Select(e => (PreconditionKind)new PreconditionKind.Computed(e)),
        Gen.Select(Column, Column, NoneOr(Table))
            .Select((start, end, current) => (PreconditionKind)new PreconditionKind.Temporal(start, end, current)),
        Gen.Const<PreconditionKind>(new PreconditionKind.Scan()),
        Gen.Select(Script, Gen.Int[1, 400]).Select((script, line) => (PreconditionKind)new PreconditionKind.Authored(script, line)),
        Gen.Select(Columns, Script, Gen.Int[1, 400])
            .Select((code, script, line) => (PreconditionKind)new PreconditionKind.SeedDrift(code, script, line)));

    /// <summary>The classes a state of the kind can hold: Fits's value classes, Unique's collation classes, and Rebuild's gapped.</summary>
    private static RowClasses Admitted(PreconditionKind kind) => kind switch
    {
        PreconditionKind.Fits => RowClasses.OverLength | RowClasses.OverBytes | RowClasses.NotConvertible | RowClasses.EmptyString
            | RowClasses.Overflow | RowClasses.LossyRoundTrip,
        PreconditionKind.Unique => RowClasses.Exact | RowClasses.Case | RowClasses.Accent | RowClasses.TrailingSpace | RowClasses.Nulls,
        PreconditionKind.Rebuild => RowClasses.Gapped,
        _ => RowClasses.None,
    };

    public static readonly Gen<Precondition> Preconditions =
        Gen.Select(Table, Columns, Kinds).Select((table, columns, kind) => new Precondition(table, columns, kind));

    public static readonly Gen<PreconditionState> States = Gen.Select(Preconditions, Gen.Bool, Gen.Bool, Gen.Int[0, 4095])
        .Select((p, populated, violating, classes) => new PreconditionState(p, populated,
            p.Kind is PreconditionKind.TableEmpty ? populated : populated && violating && p.Kind.Breakable)
        {
            Classes = populated ? (RowClasses)classes & Admitted(p.Kind) : RowClasses.None,
        });

    private static readonly Gen<TrustState> Trust =
        Gen.Select(Gen.OneOfConst("ForeignKeyConstraint", "CheckConstraint"), Table, Gen.Bool, Gen.Bool).Select((type, name, disabled, untrusted) =>
            new TrustState(Expect.Value(ElementKey.Of(type, name)), disabled, disabled || untrusted));

    /// <summary>An existing data of up to eight states and three trust states, each precondition and each constraint once.</summary>
    public static readonly Gen<ExistingData> ExistingData = Gen.Select(States.Array[0, 8], Trust.Array[0, 3]).Select((states, trust) =>
        new ExistingData(SortedArray.Of(states.DistinctBy(s => s.Precondition)), SortedArray.Of(trust.DistinctBy(t => t.Constraint))));

    private static readonly Gen<Fingerprint> Digest = Gen.String.Select(s => Fingerprint.Of(s));

    private static readonly Gen<Server?> Server = Gen.OneOf(Gen.Const((Server?)null),
        Gen.Select(Gen.OneOfConst("16.0.4305.5", "15.0.4382.1"), Gen.OneOfConst("Express Edition (64-bit)", "Developer Edition (64-bit)"),
            Gen.OneOfConst(150, 160)).Select((version, edition, level) => (Server?)Expect.Value(Kernel.Server.Of(version, edition, level))));

    /// <summary>A time a claim records, in whole seconds, as the machine block writes it.</summary>
    private static readonly Gen<DateTimeOffset> At =
        Gen.Long[0, 2_000_000_000].Select(s => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000 + s));

    /// <summary>One environment's prediction: its existing data, counts of some of its preconditions, and the claim's provenance.</summary>
    public static Gen<Kernel.Prediction> Of(string environment) => Gen.Select(ExistingData, Gen.Long[0, 1_000_000_000].Array[8],
            Gen.Select(Digest, Digest, Digest, Server, At), Gen.OneOfConst("170.5.96", "162.5.57"))
        .Select((existing, rows, claim, dacfx) =>
        {
            var name = Expect.Value(EnvironmentName.Of("the generator", environment));
            var counts = existing.States.Select((s, i) => new PreconditionCount(s.Precondition, rows[i % rows.Length],
                s.Violating ? rows[(i + 1) % rows.Length] : 0));
            return new Kernel.Prediction(name, existing, SortedArray.Of(counts), Provenance.Prediction(claim.Item1, claim.Item2,
                ExistingDataFile.FingerprintOf(existing), Expect.Value(DacFxVersion.Of(dacfx)), claim.Item4, claim.Item3,
                new Target.Environment(name), claim.Item5));
        });

    /// <summary>Predictions of one to three environments, each named once, as one machine block holds them.</summary>
    public static readonly Gen<Kernel.Prediction[]> Predictions = Gen.Int[1, 3].SelectMany(n =>
        Gen.Select(Of("dev"), Of("qa"), Of("uat-2")).Select((dev, qa, uat) => new[] { dev, qa, uat }[..n]));
}
