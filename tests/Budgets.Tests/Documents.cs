using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Estate.Budgets.Tests;

/// <summary>The documents as ci/docs.manifest.json lists them, the sets each document law reads, the rows of VALUES.md, and each milestone's exits and whether it is complete.</summary>
internal static class Documents
{
    public sealed record Row(string Path, string Kind, string Owner, string[] Reader, string[] Moment, int? Budget, string? Generator, string? Becomes);

    public static IReadOnlyList<Row> Rows { get; } = JsonNode.Parse(Repository.Read("ci/docs.manifest.json"))!["documents"]!.AsArray()
        .Select(r => new Row(
            (string)r!["path"]!,
            (string?)r["kind"] ?? "",
            (string?)r["owner"] ?? "",
            r["reader"]?.AsArray().Select(x => (string)x!).ToArray() ?? [],
            r["moment"]?.AsArray().Select(x => (string)x!).ToArray() ?? [],
            (int?)r["budget"],
            (string?)r["generator"],
            (string?)r["becomes"]))
        .ToList();

    /// <summary>
    /// The five root design documents: manifest rows, but outside Register.Prose, Vocabulary, NoRestatedCounts and
    /// Citations until M8 starts, because they must name v1's and v2's retired terms, their counts, and verbs not yet built.
    /// </summary>
    public static readonly IReadOnlyList<string> RootDesign =
        ["V3_ARCHITECTURE.md", "V3_INSTRUCTION_ARCHITECTURE.md", "LIFECYCLE_BACKPORT_PROMPT.md", "V3_MILESTONES.md", "V3_BUILD_PROMPT.md"];

    /// <summary>M8, one tool: the root design documents leave for archive/design/ and the cutover tools' "not held yet" ends as it starts.</summary>
    public const int OneTool = 8;

    private static readonly Regex ValueRow = new(@"^\| ([A-Z]\d+) \|", RegexOptions.CultureInvariant);
    private static readonly Regex WorkPackageRow = new(@"^\| (\d)\.(\d+) \|", RegexOptions.CultureInvariant);
    private static readonly Regex MilestoneHeading = new(@"^## \d+\. M(\d)\b", RegexOptions.CultureInvariant);
    private static readonly Regex NumberedExit = new(@"^(\d+)\.\s", RegexOptions.CultureInvariant);

    /// <summary>A NEXT.md line naming an exit a person runs: M&lt;n&gt; exit &lt;k&gt; beside a backticked command starting estate.</summary>
    private static readonly Regex RunByAPerson = new(@"\bM(\d) exit (\d+)\b", RegexOptions.CultureInvariant);
    private static readonly Regex EstateCommand = new(@"`estate [^`]*`", RegexOptions.CultureInvariant);

