using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;
using static DbChange.Kernel.Tests.SchemaProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// Order.Of, the planter's load order (V3_MILESTONES.md WP 3.1): parents before children; inside a cycle, the optional references
/// deferred to a second pass; a cycle of mandatory references reported with its error. Generated schemas hold up to eight tables in dbo
/// and references between them at random, a table's own included, each through one column of its own whose nullability makes the
/// reference optional or mandatory. Reachability is checked against Floyd-Warshall's closure. The PLT cases are
/// tests/Cases/20-planter.md's.
/// </summary>
public sealed class OrderTests
{
    /// <summary>A schema and its references as (child, parent, optional, key), by table index.</summary>
    private sealed record Graph(Schema Schema, (int Child, int Parent, bool Optional, ElementKey Key)[] Edges)
    {
        public int Count => Schema.Tables.Count;

        public int IndexOf(Name table) => Schema.Tables.Select(t => t.Name).ToList().IndexOf(table);

        /// <summary>Whether a path of at least one edge leads from a to b, through every edge or the mandatory ones alone.</summary>
        public bool[,] Reach(bool mandatoryOnly)
        {
            var reach = new bool[Count, Count];
            foreach (var e in Edges.Where(e => !mandatoryOnly || !e.Optional))
            {
                reach[e.Child, e.Parent] = true;
            }

            for (var k = 0; k < Count; k++)
            {
                for (var i = 0; i < Count; i++)
                {
                    for (var j = 0; j < Count; j++)
                    {
                        reach[i, j] |= reach[i, k] && reach[k, j];
                    }
                }
            }

            return reach;
        }
    }

    private static readonly Gen<Graph> Graphs = Gen.Int[1, 8].SelectMany(n =>
        Gen.Select(Gen.Int[0, n - 1], Gen.Int[0, n - 1], Gen.Bool).Array[0, 12].Select(edges => Build(n, edges)));

    [Fact]
    [Trait("Category", "fast")]
    public void Order_places_each_table_once_and_every_parent_before_its_children_outside_a_cycle() =>
        Graphs.Sample(g =>
        {
            var order = Order.Of(g.Schema);
            var at = Positions(order);
            var reach = g.Reach(mandatoryOnly: false);
            return order.Tables.Select(s => s.Position).SequenceEqual(Enumerable.Range(0, g.Count))
                && order.Tables.Select(s => s.Table).Order().SequenceEqual(g.Schema.Tables.Select(t => t.Name))
                && g.Edges.All(e => (reach[e.Parent, e.Child] && reach[e.Child, e.Parent])
                    || at[g.Schema.Tables[e.Parent].Name] < at[g.Schema.Tables[e.Child].Name]);
        });

    [Fact]
    [Trait("Category", "fast")]
    public void Order_defers_only_optional_references_and_only_inside_a_cycle() =>
        Graphs.Sample(g =>
        {
            var reach = g.Reach(mandatoryOnly: false);
            var inside = g.Edges.Where(e => e.Optional && reach[e.Parent, e.Child] && reach[e.Child, e.Parent])
                .Select(e => new Order.Deferral(g.Schema.Tables[e.Child].Name, e.Key));
            return Order.Of(g.Schema).Deferred == SortedArray.Of(inside);
        });

    [Fact]
    [Trait("Category", "fast")]
    public void Order_reports_a_cycle_exactly_when_tables_reference_one_another_through_mandatory_references_and_names_each_table_on_it() =>
        Graphs.Sample(g =>
        {
            var reach = g.Reach(mandatoryOnly: true);
            var cycles = Order.Of(g.Schema).Cycles;
            var named = cycles.SelectMany(c => c.Members.Select(g.IndexOf)).ToList();
            var onCycle = Enumerable.Range(0, g.Count).Where(t => reach[t, t]);
            bool Mandatory(int child, int parent) => g.Edges.Any(e => !e.Optional && e.Child == child && e.Parent == parent);
            return named.Order().SequenceEqual(onCycle)
                && cycles.All(c => c.Members.All(a => c.Members.All(b => reach[g.IndexOf(a), g.IndexOf(b)])))
                && cycles.All(c => c.Path[0].Table == c.Members[0] && c.Path.All(s => c.Members.Contains(s.Table))
                    && c.Path.Select((s, i) => Mandatory(g.IndexOf(s.Table), g.IndexOf(c.Path[(i + 1) % c.Path.Count].Table))).All(m => m)
                    && c.Path.Select(s => s.Table).Distinct().Count() == c.Path.Count);
        });

