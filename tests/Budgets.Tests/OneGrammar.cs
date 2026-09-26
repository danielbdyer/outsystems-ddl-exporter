using System;
using System.Linq;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// Each grammar estate reads a name by is written in one file of the kernel, io and the cli (DECISIONS.md, 2026-09-25; findings R-7 and
/// ARCH-11): an environment's name and a copy's name in kernel/Target.cs, a server's protocol prefix in kernel/ServerName.cs, a SQLCMD
/// variable's name in kernel/SqlCmd.cs. A second copy of one lets the target grammar and estate/environments.json, the registry and R15, or
/// a plan and a parse of its script read one name two ways.
/// </summary>
public sealed class OneGrammar
{
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(@"[a-z][a-z0-9-]{0,31}", "kernel/Target.cs")]
    [InlineData(@"\Aestate_(?<machine>", "kernel/Target.cs")]
    [InlineData(@"\A(?:tcp|np|lpc|admin):", "kernel/ServerName.cs")]
    [InlineData(@"[A-Za-z_][A-Za-z0-9_-]{0,127}", "kernel/SqlCmd.cs")]
    public void Each_name_grammar_is_written_in_one_file(string grammar, string file) =>
        Assert.Equal([file], Repository.Files.Where(f => f.EndsWith(".cs", StringComparison.Ordinal)
            && (f.StartsWith("kernel/", StringComparison.Ordinal) || f.StartsWith("io/", StringComparison.Ordinal) || f.StartsWith("cli/", StringComparison.Ordinal))
            && Repository.Read(f).Contains(grammar, StringComparison.Ordinal)));
}
