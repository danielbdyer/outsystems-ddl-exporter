using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// A deploy report as the kernel holds it, and the drift decision over it: a report is identified by its operations, their keys and issues,
/// and its alerts; an operation name DacFx adds later is kept and refuses nothing; and check drift names the columns that differ under each
/// table the plan alters, and nothing else.
/// </summary>
public sealed class PlanTests
{
    private static readonly Gen<string> Word = Gen.Char["abAB"].Array[1, 3].Select(cs => new string(cs));

    private static readonly Gen<PlanOperation> Operations = Gen.Select(
            Gen.OneOfConst("Create", "Alter", "Drop", "TableRebuild", "Rename", "Refresh", "AddSystemVersioning"),
            Gen.Select(Gen.OneOfConst("Table", "View", "Sequence"), Word, Word).Select((type, schema, name) => Key(type, schema, name)),
            Gen.Int[1, 3].Array[0, 2])
        .Select((name, key, issues) => new PlanOperation(PlanOperationKind.Of(name), key, SortedArray.Of(issues.Distinct())));

    private static readonly Gen<PlanAlert> Alerts = Gen.Select(Gen.OneOfConst("DataIssue", "DataMotion", "CreateClusteredIndex"),
        Gen.Int[1, 3].Select(i => (int?)i).Array[0, 1], Word)
        .Select((name, id, text) => new PlanAlert(PlanAlertKind.Of(name), id.Length == 0 ? null : id[0], text));

    private static readonly Gen<DeployReport> Reports = Gen.Select(Operations.Array[0, 3], Alerts.Array[0, 2])
        .Select((operations, alerts) => new DeployReport(SortedArray.Of(operations), SortedArray.Of(alerts)));

    /// <summary>
    /// S21 of the pre-M2 review: the operation an alert concerns is one whose item cites the alert's id, and there is one exactly when an
    /// operation cites it; an alert with no id, a DataMotion's, concerns none. check drift names a data issue's finding by it.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_alert_concerns_an_operation_that_cites_its_id_and_none_when_no_operation_does() =>
        Reports.Sample(report => report.Alerts.All(alert => report.Operation(alert) is { } cited
            ? alert.Id is { } id && cited.Issues.Contains(id) && report.Operations.Contains(cited)
            : alert.Id is not { } none || report.Operations.All(o => !o.Issues.Contains(none))));

    [Fact]
    [Trait("Category", "fast")]
    public void A_report_fingerprints_alike_only_when_its_operations_keys_and_alerts_are_equal()
    {
        Gen.Select(Reports, Reports).Sample((a, b) => (Fingerprint.Of(a) == Fingerprint.Of(b)) == (a == b));
        Reports.Where(r => r.Operations.Count > 0 && r.Alerts.Count > 0).Sample(report =>
        {
            var first = report.Operations[0];
            var alert = report.Alerts[0];
            DeployReport[] edited =
            [
                report with { Operations = Replaced(report.Operations, first,
                    first with { Issues = SortedArray.Of(first.Issues.Append(9)) }) },
                report with { Operations = Replaced(report.Operations, first,
                    first with { Kind = first.Kind is PlanOperationKind.Alter ? new PlanOperationKind.Drop()
                        : new PlanOperationKind.Alter() }) },
                report with { Alerts = Replaced(report.Alerts, alert, alert with { Text = alert.Text + "~" }) },
                report with { Alerts = Replaced(report.Alerts, alert, alert with { Id = alert.Id is null ? 1 : null }) },
            ];
            return edited.All(e => Fingerprint.Of(e) != Fingerprint.Of(report))
                && Fingerprint.Of(new DeployReport(SortedArray.Of(report.Operations.Reverse()),
                    SortedArray.Of(report.Alerts.Reverse()))) == Fingerprint.Of(report);
        });
    }

    /// <summary>DacFx's deploy report is not documented as closed: a name outside the measured list is kept as written, dbchange writes it
    /// unlisted, and nothing refuses it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_operation_name_DacFx_adds_later_is_kept_verbatim_and_refuses_nothing()
    {
        var added = PlanOperationKind.Of("AddSystemVersioning");

        Assert.Equal(new PlanOperationKind.Unlisted("AddSystemVersioning"), added);
        Assert.Equal(("AddSystemVersioning", "unlisted", false), (added.Name, added.Word, added.IsConsequence));
        Assert.Equal(["create", "alter", "drop", "table-rebuild", "rename", "refresh", "unbind-schemabinding", "rebind-schemabinding"],
            ((string[])["Create", "Alter", "Drop", "TableRebuild", "Rename", "Refresh", "UnbindSchemabinding", "RebindSchemabinding"])
                .Select(n => PlanOperationKind.Of(n).Word));
        Assert.Equal(["Refresh", "UnbindSchemabinding", "RebindSchemabinding"],
            ((string[])["Create", "Alter", "Drop", "TableRebuild", "Rename", "Refresh", "UnbindSchemabinding", "RebindSchemabinding"])
                .Where(n => PlanOperationKind.Of(n).IsConsequence));
        Assert.Equal(new PlanAlertKind.Unlisted("LedgerRename"), PlanAlertKind.Of("LedgerRename"));
    }

