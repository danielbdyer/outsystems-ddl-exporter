using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Estate.Cli;

namespace Estate.Budgets.Tests;

/// <summary>The documents as ci/docs.manifest.json lists them, and the sets each document law reads.</summary>
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
    /// Citations until M8, because they must name v1's and v2's retired terms, their counts, and verbs not yet built.
    /// </summary>
    public static readonly IReadOnlyList<string> RootDesign =
        ["V3_ARCHITECTURE.md", "V3_INSTRUCTION_ARCHITECTURE.md", "LIFECYCLE_BACKPORT_PROMPT.md", "V3_MILESTONES.md", "V3_BUILD_PROMPT.md"];

    /// <summary>
    /// The milestone in progress, <see cref="Contract.Milestone"/>: 0 through M0, and each milestone's exit raises it by
    /// one, so every rule below that reads it expires by itself (DECISIONS.md, 2026-09-23, the milestone in progress).
    /// </summary>
    public static readonly int Milestone = Contract.Milestone;

    /// <summary>
    /// M8, one tool: the root design documents leave for archive/design/ and ValuesResolve stops accepting a pending
    /// clause, so "until M8", the cutover tools' <c>pending W</c> and the root design documents' exclusion all end as it starts.
    /// </summary>
    public const int OneTool = 8;

    /// <summary>
    /// Whether M&lt;n&gt; has not exited while <paramref name="milestone"/> is in progress: <c>pending M&lt;n&gt;</c>, and a
    /// citation's <c>from M&lt;n&gt;</c> or <c>at M&lt;n&gt;</c>, hold through M&lt;n&gt; and fail once its exit raises the milestone past n.
    /// </summary>
    public static bool NotYetExited(int n, int milestone) => n >= milestone;

    /// <summary>Whether <paramref name="milestone"/> is before M&lt;n&gt; starts: what "until M&lt;n&gt;" holds while.</summary>
    public static bool Before(int n, int milestone) => milestone < n;

    /// <summary>Every hand-written markdown row, less the root design documents until M8: what NoRestatedCounts reads.</summary>
    public static IEnumerable<string> HandWritten => Rows
        .Where(r => r.Kind == "hand" && r.Path.EndsWith(".md", StringComparison.Ordinal) && !(Before(OneTool, Milestone) && RootDesign.Contains(r.Path)))
        .Select(r => r.Path);

    /// <summary>
    /// The engine's own documents, VALUES.md first: the hand-written markdown less DECISIONS.md, a dated log whose
    /// lines never change and often name what a decision rejected, and less a row a later milestone generates, whose
    /// register is the record's. Register.Prose and Citations read these.
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
