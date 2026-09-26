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
}
