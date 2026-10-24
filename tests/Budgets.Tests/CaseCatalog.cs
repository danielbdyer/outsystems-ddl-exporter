using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// The Facsimile's case catalog under tests/Cases/ (V3_MILESTONES.md WP 2.6), held as its Python lint held it in archive/v2 before the
/// move. Every case of a family file (00-invariants.md to 80-twin-parity.md) is a level-3 heading <c>### ID · subject</c> and one
/// fenced block whose info string is <c>case</c>, in the grammar of tests/Cases/README.md: known fields, the fields its kind requires,
/// a law the catalog defines, a source marked receipt, expected or verify, a receipt with its citation, and an ID no other case holds.
/// The catalog covers every law with two cases, every operation in its standard situations or with a stated n/a, every question kind
/// but Unknown with a corpus case, every stop kind with a case, and each of the Twin's 36 responsibilities with a TWN case naming its
/// row. tests/Cases/90-traceability.md is generated from the cases, and this test writes it again when it is stale.
/// </summary>
public sealed class CaseCatalog
{
    /// <summary>The catalog's folder, relative to the repository's root.</summary>
    internal const string Folder = "tests/Cases";

    private const string Matrix = Folder + "/90-traceability.md";

    private static readonly string[] Laws = [.. new[] { ('F', 9), ('H', 7), ('P', 6), ('S', 6), ('D', 6), ('M', 4), ('E', 4), ('A', 5),
        ('T', 4) }.SelectMany(f => Enumerable.Range(1, f.Item2).Select(n => f.Item1 + n.ToString(CultureInfo.InvariantCulture)))];

    private static readonly Dictionary<string, string[]> Required = new(StringComparer.Ordinal)
    {
        ["pure"] = ["kind", "expect", "laws", "source"],
        ["sql"] = ["kind", "expect", "laws", "source"],
        ["corpus"] = ["kind", "env", "change", "expect", "laws", "source"],
        ["process"] = ["kind", "expect", "laws", "source"],
        ["manual"] = ["kind", "expect", "laws", "source"],
    };

    private static readonly HashSet<string> Fields = new(
        ["kind", "tags", "input", "env", "change", "setup", "action", "expect", "laws", "source", "note"], StringComparer.Ordinal);

    private static readonly string[] QuestionKinds = ["HasRows", "NotNull", "Fits", "Unique", "Orphan", "CheckFalse", "KeyLength",
        "Rebuild", "Computed", "Temporal", "Scan", "Authored", "SeedDrift", "Unknown"];

    private static readonly string[] Stops = ["unreachable", "no-access", "missing-object", "stale-answers", "authored-script",
        "check-witness", "computed-witness", "planter-needs-row", "acceptance", "identity", "in-flight", "config", "toolchain", "platform",
        "options-unknown", "build-failed", "unknown-statement", "unclassified", "cdc", "base-unpublishable", "usage", "probe-failed",
        "answers-invalid", "witness-failed", "witness-not-synthetic", "identifier-unsafe", "interrupted", "internal"];

    /// <summary>The operations of v2's catalog (archive/v2/ssdt-agent/skills/op/), each with its OP- cases.</summary>
    internal static readonly string[] Operations = ["add-check", "add-default", "add-index", "add-mandatory", "add-optional",
        "add-unique", "archive-entity", "audit-columns", "change-delete-rule", "create-entity", "create-fk-clean", "create-fk-orphan",
        "create-static-seed", "define-pk", "delete-attribute", "delete-entity", "delete-seed-value", "drop-check", "drop-default",
        "drop-fk", "drop-index", "drop-pk", "drop-unique", "edit-seed", "extract-to-lookup", "identity-swap", "junction",
        "make-mandatory", "make-optional", "merge-tables", "modify-default", "modify-index", "move-attribute", "move-schema", "narrow",
        "rebuild-index", "rename-attribute", "rename-entity", "retype-explicit", "retype-implicit", "split-table", "temporal-convert",
        "temporal-new", "toggle-trust", "widen"];