    [Fact]
    [Trait("Category", "fast")]
    public void With_its_deferred_references_left_out_the_order_respects_every_reference_outside_a_reported_cycle() =>
        Graphs.Sample(g =>
        {
            var order = Order.Of(g.Schema);
            var at = Positions(order);
            var deferred = order.Deferred.Select(d => d.Reference).ToHashSet();
            bool Together(Name a, Name b) => order.Cycles.Any(c => c.Members.Contains(a) && c.Members.Contains(b));
            return g.Edges.Where(e => !deferred.Contains(e.Key)).All(e =>
            {
                var (child, parent) = (g.Schema.Tables[e.Child].Name, g.Schema.Tables[e.Parent].Name);
                return Together(child, parent) || at[parent] < at[child];
            });
        });

    [Fact]
    [Trait("Category", "fast")]
    public void A_schema_with_no_cycle_defers_nothing() =>
        Graphs.Sample(g =>
        {
            var reach = g.Reach(mandatoryOnly: false);
            var order = Order.Of(g.Schema);
            return Enumerable.Range(0, g.Count).Any(t => reach[t, t]) || (order.Deferred.Count == 0 && order.Cycles.Count == 0);
        });

    /// <summary>D1: the order of a fixed schema with ties, pinned, since a change in how ties break changes WP 3.8's planted rows.
    /// Account and Customer reference each other through nullable columns and Status references nothing, so all three are ready at
    /// once: the component whose smallest name is Account goes first, its tables in name order, then Status, then Order, which
    /// references Customer and Status through NOT NULL columns, then Note, which references Order.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Ready_tables_are_placed_smallest_name_first_and_a_cycle_s_tables_in_name_order_at_its_place()
    {
        var order = Order.Of(Schema(("Order", "CustomerId", "Customer", false), ("Order", "StatusId", "Status", false),
            ("Customer", "AccountId", "Account", true), ("Account", "CustomerId", "Customer", true), ("Note", "OrderId", "Order", false)));

        Assert.Equal([new Order.Step(0, Table("Account")), new Order.Step(1, Table("Customer")), new Order.Step(2, Table("Status")),
            new Order.Step(3, Table("Order")), new Order.Step(4, Table("Note"))], order.Tables);
        Assert.Equal([new Order.Deferral(Table("Account"), ForeignKey("Account", "CustomerId")),
            new Order.Deferral(Table("Customer"), ForeignKey("Customer", "AccountId"))], order.Deferred);
        Assert.Empty(order.Cycles);
    }

    /// <summary>PLT-57: a NOT NULL self-reference, Employee.ManagerId to Employee.Id, needs a declared row.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_mandatory_self_reference_is_a_cycle_of_one_table()
    {
        var order = Order.Of(Schema(("Employee", "ManagerId", "Employee", false)));

        var cycle = Assert.Single(order.Cycles);
        Assert.Equal([Table("Employee")], cycle.Members);
        Assert.Equal([new Order.Step(0, Table("Employee"))], cycle.Path);
        Assert.Empty(order.Deferred);
        Assert.Equal([new Order.Step(0, Table("Employee"))], order.Tables);
    }