    /// <summary>The rows of VALUES.md, S1 to G9, in the file's order.</summary>
    public static IReadOnlyList<string> Values { get; } = Repository.Lines("VALUES.md").Select(l => ValueRow.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();

    /// <summary>The work packages of V3_MILESTONES.md, as (milestone, number).</summary>
    public static IReadOnlySet<(int Milestone, int Number)> WorkPackages { get; } = Repository.Lines("V3_MILESTONES.md").Select(l => WorkPackageRow.Match(l)).Where(m => m.Success)
        .Select(m => (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToHashSet();

    /// <summary>
    /// Each milestone's exits as V3_MILESTONES.md numbers them: the numbered list after <c>**Exit.**</c> in the section headed
    /// <c>M&lt;n&gt; —</c>, or one exit when the section writes its exit as a paragraph; a milestone whose section has no
    /// <c>**Exit.**</c> (M8) lists none.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> Exits { get; } = ReadExits(Repository.Lines("V3_MILESTONES.md"));

    internal static IReadOnlyDictionary<int, IReadOnlyList<int>> ReadExits(IEnumerable<string> lines)
    {
        var exits = new Dictionary<int, IReadOnlyList<int>>();
        var (milestone, reading, numbered) = (-1, false, new List<int>());
        void Close()
        {
            if (reading)
            {
                exits[milestone] = numbered.Count > 0 ? [.. numbered] : [1];
            }

            (reading, numbered) = (false, []);
        }

        foreach (var line in lines)
        {
            if (MilestoneHeading.Match(line) is { Success: true } heading)
            {
                Close();
                milestone = int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith("**Exit.**", StringComparison.Ordinal) && milestone >= 0)
            {
                Close();
                reading = true;
            }
            else if (reading && NumberedExit.Match(line) is { Success: true } item)
            {
                numbered.Add(int.Parse(item.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            else if (reading && line.Length == 0)
            {
                Close();
            }
        }

        Close();
        return exits;
    }

    /// <summary>Every exit of the plan, as a trait names it: M0.1 to M7.1.</summary>
    public static IEnumerable<string> EveryExit => Exits.OrderBy(e => e.Key).SelectMany(e => e.Value.Select(k => Exit(e.Key, k)));

    /// <summary>The exits some test declares with [Trait("Exit", …)].</summary>
    public static IReadOnlySet<string> DeclaredExits { get; } = TestTraits.All.SelectMany(t => t.Values("Exit")).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether M&lt;n&gt; is complete (DECISIONS.md, 2026-09-25): its section lists exits, and each has a test declaring
    /// [Trait("Exit", "M&lt;n&gt;.&lt;k&gt;")], or a line of NEXT.md names it as M&lt;n&gt; exit &lt;k&gt; beside the backticked estate command a person runs.
    /// </summary>
    public static bool Complete(int n) => Complete(n, Exits, DeclaredExits, Repository.Lines("NEXT.md"));

    internal static bool Complete(int n, IReadOnlyDictionary<int, IReadOnlyList<int>> exits, IReadOnlySet<string> declared, IEnumerable<string> next) =>
        exits.TryGetValue(n, out var list) && list.All(k => declared.Contains(Exit(n, k)) || next.Any(line => EstateCommand.IsMatch(line)
            && RunByAPerson.Matches(line).Any(m => m.Groups[1].Value == n.ToString(CultureInfo.InvariantCulture) && m.Groups[2].Value == k.ToString(CultureInfo.InvariantCulture))));

    /// <summary>Whether M&lt;n&gt; has started: every milestone before it is complete.</summary>
    public static bool Started(int n) => Started(n, Complete);

    internal static bool Started(int n, Func<int, bool> complete) => Enumerable.Range(0, n).All(complete);

    /// <summary>An exit as a trait names it: M1.5.</summary>
    public static string Exit(int milestone, int exit) => string.Create(CultureInfo.InvariantCulture, $"M{milestone}.{exit}");

    /// <summary>Every hand-written markdown row, less the root design documents until M8 starts: what NoRestatedCounts reads.</summary>
    public static IEnumerable<string> HandWritten => Rows
        .Where(r => r.Kind == "hand" && r.Path.EndsWith(".md", StringComparison.Ordinal) && !(!Started(OneTool) && RootDesign.Contains(r.Path)))
        .Select(r => r.Path);

    /// <summary>
    /// The engine's own documents, VALUES.md first: the hand-written markdown less DECISIONS.md, a dated log whose
    /// lines never change and often name what a decision rejected, and less a row a later milestone generates, whose
    /// register is knowledge/description.md's. Register.Prose and Citations read these.
    /// </summary>
    public static IEnumerable<string> Engine => HandWritten
        .Where(p => p != "DECISIONS.md" && Rows.Single(r => r.Path == p).Becomes is null)
        .OrderBy(p => p == "VALUES.md" ? 0 : 1)
        .ThenBy(p => p, StringComparer.Ordinal);

    private static readonly Regex Opens = new(@"^(?:\||#|```|\s*(?:[-*]|\d+\.)\s)", RegexOptions.CultureInvariant);

    /// <summary>
    /// A document's logical lines, each with the physical line it starts on: a table row, a heading, a list item or a
    /// paragraph, with its soft-wrapped lines joined by a space.
    /// </summary>
    public static IEnumerable<(int Line, string Text)> Blocks(string path)
    {
        var lines = Repository.Lines(path);
        var block = new List<string>();
        var start = 0;
        for (var i = 0; i <= lines.Length; i++)
        {
            var ends = i == lines.Length || lines[i].Length == 0 || Opens.IsMatch(lines[i]);
            if (ends && block.Count > 0)
            {
                yield return (start + 1, string.Join(' ', block));
                block.Clear();
            }

            if (i < lines.Length && lines[i].Length > 0)
            {
                start = block.Count == 0 ? i : start;
                block.Add(lines[i].Trim());
            }
        }
    }
}
