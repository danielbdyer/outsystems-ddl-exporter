using System.Linq;
using Xunit;

namespace DbChange.Budgets.Tests.Register;

/// <summary>
/// Every test's name, read as words, is in the register: no retired word and no banned form, as Prose holds the documents to, since
/// LAWS.md prints the names.
/// </summary>
public sealed class TestNames
{
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L1")]
    public void Every_test_name_read_as_words_is_in_the_register() =>
        Assert.Empty(TestTraits.All.SelectMany(t => Prose.Findings(t.English).Select(f => t.FullName + ": " + f)));

    /// <summary>
    /// S18 of the pre-M2 review: what --help prints is in the register, each verb's summary and each outcome's meaning, and each exit's
    /// meaning and remedy, as Prose holds the documents to; the verb table once said "with a receipt" and "an engine outside the pinned
    /// window", which no test read.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L1")]
    public void Every_text_the_help_prints_is_in_the_register() =>
        Assert.Empty(Cli.Contract.Verbs.SelectMany(v => ((string[])[v.Summary, .. (v.Outcomes ?? []).Select(o => o.Meaning)]).Select(text => (v.Name, Text: text)))
            .Concat(Cli.Contract.Exits.SelectMany(e => new[] { (e.Name, Text: e.Meaning), (e.Name, Text: e.Remedy) }))
            .SelectMany(t => Prose.Findings(t.Text).Select(f => t.Name + ": " + f)));
}