    /// <summary>PLT-58: Dept.HeadId and Person.DeptId, both NOT NULL, reference each other; the stop names the cycle.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Two_tables_that_reference_each_other_through_mandatory_columns_are_a_cycle_named_in_the_planter_s_words()
    {
        var order = Order.Of(Schema(("Dept", "HeadId", "Person", false), ("Person", "DeptId", "Dept", false)));

        var cycle = Assert.Single(order.Cycles);
        Assert.Equal([Table("Dept"), Table("Person")], cycle.Members);
        Assert.Equal("synthetic-copy.cycle", cycle.Error.Code);
        Assert.Contains("([dbo].[Dept] → [dbo].[Person] → [dbo].[Dept];", cycle.Error.Message, System.StringComparison.Ordinal);
        Assert.StartsWith("[dbo].[Dept] and [dbo].[Person] reference one another", cycle.Error.Message, System.StringComparison.Ordinal);
        Assert.Equal([Table("Dept"), Table("Person")], order.Tables.Select(s => s.Table));
    }

    /// <summary>Employee.DepartmentId NOT NULL to Department, Department.HeadId nullable to Employee, and the nullable self-reference
    /// Employee.ManagerId: both nullable references are planted NULL and set in the second pass, so Department comes first.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_cycle_through_an_optional_reference_is_broken_by_deferring_that_reference()
    {
        var order = Order.Of(Schema(("Employee", "DepartmentId", "Department", false), ("Department", "HeadId", "Employee", true),
            ("Employee", "ManagerId", "Employee", true)));

        Assert.Empty(order.Cycles);
        Assert.Equal([new Order.Deferral(Table("Department"), ForeignKey("Department", "HeadId")),
            new Order.Deferral(Table("Employee"), ForeignKey("Employee", "ManagerId"))], order.Deferred);
        Assert.Equal([Table("Department"), Table("Employee")], order.Tables.Select(s => s.Table));
    }

