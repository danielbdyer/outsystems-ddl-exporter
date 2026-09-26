using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DbChange.Kernel;
using DbChange.Kernel.Tests;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// The kernel's hand-built sample changes (Kernel.Tests' SampleChanges) against the golden project's, built and read by DacFx: over the
/// keys both models hold, each gives the same change (the same elements created and dropped, the same renames, and the same properties
/// and relationships altered on the same elements), so the kernel's properties stand on models shaped as DacFx reads them.
/// </summary>
public sealed class SampleChangeTests
{
    public static TheoryData<string> Registered => new(SampleChanges.Registered);

    [Theory]
    [Trait("Category", "build")]
    [MemberData(nameof(Registered))]
    public async Task Each_sample_change_gives_the_same_change_in_memory_and_built(string name)
    {
        var (before, after, renames) = SampleChanges.Pair(name);
        var (built, head) = (await GoldenProject.Model(), await GoldenProject.Model(GoldenProject.Change(name)));
        var keys = before.Concat(after).Select(e => e.Key).ToHashSet();
        keys.IntersectWith(built.Elements.Concat(head.Elements).Select(e => e.Key));

        var inMemory = Shape(Expect.Value(Change.Between(before, after, renames)), keys);

        Assert.NotEmpty(inMemory);
        Assert.Equal(inMemory, Shape(Expect.Value(Change.Between(built.Elements, head.Elements, head.Renames)), keys));
    }

    /// <summary>A change as the keys it touches among <paramref name="keys"/>: created, dropped, renamed, and altered with the names of what altered.</summary>
    private static List<string> Shape(Change change, IReadOnlySet<ElementKey> keys) =>
    [
        .. change.Created.Where(e => keys.Contains(e.Key)).Select(e => "created " + e.Key)
            .Concat(change.Dropped.Where(e => keys.Contains(e.Key)).Select(e => "dropped " + e.Key))
            .Concat(change.Renamed.Where(r => keys.Contains(r.Before) || keys.Contains(r.After)).Select(r => "renamed " + r.Before + " to " + r.After))
            .Concat(change.Altered.Where(a => keys.Contains(a.Key))
                .Select(a => "altered " + a.Key + ": " + string.Join(", ", a.Properties.Select(p => p.Name).Concat(a.Relationships.Select(r => r.Name)))))
            .Order(StringComparer.Ordinal),
    ];
}
