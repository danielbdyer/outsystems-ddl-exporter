using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DbChange.Budgets.Tests.Register;

/// <summary>
/// The texts of dbchange's findings are in the register (finding D6 of round two): each finding the kernel, io and the cli construct
/// (Finding.Note, Finding.Warning, Finding.Error) is read from the sources, and every string its construction writes, the fixed parts of
/// its subject, message and remedy, goes through Prose as the documents do. A finding's variable parts (a key, a count, DacFx's own
/// text) are not read here, and neither is a remedy's form, which a fixed part alone does not show.
/// </summary>
public sealed class Findings
{
    /// <summary>A finding's construction and its arguments, parentheses balanced.</summary>
    private static readonly Regex Construction = new(@"Finding\.(?:Note|Warning|Error)\((?<arguments>(?:[^()""]|""(?:[^""\\]|\\.)*""|\((?<depth>)|\)(?<-depth>))*(?(depth)(?!)))\)", RegexOptions.CultureInvariant);

    private static readonly Regex Literal = new(@"""(?<text>(?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L1")]
    public void Every_text_a_finding_writes_is_in_the_register()
    {
        var texts = Written().ToList();

        Assert.Contains(texts, t => t.Text.Contains("is not verified against it", StringComparison.Ordinal));   // check drift's profile.unverified
        Assert.Empty(texts.SelectMany(t => Prose.Findings(t.Text).Select(f => t.File + ": " + f)));
    }

    /// <summary>The scan as a minimal pair: the strings of a finding's construction, nested calls included, and none of another call's.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_scan_reads_each_string_of_a_finding_s_construction_and_no_other() =>
        Assert.Equal(["drift.column", "on the target (", ") not in the repository."],
            TextsIn("Finding.Warning(\"drift.column\", key.ToString(), \"on the target (\" + Name(x) + \") not in the repository.\"); Other(\"not read\");"));

    /// <summary>Each string a finding's construction writes, in the kernel's, io's and the cli's sources, with its file.</summary>
    private static IEnumerable<(string File, string Text)> Written() => Repository.Files
        .Where(f => (f.StartsWith("kernel/", StringComparison.Ordinal) || f.StartsWith("io/", StringComparison.Ordinal) || f.StartsWith("cli/", StringComparison.Ordinal))
            && f.EndsWith(".cs", StringComparison.Ordinal))
        .SelectMany(f => TextsIn(Repository.Read(f)).Select(text => (File: f, Text: text)));

    private static IEnumerable<string> TextsIn(string source) =>
        Construction.Matches(source).SelectMany(c => Literal.Matches(c.Groups["arguments"].Value).Select(l => l.Groups["text"].Value.Replace("\\\"", "\"", StringComparison.Ordinal)));
}
