using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DbChange.Cli;
using DbChange.Kernel;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// The kernel cannot do I/O: the DbChange.Kernel assembly references the BCL (the shared framework, which carries
/// System.Collections.Immutable) and nothing else, and no public kernel member returns a Task, a ValueTask or an
/// IAsyncEnumerable. kernel/BannedSymbols.txt holds the rest (BannedSymbolsTests).
/// </summary>
public sealed class KernelCannotDoIo
{
    private static readonly Assembly Kernel = typeof(SortedArray).Assembly;

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "the kernel cannot do I/O")]
    [Trait("Value", "L7")]
    public void The_kernel_references_the_BCL_and_nothing_else()
    {
        Assert.Empty(OutsideTheBcl(Kernel));
        Assert.Contains(Kernel.GetReferencedAssemblies(), a => a.Name == "System.Collections.Immutable");
        Assert.Contains("DbChange.Io", OutsideTheBcl(typeof(Contract).Assembly));   // the rule goes red on an assembly that does I/O
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "the kernel cannot do I/O")]
    [Trait("Value", "L7")]
    public void No_public_kernel_member_is_asynchronous()
    {
        Type[] asynchronous = [typeof(Task), typeof(Task<>), typeof(ValueTask), typeof(ValueTask<>), typeof(IAsyncEnumerable<>)];
        var found = Kernel.GetExportedTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(m => (Member: m, Type: m switch { MethodInfo x => x.ReturnType, PropertyInfo x => x.PropertyType, FieldInfo x => x.FieldType, _ => typeof(void) }))
            .Where(m => asynchronous.Contains(m.Type.IsGenericType ? m.Type.GetGenericTypeDefinition() : m.Type))
            .Select(m => m.Member.DeclaringType + "." + m.Member.Name);

        Assert.Empty(found);
    }

    /// <summary>The assemblies a given one references that are not files of the shared framework it runs on.</summary>
    private static IEnumerable<string> OutsideTheBcl(Assembly assembly)
    {
        var framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => !File.Exists(Path.Combine(framework, n + ".dll")));
    }
}
