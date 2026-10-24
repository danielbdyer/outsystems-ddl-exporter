using System;
using System.IO;
using System.Linq;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class DiffTests
{
    public static TheoryData<string> WithChangeText => new(GoldenProject.SampleChanges.Where(n => File.Exists(GoldenProject.Case(n, "change.txt"))));

    [Theory]
    [Trait("Category", "build")]
    [MemberData(nameof(WithChangeText))]
    public void Diff_prints_each_sample_changes_change_txt(string name)
    {
        Environment.SetEnvironmentVariable("DBCHANGE_LITE_TOOL", Repo.Tool);
        using var scratch = new Scratch();
        var from = GoldenProject.Copy(scratch, "base");
        var to = GoldenProject.Copy(scratch, name).Edit(name);
        var (exit, answer) = Cli.Run(scratch.Path, "diff", "--from", "project:" + from.Folder, "--to", "project:" + to.Folder);
        Assert.True(exit == 0, answer.GetProperty("message").GetString());
        var printed = answer.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("message").GetString()!).Order(StringComparer.Ordinal).ToList();
        var expected = File.ReadAllLines(GoldenProject.Case(name, "change.txt")).Where(l => l.Length > 0).Order(StringComparer.Ordinal).ToList();
        Assert.True(expected.SequenceEqual(printed, StringComparer.Ordinal), name + ": change.txt says " + string.Join(" | ", expected) + "; diff says " + string.Join(" | ", printed));
    }
}
