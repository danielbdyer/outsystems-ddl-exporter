using System;
using System.Linq;
using System.Reflection;
using DbChange.Cli;
using DbChange.Io;
using DbChange.Kernel;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// A kernel closed hierarchy stays closed: every abstract kernel class (Result&lt;T&gt; today, Statement later) has
/// only private constructors beside a record's copy constructor, and every type deriving from it, in any v3 assembly,
/// is sealed and nested inside it, so its Match covers every case (V3_ARCHITECTURE.md §6.6).
/// </summary>
public sealed class ClosedHierarchies
{
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S2")]
    public void Every_case_of_a_kernel_closed_hierarchy_is_sealed_and_nested_inside_it()
    {
        var hierarchies = typeof(SortedArray).Assembly.GetTypes().Where(t => t.IsClass && t.IsAbstract && !t.IsSealed).ToList();
        var open = hierarchies
            .SelectMany(h => h.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(c => !c.IsPrivate && !(c.GetParameters() is [var only] && Definition(only.ParameterType) == h))
                .Select(c => h + " can be derived from outside: " + c));
        var strays = new[] { typeof(SortedArray).Assembly, typeof(Write).Assembly, typeof(Contract).Assembly }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => hierarchies.Where(h => Derives(t, h) && (Definition(t.DeclaringType) != h || !t.IsSealed)).Select(h => t + " derives from " + h + " but is not a sealed type nested in it"));

        Assert.Contains(typeof(Result<>), hierarchies);
        Assert.Empty(open);
        Assert.Empty(strays);
    }

    private static bool Derives(Type type, Type hierarchy)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (Definition(b) == hierarchy)
            {
                return true;
            }
        }

        return false;
    }

    private static Type? Definition(Type? type) => type is { IsGenericType: true } ? type.GetGenericTypeDefinition() : type;
}