    /// <summary>
    /// VALUES.md S2, contract C7: over generated targets and packages, with a plan altering or rebuilding one table, check drift lists each
    /// column of that table the target and the package disagree on (created, dropped, or altered) and nothing keyed under another table;
    /// an empty plan matches whatever the two hold.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S2")]
    public void Check_drift_names_every_differing_column_of_every_table_the_plan_alters_and_nothing_else()
    {
        Gen.Select(Sets, Sets)
            .Sample((target, package) => Ok(Drift.Of(new DeployReport([], []), target, package, Collation.CaseSensitive)) is Drift.InSync);
        Gen.Select(Sets, Sets, Gen.Int[0, 1000], Gen.Bool).Where((target, package, _, _) => Tables(target, package).Count > 0)
            .Sample((target, package, pick, rebuilt) =>
            {
                var tables = Tables(target, package);
                var table = tables[pick % tables.Count];
                var other = tables.FirstOrDefault(t => t != table);
                SortedArray<PlanOperation> operations = [
                    new PlanOperation(rebuilt ? new PlanOperationKind.TableRebuild() : new PlanOperationKind.Alter(), table, []),
                    .. other is null ? [] : new[] { new PlanOperation(new PlanOperationKind.Refresh(), other, []) }];
                var columns = Assert.IsType<Drift.Differs>(Ok(Drift.Of(new DeployReport(operations, []), target, package,
                    Collation.CaseSensitive))).Columns;

                var (was, now) = (Columns(target, table), Columns(package, table));
                var differing = was.Keys.Union(now.Keys)
                    .Where(key => !was.TryGetValue(key, out var a) || !now.TryGetValue(key, out var b) || a != b).ToHashSet();
                var listed = columns.Created.Select(e => e.Key).Concat(columns.Dropped.Select(e => e.Key))
                    .Concat(columns.Altered.Select(a => a.Key)).ToList();
                return listed.ToHashSet().SetEquals(differing) && listed.All(key => key.Parent == table);
            });
    }

    /// <summary>DF-8: SQL Server keeps a computed column's text as it normalized it, so a column differing from its package in Expression
    /// alone is no drift, and one differing in more keeps the rest.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_computed_column_s_normalized_expression_is_no_drift()
    {
        var table = Key("Table", "dbo", "Customer");
        var column = Key(table, "Column", "Doubled");
        SortedArray<Element> Model(string expression, bool nullable) =>
            [New(table, []), New(column, [("Expression", new Value.Script(expression)), ("Nullable", Bool(nullable))])];
        var plan = new DeployReport([new PlanOperation(new PlanOperationKind.Alter(), table, [])], []);

        var normalized = Assert.IsType<Drift.Differs>(Ok(Drift.Of(plan, Model("([Id]*(2))", true), Model("(Id * 2)", true),
            Collation.CaseSensitive)));
        var tightened = Assert.IsType<Drift.Differs>(Ok(Drift.Of(plan, Model("([Id]*(2))", true), Model("(Id * 2)", false),
            Collation.CaseSensitive)));

        Assert.True(normalized.Columns.IsEmpty);
        Assert.Equal(["Nullable"], Assert.Single(tightened.Columns.Altered).Properties.Select(p => p.Name));
    }

    /// <summary>
    /// The operator's ruling of 2026-10-19 (spike S11's scout): SQL Server stores a check written with IN as a chain of ORs, so every plan
    /// against a database holding it drops the check and creates it again. Drift names each check its plan drops and creates whose
    /// repository text lists values with IN, with the text the database stores; a check written in the OR form, one with IN only inside a
    /// string literal, and a check the plan creates without dropping it are named by none.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("([Pinned] IN (0, 1))", true)]
    [InlineData("(Pinned not in(0))", true)]
    [InlineData("([Pinned]=(1) OR [Pinned]=(0))", false)]
    [InlineData("([Code]<>N'IN (A)')", false)]
    public void A_check_its_plan_drops_and_creates_is_named_with_the_text_SQL_Server_stores_when_the_repository_writes_it_with_IN(
        string written, bool named)
    {
        const string Stored = "([Pinned]=(1) OR [Pinned]=(0))";
        var (table, check) = (Key("Table", "dbo", "Note"), Key("CheckConstraint", "dbo", "CK_Note_Pinned"));
        SortedArray<Element> Model(string expression) => [New(table, []), New(check, [("Expression", new Value.Script(expression))])];
        var restated = new DeployReport(
            [new PlanOperation(new PlanOperationKind.Drop(), check, []), new PlanOperation(new PlanOperationKind.Create(), check, [])], []);
        var created = new DeployReport([new PlanOperation(new PlanOperationKind.Create(), check, [])], []);

        Assert.Equal(named ? [new CheckWrittenWithIn(check, written, Stored)] : [],
            Assert.IsType<Drift.Differs>(Ok(Drift.Of(restated, Model(Stored), Model(written), Collation.CaseSensitive))).WrittenWithIn);
        Assert.Empty(Assert.IsType<Drift.Differs>(Ok(Drift.Of(created, [New(table, [])], Model(written), Collation.CaseSensitive)))
            .WrittenWithIn);
    }

    /// <summary>The tables either set holds.</summary>
    private static List<ElementKey> Tables(SortedArray<Element> a, SortedArray<Element> b) =>
        [.. a.Concat(b).Select(e => e.Key).Where(k => k.Type == "Table").Distinct()];

    /// <summary>The columns a set holds under a table, by key.</summary>
    private static Dictionary<ElementKey, Element> Columns(SortedArray<Element> set, ElementKey table) =>
        set.Where(e => e.Key.Type == "Column" && e.Key.Parent == table).ToDictionary(e => e.Key);

    private static SortedArray<T> Replaced<T>(SortedArray<T> items, T before, T after) where T : IComparable<T> =>
        SortedArray.Of(items.Where(i => !Equals(i, before)).Append(after));
}