    /// <summary>PLT-51: ShipmentNote's (ShipmentId, LineNo) references Shipment's unique key as one foreign key, one edge; a NULL in
    /// either column makes all of it optional, and inside a cycle it is deferred as one.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_composite_reference_is_one_edge()
    {
        var key = ForeignKey("ShipmentNote", "Shipment");
        Table Note(bool nullable) => new(Table("ShipmentNote"),
            SortedArray.Of<Column>(Plain(1, "ShipmentId", false), Plain(2, "LineNo", nullable)), null, [], [new Reference(key, null,
                Table("Shipment"), [new(0, Name("ShipmentId"), Name("Id")), new(1, Name("LineNo"), Name("LineNo"))],
                ReferentialAction.NoAction, ReferentialAction.NoAction, false, nullable)], [], [], null);
        var shipment = Schema(("Shipment", "NoteId", "ShipmentNote", false)).Find(Table("Shipment"))!;

        var acyclic = Order.Of(new Schema([Note(false), shipment with { References = [] }]));
        var broken = Order.Of(new Schema([Note(true), shipment]));

        Assert.Equal([Table("Shipment"), Table("ShipmentNote")], acyclic.Tables.Select(s => s.Table));
        Assert.Equal([new Order.Deferral(Table("ShipmentNote"), key)], broken.Deferred);
        Assert.Empty(broken.Cycles);
        Assert.Equal([Table("ShipmentNote"), Table("Shipment")], broken.Tables.Select(s => s.Table));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_cycle_s_error_is_synthetic_copy_cycle_naming_its_path()
    {
        var cycle = Assert.Single(Order.Of(Schema(("Hen", "EggId", "Egg", false), ("Egg", "HenId", "Hen", false),
            ("Chick", "HenId", "Hen", false), ("Hen", "ChickId", "Chick", false))).Cycles);

        Assert.Equal([Table("Chick"), Table("Egg"), Table("Hen")], cycle.Members);
        Assert.Equal("synthetic-copy.cycle", cycle.Error.Code);
        Assert.StartsWith("[dbo].[Chick], [dbo].[Egg] and [dbo].[Hen] reference one another", cycle.Error.Message,
            System.StringComparison.Ordinal);
        Assert.Contains("([dbo].[Chick] → [dbo].[Hen] → [dbo].[Chick];", cycle.Error.Message, System.StringComparison.Ordinal);
    }

    /// <summary>PLT-57's stop names the foreign key, so a reader told to make a column of one nullable knows which: a table with two
    /// NOT NULL self-references names both, and a pair of tables names the key each step of the path follows.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_cycle_s_error_names_every_NOT_NULL_foreign_key_its_path_follows()
    {
        var self = Assert.Single(Order.Of(Schema(("Employee", "ManagerId", "Employee", false),
            ("Employee", "MentorId", "Employee", false))).Cycles);
        var pair = Assert.Single(Order.Of(Schema(("Dept", "HeadId", "Person", false), ("Person", "DeptId", "Dept", false),
            ("Person", "BadgeId", "Badge", false))).Cycles);

        Assert.Equal([ForeignKey("Employee", "ManagerId"), ForeignKey("Employee", "MentorId")], self.Keys);
        Assert.Contains("([dbo].[Employee] → [dbo].[Employee]; foreign keys [dbo].[FK_Employee_ManagerId] and "
            + "[dbo].[FK_Employee_MentorId])", self.Error.Message, System.StringComparison.Ordinal);
        Assert.Equal([ForeignKey("Dept", "HeadId"), ForeignKey("Person", "DeptId")], pair.Keys);
        Assert.Contains("([dbo].[Dept] → [dbo].[Person] → [dbo].[Dept]; foreign keys [dbo].[FK_Dept_HeadId] and "
            + "[dbo].[FK_Person_DeptId])", pair.Error.Message, System.StringComparison.Ordinal);
    }

    private static Dictionary<Name, int> Positions(Order order) => order.Tables.ToDictionary(s => s.Table, s => s.Position);

    private static Name Name(string part) => Ok(Kernel.Name.Of(part));

    private static Name Table(string table) => Ok(Kernel.Name.Of("dbo", table));

    private static ElementKey ForeignKey(string table, string column) => Key("ForeignKeyConstraint", "dbo", "FK_" + table + "_" + column);

    private static Column.Stored Plain(int ordinal, string name, bool nullable) =>
        new(ordinal, Name(name), nullable, SystemType("int"), null, null, null, false, false);

    /// <summary>
    /// Tables in dbo, each with an Id, and a foreign key FK_&lt;table&gt;_&lt;column&gt; for each (table, column, parent, nullable)
    /// written, through its own column; a parent written only as a parent is a table too.
    /// </summary>
    private static Schema Schema(params (string Table, string Column, string Parent, bool Nullable)[] references)
    {
        var tables = references.Select(r => r.Table).Concat(references.Select(r => r.Parent)).Distinct();
        return new Schema(SortedArray.Of(tables.Select(t =>
        {
            var own = references.Where(r => r.Table == t).ToArray();
            return new Table(Table(t),
                SortedArray.Of<Column>(own.Select((r, i) => Plain(i + 2, r.Column, r.Nullable)).Prepend(Plain(1, "Id", false))), null,
                [], SortedArray.Of(own.Select(r => new Reference(ForeignKey(t, r.Column), null, Table(r.Parent),
                    [new Reference.Leg(0, Name(r.Column), Name("Id"))], ReferentialAction.NoAction, ReferentialAction.NoAction, false,
                    r.Nullable))), [], [], null);
        })));
    }

    private static Graph Build(int n, (int Child, int Parent, bool Optional)[] edges)
    {
        var keyed = edges.Select((e, k) => (e.Child, e.Parent, e.Optional, Key: Key("ForeignKeyConstraint", "dbo", "FK_" + k))).ToArray();
        var tables = Enumerable.Range(0, n).Select(t =>
        {
            var own = keyed.Select((e, k) => (e, k)).Where(x => x.e.Child == t).ToArray();
            return new Table(Table("T" + t),
                SortedArray.Of<Column>(own.Select((x, i) => Plain(i + 2, "r" + x.k, x.e.Optional)).Prepend(Plain(1, "Id", false))), null,
                [], SortedArray.Of(own.Select(x => new Reference(x.e.Key, null, Table("T" + x.e.Parent),
                    [new Reference.Leg(0, Name("r" + x.k), Name("Id"))], ReferentialAction.NoAction, ReferentialAction.NoAction, false,
                    x.e.Optional))), [], [], null);
        });
        return new Graph(new Schema(SortedArray.Of(tables)), keyed);
    }
}
