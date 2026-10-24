using System;
using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>
/// The planter's load order (V3_MILESTONES.md WP 3.1; V3_ARCHITECTURE.md §7.7): every table of a schema at a position, each table after
/// the tables it references; the references a first pass leaves unmet and a second pass sets, which are the optional references inside a
/// cycle; and the cycles of mandatory references, which no order of INSERTs satisfies, each with its error. A reference is an edge from
/// the referencing table to the referenced one, however many columns it has (PLT-51). Every optional reference inside a cycle (its two
/// tables reach each other) is deferred, which is simple and visible where a minimal set would need a feedback-set solver; it can plan a
/// second-pass UPDATE the planter did not strictly need, and changing that changes WP 3.8's planted rows (D1). Outside its cycles the
/// order is v2's PartialTopological (archive/v2/src/Projection.Core/TopologicalOrder.fs): a parent before its children, ties by name,
/// and a mandatory cycle's tables in name order at the cycle's place. A reference to a table the schema does not hold orders nothing.
/// </summary>
public sealed record Order(SortedArray<Order.Step> Tables, SortedArray<Order.Deferral> Deferred, SortedArray<Order.Cycle> Cycles)
{
    /// <summary>
    /// The order of a schema; total, since a cycle is a fact it reports. Tarjan's strongly connected components over every reference
    /// give the cycles; inside each, the optional references are deferred, and Tarjan over the mandatory ones alone gives the cycles
    /// reported. Kahn's algorithm then places the components of what is left, parents first, the smallest table name first among those
    /// ready. O((V + E) log V).
    /// </summary>
    public static Order Of(Schema schema)
    {
        var tables = schema.Tables;
        var index = new Dictionary<Name, int>();
        for (var i = 0; i < tables.Count; i++)
        {
            index[tables[i].Name] = i;
        }

        // Each table's references as edges to the parent's index, in the order of the parent's name, then the reference's key.
        var edges = tables.Select(t => t.References.Where(r => index.ContainsKey(r.Parent))
            .Select(r => (Parent: index[r.Parent], Reference: r)).OrderBy(e => e.Parent).ThenBy(e => e.Reference.Key).ToArray()).ToArray();
        var all = Components(edges.Select(es => es.Select(e => e.Parent).ToArray()).ToArray());
        var deferred = new List<Deferral>();
        var kept = new int[tables.Count][];
        for (var t = 0; t < tables.Count; t++)
        {
            var child = t;
            bool Deferrable((int Parent, Reference Reference) e) => e.Reference.Optional && all[e.Parent] == all[child];
            deferred.AddRange(edges[t].Where(Deferrable).Select(e => new Deferral(tables[child].Name, e.Reference.Key)));
            kept[t] = [.. edges[t].Where(e => !Deferrable(e)).Select(e => e.Parent)];
        }

        var mandatory = edges.Select(es => es.Where(e => !e.Reference.Optional).Select(e => e.Parent).ToArray()).ToArray();
        var block = Components(mandatory);
        var cycles = Enumerable.Range(0, tables.Count).GroupBy(t => block[t])
            .Where(g => g.Count() > 1 || mandatory[g.First()].Contains(g.First()))
            .Select(g => Reported(SortedArray.Of(g.Select(t => tables[t].Name)), Path(g.Min(), mandatory, block, tables)));
        return new Order(Placed(kept, block, tables), SortedArray.Of(deferred), SortedArray.Of(cycles));

        // A cycle with the NOT NULL foreign keys from each table of its path to the next.
        Cycle Reported(SortedArray<Name> members, SortedArray<Step> path) => new(members, path)
        {
            Keys = SortedArray.Of(path.SelectMany((s, i) => edges[index[s.Table]]
                .Where(e => !e.Reference.Optional && e.Parent == index[path[(i + 1) % path.Count].Table]).Select(e => e.Reference.Key))
                .Distinct()),
        };
    }

    /// <summary>A table at its place in the order, from 0.</summary>
    public sealed record Step(int Position, Name Table) : IComparable<Step>
    {
        public int CompareTo(Step? other) =>
            other is null ? 1 : Position.CompareTo(other.Position) is var c and not 0 ? c : Table.CompareTo(other.Table);
    }

    /// <summary>A reference whose columns the first pass leaves NULL and the second pass sets, and the table that declares it.</summary>
    public sealed record Deferral(Name Table, ElementKey Reference) : IComparable<Deferral>
    {
        public int CompareTo(Deferral? other) =>
            other is null ? 1 : Table.CompareTo(other.Table) is var c and not 0 ? c : Reference.CompareTo(other.Reference);
    }

    /// <summary>
    /// Tables that reference one another through mandatory references (PLT-57, PLT-58): every table of one strongly connected component
    /// of those references, in name order, and the shortest cycle through the first of them, in reference order (a self-reference's is
    /// that table alone). No order of INSERTs plants a row in any of them, so a person declares their rows.
    /// </summary>
    public sealed record Cycle(SortedArray<Name> Members, SortedArray<Step> Path) : IComparable<Cycle>
    {
        /// <summary>Every NOT NULL foreign key from a table of the path to the next one, which the error names (PLT-57).</summary>
        public SortedArray<ElementKey> Keys { get; init; } = [];

