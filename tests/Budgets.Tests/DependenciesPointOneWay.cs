using System;
using System.Linq;
using Estate.Cli;
using Estate.Io;
using Estate.Kernel;
using NetArchTest.Rules;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// Dependencies point one way: the kernel depends on neither io nor the CLI, io does not depend on the CLI, and no
/// project reaches into knowledge/ or ci/ except by reading a file at run time.
/// </summary>
public sealed class DependenciesPointOneWay
{
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "dependencies point one way")]
    public void Io_does_not_depend_on_the_cli_and_the_kernel_on_neither()
    {
        var io = Types.InAssembly(typeof(Write).Assembly).ShouldNot().HaveDependencyOn("Estate.Cli").GetResult();
        var kernel = Types.InAssembly(typeof(Seq).Assembly).ShouldNot().HaveDependencyOnAny("Estate.Io", "Estate.Cli").GetResult();

        Assert.True(io.IsSuccessful, "io depends on the CLI: " + string.Join(", ", io.FailingTypeNames ?? []));
        Assert.True(kernel.IsSuccessful, "the kernel depends on io or the CLI: " + string.Join(", ", kernel.FailingTypeNames ?? []));
        Assert.DoesNotContain(typeof(Write).Assembly.GetReferencedAssemblies(), a => a.Name == typeof(Contract).Assembly.GetName().Name);
        Assert.False(Types.InAssembly(typeof(Contract).Assembly).ShouldNot().HaveDependencyOn("Estate.Io").GetResult().IsSuccessful);   // the rule goes red the other way
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "dependencies point one way")]
    public void No_project_reaches_into_the_knowledge_files_or_ci()
    {
        var reaching = Repository.MsBuildFiles
            .SelectMany(f => Repository.PathsNamedBy(f).Where(p => p.StartsWith("knowledge/", StringComparison.Ordinal) || p.StartsWith("ci/", StringComparison.Ordinal)).Select(p => f + " names " + p));

        Assert.Empty(reaching);
    }
}