    private static readonly string[] Situations = ["clean", "edge", "empty", "remedy", "violating"];

    private const int TwinResponsibilities = 36;

    private static readonly Regex Heading = new(@"^### +([A-Za-z0-9][A-Za-z0-9\-]*) +· +(.+?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex FieldLine = new(@"^([a-z]+):\s?(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex LawId = new(@"\b([FHPSDMEAT]\d)\b", RegexOptions.CultureInvariant);
    private static readonly Regex OperationCase = new(@"^OP-(.+)-\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex Family = new(@"^\d\d-.+\.md$", RegexOptions.CultureInvariant);
    private static readonly Regex Row = new(@"(?:row|#)\s?(\d{1,2})\b", RegexOptions.CultureInvariant);

    /// <summary>A case: its ID, its subject, its file and heading line, and its fields in the order the case writes them.</summary>
    internal sealed record Case(string Id, string Title, string File, int Line, IReadOnlyList<(string Name, string Value)> Fields)
    {
        public string this[string field] => Fields.FirstOrDefault(f => f.Name == field).Value ?? "";

        public string Kind => this["kind"].Split(default(char[]), StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        public string Where => File + ":" + Line.ToString(CultureInfo.InvariantCulture) + ": " + Id;
    }

    /// <summary>The cases of every family file, in file order, and the errors their grammar raises.</summary>
    internal static (IReadOnlyList<Case> Cases, IReadOnlyList<string> Errors) Read()
    {
        var (cases, errors) = (new List<Case>(), new List<string>());
        foreach (var file in Families())
        {
            var (some, wrong) = Parse(Path.GetFileName(file), Repository.Lines(file));
            cases.AddRange(some);
            errors.AddRange(wrong);
        }

        return (cases, errors);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Every_case_is_in_the_catalog_s_grammar_cites_laws_it_defines_and_a_source_and_holds_an_ID_no_other_case_holds()
    {
        var (cases, errors) = Read();

        Assert.NotEmpty(cases);
        None(errors.Concat(Checked(cases)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_catalog_covers_each_law_operation_question_kind_stop_and_responsibility_of_v2_s_synthetic_copy() => None(Gaps(Read().Cases));

    /// <summary>
    /// The matrix is a function of the cases: written twice it is the same bytes, LF and UTF-8 without a byte order mark; the committed
    /// file is those bytes, and where it is stale the test writes them and fails, so the commit that changes a case carries the matrix.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_traceability_matrix_is_what_the_cases_generate_and_is_written_again_when_stale()
    {
        var cases = Read().Cases;
        var (first, second) = (Generated(cases), Generated(cases));
        var path = Path.Combine(Repository.Root, Matrix);
        var committed = File.Exists(path) ? File.ReadAllText(path) : "";

        Assert.Equal(first, second);
        Assert.Matches(Manifest.Banner, first.Split('\n')[0]);
        if (committed != first)
        {
            File.WriteAllText(path, first, new UTF8Encoding(false));
        }

        Assert.True(committed == first, Matrix + " was stale against the cases and is written again: commit it");
    }

    /// <summary>
    /// The catalog's own prose, outside the case blocks, restates no count NoRestatedCounts names; it points to the generated matrix. A
    /// case block's fields are data, where 'exit 3' above 'laws:' is no count.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L2")]
    public void The_catalog_s_prose_restates_no_count_and_points_to_the_generated_matrix() =>
        None(Prose().SelectMany(p => NoRestatedCounts.Restated(p.Text).Select(m =>
            p.File + ":" + p.Line.ToString(CultureInfo.InvariantCulture) + ": '" + m + "' restates a count; name " + Matrix
            + ", which carries it, or drop the number")));

    /// <summary>
    /// Every path the catalog cites relative to its own folder resolves from tests/Cases/: a backticked path that opens with ../ and a
    /// markdown link, as v2's ssdt-agent gates checked them where the catalog stood before (their R1). A sibling file it names bare, such
    /// as `ARCHITECTURE.md` or `20-planter.md`, exists beside it.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_path_the_catalog_cites_from_its_own_folder_resolves()
    {
        var relative = new Regex(@"`((?:\.\./)+[\w./ -]+?(?:\.(?:md|sql|xml|json|refactorlog|cs|py|fs|mjs)|/))`", RegexOptions.CultureInvariant);
        var link = new Regex(@"\]\((?![a-z]+:)([^)\s#]+\.md)\)", RegexOptions.CultureInvariant);
        var sibling = new Regex(@"`((?:\d\d-[a-z0-9-]+|ARCHITECTURE|INVARIANTS|README)\.md|cases/[^`]*)`", RegexOptions.CultureInvariant);
        var unresolved = Documents.Rows.Select(r => r.Path).Where(p => p.StartsWith(Folder + "/", StringComparison.Ordinal))
            .SelectMany(file => Repository.Lines(file).SelectMany((line, i) => relative.Matches(line).Concat(link.Matches(line))
                .Concat(sibling.Matches(line)).Select(m => m.Groups[1].Value)
                .Where(cited => !Repository.Contains(Normalized(Folder + "/" + cited)))
                .Select(cited => file + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + cited + " does not resolve")));

        None(unresolved);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("kind:     pure\nexpect:   x\nlaws:     F1\nsource:   expected", "")]
    [InlineData("kind:     pure\nexpect:   x\nlaws:     F1\nsource:   receipt", "receipt without citation")]
    [InlineData("kind:     pure\nexpect:   x\nlaws:     Z1\nsource:   expected", "no law cited")]
    [InlineData("kind:     pure\nexpect:   x\nlaws:     F10 F1\nsource:   expected", "")]
    [InlineData("kind:     pure\nexpect:   x\nlaws:     A9\nsource:   expected", "unknown law A9")]
    [InlineData("kind:     corpus\nexpect:   outcome x\nlaws:     F1\nsource:   expected", "missing field 'env' for kind corpus, missing field 'change' for kind corpus")]
    [InlineData("kind:     pure\nexpect:   x\nlaws:     F1\nsource:   expected\nwhen:     now", "unknown field 'when'")]
    [InlineData("kind:     pure\nexpect:   x\nlaws:     F1\nsource:   guessed", "source marker 'guessed' not receipt|expected|verify")]
    [InlineData("kind:     pure\nexpect:   x\n  continued\nlaws:     F1\nsource:   expected", "")]
    [InlineData("kind:     pure\nexpect:   x\nstray\nlaws:     F1\nsource:   expected", "stray line: stray")]
    public void The_lint_refuses_a_case_outside_the_grammar_and_names_why(string block, string error)
    {
        var lines = ("### QST-01 · a case\n```case\n" + block + "\n```\n").Split('\n');

        var (cases, errors) = Parse("10-questions.md", lines);

        Assert.Equal(error, string.Join(", ", errors.Concat(Checked(cases)).Select(e => e.Split("QST-01: ", 2)[^1])));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_lint_refuses_a_heading_outside_the_grammar_a_heading_with_no_case_block_and_an_ID_twice()
    {
        var (cases, errors) = Parse("10-questions.md", ["### QST-01 a case", "### QST-02 · no block", "text", "### QST-03 · one",
            "```case", "kind:     pure", "expect:   x", "laws:     F1", "source:   expected", "```", "### QST-03 · two", "```case",
            "kind:     pure", "expect:   x", "laws:     F1", "source:   expected", "```"]);

        Assert.Equal(["10-questions.md:1: heading not in grammar: ### QST-01 a case", "10-questions.md:2: QST-02: no ```case block after heading",
            "duplicate id QST-03: 10-questions.md:4, 10-questions.md:11"], errors.Concat(Checked(cases)));
    }

    /// <summary>Fails naming every finding, one a line.</summary>
    private static void None(IEnumerable<string> findings)
    {
        var all = findings.ToList();
        Assert.True(all.Count == 0, string.Join('\n', all));
    }

    /// <summary>The family files, 00- to 80-, in ordinal order; README.md, ARCHITECTURE.md, INVARIANTS.md and the matrix hold no
    /// case.</summary>
    private static IEnumerable<string> Families() => Repository.Files
        .Where(f => f.StartsWith(Folder + "/", StringComparison.Ordinal) && f.Count(c => c == '/') == 2)
        .Where(f => Family.IsMatch(Path.GetFileName(f)) && !Path.GetFileName(f).StartsWith("90-", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal);

    /// <summary>The cases of one file and the errors of its grammar: a heading out of grammar, a heading with no case block, a field the
    /// grammar does not name or names twice, and a line that is neither a field nor its continuation.</summary>
    internal static (List<Case> Cases, List<string> Errors) Parse(string file, IReadOnlyList<string> lines)
    {
        var (cases, errors) = (new List<Case>(), new List<string>());
        var i = 0;
        while (i < lines.Count)
        {
            var heading = Heading.Match(lines[i]);
            if (!heading.Success)
            {
                if (lines[i].StartsWith("### ", StringComparison.Ordinal))
                {
                    errors.Add(file + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": heading not in grammar: "
                        + lines[i][..Math.Min(80, lines[i].Length)]);
                }

                i++;
                continue;
            }

            var (id, title, at) = (heading.Groups[1].Value, heading.Groups[2].Value, i + 1);
            var where = file + ":" + at.ToString(CultureInfo.InvariantCulture) + ": " + id + ": ";
            var j = i + 1;
            while (j < lines.Count && lines[j].Trim().Length == 0)
            {
                j++;
            }

            if (j >= lines.Count || lines[j].Trim() != "```case")
            {
                errors.Add(where + "no ```case block after heading");
                i++;
                continue;
            }

            var k = j + 1;
            var fields = new List<(string Name, string Value)>();
            string? current = null;
            for (; k < lines.Count && lines[k].Trim() != "```"; k++)
            {
                var line = lines[k];
                var field = FieldLine.Match(line);
                if (field.Success && !line.StartsWith(' ') && Fields.Contains(field.Groups[1].Value))
                {
                    current = field.Groups[1].Value;
                    if (fields.Any(f => f.Name == current))
                    {
                        errors.Add(where + "duplicate field " + current);
                    }

                    fields.Add((current, field.Groups[2].Value.Trim()));
                }
                else if (field.Success && !line.StartsWith(' '))
                {
                    errors.Add(where + "unknown field '" + field.Groups[1].Value + "'");
                    current = null;
                }
                else if (current is not null && (line.StartsWith(' ') || line.Trim().Length == 0))
                {
                    var last = fields.FindLastIndex(f => f.Name == current);
                    fields[last] = (current, fields[last].Value + "\n" + line.Trim());
                }
                else if (line.Trim().Length > 0)
                {
                    errors.Add(where + "stray line: " + line[..Math.Min(60, line.Length)]);
                }
            }

            cases.Add(new Case(id, title, file, at, fields));
            i = k + 1;
        }

        return (cases, errors);
    }

    /// <summary>What the cases break together: an ID held twice, a kind outside the five or a field its kind requires missing, a corpus
    /// case with no outcome, no law or one the catalog does not define, and a source not marked receipt, expected or verify, or a receipt
    /// with no citation.</summary>
    internal static IEnumerable<string> Checked(IReadOnlyList<Case> cases)
    {
        foreach (var twice in cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            yield return "duplicate id " + twice.Key + ": " + string.Join(", ",
                twice.Select(c => c.File + ":" + c.Line.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var c in cases)
        {
            if (!Required.TryGetValue(c.Kind, out var required))
            {
                yield return c.Where + ": bad kind '" + c["kind"] + "'";
            }
            else
            {
                foreach (var missing in required.Where(r => c[r].Length == 0))
                {
                    yield return c.Where + ": missing field '" + missing + "' for kind " + c.Kind;
                }

                if (c.Kind == "corpus" && !c["expect"].Contains("outcome", StringComparison.Ordinal))
                {
                    yield return c.Where + ": corpus case without an expect outcome";
                }
            }

            var laws = LawsOf(c).ToList();
            if (laws.Count == 0)
            {
                yield return c.Where + ": no law cited";
            }

            foreach (var unknown in laws.Where(l => !Laws.Contains(l)))
            {
                yield return c.Where + ": unknown law " + unknown;
            }

            var marker = Marker(c);
            if (marker is not ("receipt" or "expected" or "verify"))
            {
                yield return c.Where + ": source marker '" + marker + "' not receipt|expected|verify";
            }
            else if (marker == "receipt" && c["source"][marker.Length..].Trim(' ', '·').Length < 4)
            {
                yield return c.Where + ": receipt without citation";
            }
        }
    }

    /// <summary>The coverage the catalog lacks, one line each.</summary>
    internal static IEnumerable<string> Gaps(IReadOnlyList<Case> cases)
    {
        var index = Index(cases);
        foreach (var law in Laws.Where(l => index.Laws[l].Count < 2))
        {
            yield return "law " + law + ": " + index.Laws[law].Count.ToString(CultureInfo.InvariantCulture) + " case(s)";
        }

        foreach (var operation in Operations)
        {
            if (index.Operations[operation].Count == 0)
            {
                yield return "op " + operation + ": no cases";
            }
            else if (Situations.Where(s => !index.Tags[operation].Contains(s)).ToList() is { Count: > 0 } missing)
            {
                yield return "op " + operation + ": missing tags " + string.Join(", ", missing);
            }
        }

        foreach (var unknown in index.Operations.Keys.Where(o => !Operations.Contains(o)))
        {
            yield return "op cases for unknown op slug '" + unknown + "'";
        }

        foreach (var kind in QuestionKinds.Where(q => q != "Unknown" && index.Questions[q].Count == 0))
        {
            yield return "question kind " + kind + ": no corpus case";
        }

        foreach (var stop in Stops.Where(s => index.Stops[s].Count == 0))
        {
            yield return "stop " + stop + ": no case";
        }

        foreach (var row in Enumerable.Range(1, TwinResponsibilities).Where(r => index.Twin[r].Count == 0))
        {
            yield return "twin responsibility row " + row.ToString(CultureInfo.InvariantCulture) + ": no TWN case cites it";
        }
    }

    private sealed record Indexed(Dictionary<string, SortedSet<string>> Laws, Dictionary<string, List<string>> Operations,
        Dictionary<string, SortedSet<string>> Tags, Dictionary<string, SortedSet<string>> Questions,
        Dictionary<string, SortedSet<string>> Stops, Dictionary<int, SortedSet<string>> Twin);

    /// <summary>The catalog indexed: each law's cases, each operation's cases and the situations its tags and n/a notes name, each
    /// question kind's corpus cases, each stop's cases and each Twin responsibility's TWN cases.</summary>
    private static Indexed Index(IReadOnlyList<Case> cases)
    {
        SortedSet<string> Set() => new(StringComparer.Ordinal);
        var index = new Indexed(Laws.ToDictionary(l => l, _ => Set()), Operations.ToDictionary(o => o, _ => new List<string>()),
            Operations.ToDictionary(o => o, _ => Set()), QuestionKinds.ToDictionary(q => q, _ => Set()), Stops.ToDictionary(s => s, _ => Set()),
            Enumerable.Range(1, TwinResponsibilities).ToDictionary(r => r, _ => Set()));
        foreach (var c in cases)
        {
            foreach (var law in LawsOf(c).Where(index.Laws.ContainsKey))
            {
                index.Laws[law].Add(c.Id);
            }

            if (OperationCase.Match(c.Id) is { Success: true } op)
            {
                var name = op.Groups[1].Value;
                if (!index.Operations.ContainsKey(name))
                {
                    (index.Operations[name], index.Tags[name]) = ([], Set());
                }

                index.Operations[name].Add(c.Id);
                index.Tags[name].UnionWith(Regex.Split(c["tags"], @"[\s,·]+").Where(t => t.Length > 0).Select(t => t.Split(':')[0]));
                index.Tags[name].UnionWith(Regex.Matches(c["note"], @"n/a\s+([a-z]+)").Select(m => m.Groups[1].Value));
            }

            if (c["kind"].StartsWith("corpus", StringComparison.Ordinal))
            {
                foreach (var kind in QuestionKinds.Where(q => Regex.IsMatch(c["expect"], @"\b" + q + @"\b")))
                {
                    index.Questions[kind].Add(c.Id);
                }
            }

            foreach (var stop in Stops.Where(s => Regex.IsMatch(c["expect"], @"stop\s+" + Regex.Escape(s) + @"\b")))
            {
                index.Stops[stop].Add(c.Id);
            }

            if (c.Id.StartsWith("TWN", StringComparison.Ordinal))
            {
                var text = string.Join(' ', new[] { c.Title, c.Id, c.Title, c.File, c.Line.ToString(CultureInfo.InvariantCulture) }
                    .Concat(c.Fields.Select(f => f.Value)));
                foreach (var row in Row.Matches(text).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                    .Where(r => r is >= 1 and <= TwinResponsibilities))
                {
                    index.Twin[row].Add(c.Id);
                }
            }
        }

        return index;
    }

    private static IEnumerable<string> LawsOf(Case c) => LawId.Matches(c["laws"]).Select(m => m.Groups[1].Value);

    private static string Marker(Case c) =>
        c["source"].Split(default(char[]), StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('·').ToLowerInvariant() ?? "";

    /// <summary>The matrix: the banner, the cases by file, kind and source, then which cases witness each law, operation, question
    /// kind, stop and Twin responsibility, then the open verify cases of each file; LF line endings and no timestamp.</summary>
    internal static string Generated(IReadOnlyList<Case> cases)
    {
        var index = Index(cases);
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append('\n');
        static string Ids(IEnumerable<string> ids, int most) =>
            string.Join(' ', ids.Take(most)) + (ids.Count() > most ? " …" : "");
        static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

        Line("<!-- generated by Budgets.Tests: CaseCatalog from the case files of tests/Cases/; do not edit -->");
        Line("# Traceability");
        Line();
        Line("What the cases of this folder witness, as the catalog's lint (`Budgets.Tests: CaseCatalog`) reads them: the cases by file,");
        Line("kind and source; the cases of each law, operation, question kind, stop and Twin responsibility (`ARCHITECTURE.md` §2); and");
        Line("the open `verify` cases. The lint writes this file again whenever a case changes; it is never edited by hand.");
        Line();
        Line("## Cases by file, kind and source");
        Line();
        Line("| File | Cases | " + string.Join(" | ", Required.Keys) + " | receipt | expected | verify |");
        Line("|---|---:|" + string.Concat(Enumerable.Repeat("---:|", Required.Count + 3)));
        foreach (var file in cases.GroupBy(c => c.File).Concat(cases.GroupBy(_ => "all")))
        {
            Line("| " + (file.Key == "all" ? "all" : "`" + file.Key + "`") + " | " + N(file.Count()) + " | "
                + string.Join(" | ", Required.Keys.Select(k => N(file.Count(c => c.Kind == k)))) + " | "
                + string.Join(" | ", new[] { "receipt", "expected", "verify" }.Select(s => N(file.Count(c => Marker(c) == s)))) + " |");
        }

        Line();
        Line("## Law → cases");
        Line();
        Line("| Law | Cases | IDs |");
        Line("|---|---:|---|");
        foreach (var law in Laws.OrderBy(l => "FHPSDMEAT".IndexOf(l[0], StringComparison.Ordinal))
            .ThenBy(l => int.Parse(l[1..], CultureInfo.InvariantCulture)))
        {
            Line("| " + law + " | " + N(index.Laws[law].Count) + " | " + Ids(index.Laws[law], 40) + " |");
        }

        Line();
        Line("## Operation → cases (standard set)");
        Line();
        Line("| Operation | Cases | Tags present |");
        Line("|---|---:|---|");
        foreach (var operation in Operations)
        {
            Line("| " + operation + " | " + N(index.Operations[operation].Count) + " | " + string.Join(' ', index.Tags[operation]) + " |");
        }

        Line();
        Line("## Question kind → corpus cases");
        Line();
        Line("| Kind | Corpus cases |");
        Line("|---|---|");
        foreach (var kind in QuestionKinds)
        {
            Line("| " + kind + " | " + N(index.Questions[kind].Count) + ": " + Ids(index.Questions[kind], 25) + " |");
        }

        Line();
        Line("## Stop → cases");
        Line();
        Line("| Stop | Cases |");
        Line("|---|---|");
        foreach (var stop in Stops)
        {
            Line("| " + stop + " | " + N(index.Stops[stop].Count) + ": " + Ids(index.Stops[stop], 20) + " |");
        }

        Line();
        Line("## Twin responsibility (ARCH §2) → cases");
        Line();
        Line("| Row | Cases |");
        Line("|---:|---|");
        foreach (var row in Enumerable.Range(1, TwinResponsibilities))
        {
            Line("| " + N(row) + " | " + string.Join(' ', index.Twin[row]) + " |");
        }

        Line();
        Line("## Open `verify` cases");
        Line();
        Line("Each is settled by its first run, then sourced again as a receipt. The questions that cross families are `ARCHITECTURE.md`");
        Line("§11.");
        foreach (var file in cases.Where(c => Marker(c) == "verify").GroupBy(c => c.File))
        {
            Line();
            Line("### `" + file.Key + "` (" + N(file.Count()) + ")");
            Line();
            Line("| Case | Situation | What is uncertain |");
            Line("|---|---|---|");
            foreach (var c in file)
            {
                Line("| " + c.Id + " | " + Cell(c.Title) + " | " + Cell(c["source"]["verify".Length..].Trim(' ', '·')) + " |");
            }
        }

        return text.ToString();
    }

    /// <summary>A value as one table cell: its lines joined by a space and its pipes escaped.</summary>
    private static string Cell(string value) => value.Replace("\n", " ", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>The catalog's logical lines outside its case blocks and case headings, with the file and the line each starts on: the
    /// prose NoRestatedCounts' rule reads.</summary>
    private static IEnumerable<(string File, int Line, string Text)> Prose() =>
        Documents.Rows.Where(r => r.Kind == "hand" && r.Path.StartsWith(Folder + "/", StringComparison.Ordinal)).SelectMany(r =>
        {
            var fenced = new HashSet<int>();
            var lines = Repository.Lines(r.Path);
            for (var (i, inside) = (0, false); i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    inside = !inside;
                    fenced.Add(i + 1);
                }
                else if (inside)
                {
                    fenced.Add(i + 1);
                }
            }

            return Documents.Blocks(r.Path).Where(b => !fenced.Contains(b.Line) && !Heading.IsMatch(b.Text))
                .Select(b => (r.Path, b.Line, b.Text));
        });

    private static string Normalized(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part == "..")
            {
                parts.RemoveAt(parts.Count - 1);
            }
            else if (part is not ("." or ""))
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }
}
