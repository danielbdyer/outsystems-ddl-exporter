using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Estate.Budgets.Tests.Register;

/// <summary>
/// Every test's name, read as words, is in the register: no retired word and no banned form, as Prose holds the documents to, since
/// LAWS.md prints the names. A class whose names carry a word the kernel still uses is excused with its reason until the rename that
/// retires the word reaches the kernel.
/// </summary>
public sealed class TestNames
{
    private static readonly IReadOnlyDictionary<string, string> Excused = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Estate.Kernel.Tests.ReceiptTests"] = "names the kernel's Receipt and Engine, which NEXT.md's provenance record renames",
        ["Estate.Io.Tests.DriftTests"] = "names the receipt and its engine as the kernel does, until the provenance record renames them",
        ["Estate.Io.Tests.DiffTests"] = "names the engine as the kernel does, until the provenance record renames it",
    };

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L1")]
    public void Every_test_name_read_as_words_is_in_the_register()
    {
        Assert.Empty(TestTraits.All.Where(t => !Excused.ContainsKey(t.Class)).SelectMany(t => Prose.Findings(t.English).Select(f => t.FullName + ": " + f)));
        Assert.All(Excused.Keys, excused => Assert.Contains(TestTraits.All, t => t.Class == excused));
    }
}