        /// <summary>The error the planter stops with, naming the tables and the path, the first table repeated at its end.</summary>
        public Error Error => new("synthetic-copy.cycle",
            (Members.Count == 1 ? Members[0] + " references itself through a NOT NULL foreign key"
                : string.Join(", ", Members.Take(Members.Count - 1)) + " and " + Members[^1]
                    + " reference one another through NOT NULL foreign keys")
                + " (" + string.Join(" → ", Path.Select(s => s.Table).Append(Path[0].Table)) + Named(Keys)
                + "), so no order of INSERTs plants a row in " + (Members.Count == 1 ? "it." : "any of them."),
            "Write a row of each table named into the change's declared witnesses, or make a column of one of those foreign keys"
                + " nullable.");

        public int CompareTo(Cycle? other) => other is null ? 1 : SortedArray.Compare(Members, other.Members);

        private static string Named(SortedArray<ElementKey> keys) => keys.Count switch
        {
            0 => "",
            1 => "; foreign key " + keys[0].Path,
            _ => "; foreign keys " + string.Join(", ", keys.Take(keys.Count - 1).Select(k => k.Path)) + " and " + keys[^1].Path,
        };
    }

    /// <summary>Tarjan's strongly connected components, iterative, visiting the tables in order: each table's component, numbered in
    /// the order the components complete.</summary>
    private static int[] Components(int[][] edges)
    {
        var n = edges.Length;
        var (order, low, component) = (new int[n], new int[n], new int[n]);
        Array.Fill(order, -1);
        var (stack, onStack, frames) = (new Stack<int>(), new bool[n], new Stack<(int Table, int Next)>());
        var (counter, components) = (0, 0);
        for (var root = 0; root < n; root++)
        {
            if (order[root] >= 0)
            {
                continue;
            }

            frames.Push((root, 0));
            (order[root], low[root]) = (counter, counter++);
            stack.Push(root);
            onStack[root] = true;
            while (frames.Count > 0)
            {
                var (t, next) = frames.Pop();
                if (next < edges[t].Length)
                {
                    frames.Push((t, next + 1));
                    var p = edges[t][next];
                    if (order[p] < 0)
                    {
                        (order[p], low[p]) = (counter, counter++);
                        stack.Push(p);
                        onStack[p] = true;
                        frames.Push((p, 0));
                    }
                    else if (onStack[p])
                    {
                        low[t] = Math.Min(low[t], order[p]);
                    }

                    continue;
                }

                if (frames.TryPeek(out var caller))
                {
                    low[caller.Table] = Math.Min(low[caller.Table], low[t]);
                }

                if (low[t] == order[t])
                {
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack[member] = false;
                        component[member] = components;
                    }
                    while (member != t);
                    components++;
                }
            }
        }

        return component;
    }

    /// <summary>The shortest cycle of mandatory references from <paramref name="first"/> back to it inside its component, by a
    /// breadth-first search that takes each table's parents in name order, so ties go to the smaller name.</summary>
    private static SortedArray<Step> Path(int first, int[][] mandatory, int[] block, SortedArray<Table> tables)
    {
        var before = new Dictionary<int, int>();
        var queue = new Queue<int>([first]);
        while (queue.TryDequeue(out var t))
        {
            foreach (var p in mandatory[t].Where(p => block[p] == block[first]))
            {
                if (p == first)
                {
                    var path = new List<int> { t };
                    while (path[^1] != first)
                    {
                        path.Add(before[path[^1]]);
                    }

                    path.Reverse();
                    return SortedArray.Of(path.Select((x, i) => new Step(i, tables[x].Name)));
                }

                if (before.TryAdd(p, t))
                {
                    queue.Enqueue(p);
                }
            }
        }

        throw new InvalidOperationException(tables[first].Name + " is on no cycle of its component.");
    }

    /// <summary>Kahn's algorithm over the components of the kept references: a component is placed once every component it references is,
    /// the one with the smallest table name first, its tables in name order.</summary>
    private static SortedArray<Step> Placed(int[][] kept, int[] block, SortedArray<Table> tables)
    {
        var members = Enumerable.Range(0, tables.Count).GroupBy(t => block[t]).ToDictionary(g => g.Key, g => g.Order().ToArray());
        var waiting = members.Keys.ToDictionary(b => b, _ => new HashSet<int>());
        var children = members.Keys.ToDictionary(b => b, _ => new HashSet<int>());
        for (var t = 0; t < tables.Count; t++)
        {
            foreach (var p in kept[t].Where(p => block[p] != block[t]))
            {
                waiting[block[t]].Add(block[p]);
                children[block[p]].Add(block[t]);
            }
        }

        var ready = new PriorityQueue<int, int>(members.Where(m => waiting[m.Key].Count == 0).Select(m => (m.Key, m.Value[0])));
        var placed = new List<Step>();
        while (ready.TryDequeue(out var b, out _))
        {
            var start = placed.Count;
            placed.AddRange(members[b].Select((t, i) => new Step(start + i, tables[t].Name)).ToArray());
            foreach (var child in children[b].Where(c => waiting[c].Remove(b) && waiting[c].Count == 0))
            {
                ready.Enqueue(child, members[child][0]);
            }
        }

        return SortedArray.Of(placed);
    }
}
