using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Cli;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// WP 2.4's exit: each sample change of tests/Golden/changes/, built and read by DacFx, classifies against the seeded recognize: blocks of
/// tests/Golden/patterns/ as the classify.txt beside its edits states, line for line; and each rename made by typing is refused with the
/// remedy that writes its refactorlog entry, at exit 9.
/// </summary>
public sealed class SampleClassificationTests
{
    private static readonly string Golden = Path.Combine(Repository.Root, "tests", "Golden");

    public static TheoryData<string> Samples => new(GoldenProject.SampleChanges.Select(c => c.Name));

    public static TheoryData<string, string> TypedRenames => new()
    {
        { "rename-a-column-without-the-refactorlog", "rename.column-without-entry" },
        { "rename-a-table-without-the-refactorlog", "rename.table-without-entry" },
        { "move-a-table-to-another-schema-without-the-refactorlog", "rename.schema-move-without-entry" },
    };

    [Theory]
    [Trait("Category", "build")]
    [MemberData(nameof(Samples))]
    public async Task Each_sample_change_classifies_as_the_operation_its_classify_txt_names(string name)
    {
        var expected = Path.Combine(Golden, "changes", name, "classify.txt");
        Assert.True(File.Exists(expected), "tests/Golden/changes/" + name + "/ holds no classify.txt: write the lines it classifies as");

        var classification = await Classified(name);

        Assert.Equal(File.ReadAllLines(expected).Where(l => l.Length > 0), classification.Lines);
        Assert.StartsWith("provisional: ", classification.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "build")]
    [MemberData(nameof(TypedRenames))]
    public async Task Each_rename_made_by_typing_is_refused_at_exit_9_with_the_remedy_that_writes_its_refactorlog_entry(string name,
        string code)
    {
        var classification = await Classified(name);

        var refusal = Assert.Single(classification.Findings, f => f.Severity == Severity.Error);
        Assert.Equal(code, refusal.Code);
        Assert.StartsWith("Run dbchange diff --refactorlog to write the entry", refusal.Remedy, StringComparison.Ordinal);
        Assert.Equal(9, Contract.ExitByCategory(ErrorCode.Parse(code.Split('.')[0])!.Value));
    }

    /// <summary>The seeded blocks parse, one per file, each named for its file.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_seeded_recognize_blocks_parse_one_operation_per_file()
    {
        var patterns = Expect.Value(Patterns.Read(Path.Combine(Golden, "patterns")));

        Assert.Equal(Directory.GetFiles(Path.Combine(Golden, "patterns"), "*.md").Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal), patterns.Select(p => p.Operation));
        Assert.Empty(Expect.Value(Patterns.Read(Path.Combine(Golden, "no-such-folder"))));
    }

    private static async Task<ChangeClassification> Classified(string name)
    {
        var (built, head) = (await GoldenProject.Model(), await GoldenProject.Model(GoldenProject.Change(name)));
        var collation = Expect.Value(Ssdt.CollationOf(head.Elements));
        var change = Expect.Value(Change.Between(built.Elements, head.Elements, head.Renames, collation));
        return Classify.Of(built.Elements, head.Elements, change, Expect.Value(Patterns.Read(Path.Combine(Golden, "patterns"))),
            Classify.Inputs.None with { Collation = collation });
    }
}
