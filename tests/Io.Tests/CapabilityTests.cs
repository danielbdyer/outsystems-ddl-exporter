using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// §2.1 rule 3, capabilities are types (M1 exit 5, R15; VALUES.md S1, S7, G6): a named environment and a disposable copy are
/// different types. Publish exists only on a Copy, a Copy has no public constructor and only io/LocalServer makes one, and the
/// Permissive profile is made only by a Copy, for itself. The compile-fail test plants each forbidden use in a project of its own
/// that references io, and the build refuses each on its line; the same file without them, publishing to a Copy that
/// LocalServer.Create made, builds.
/// </summary>
public sealed class CapabilityTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Each forbidden use, one per line, with the compiler error that refuses it: no Publish or Permissive on a named environment or on
    /// the type both databases share (CS1061); and, io's internals being invisible to any assembly but its tests, no Permissive.Of
    /// (CS0117), no constructor of a Copy, called with as many arguments as io's own takes (CS1729), no aggregate query made from
    /// text, which only io's builders make for a named environment (CS0117; DECISIONS.md, 2026-09-25), and no DacFx.Publish to a named
    /// environment, since it takes a Copy (CS1503).
    /// </summary>
    private static readonly (string Use, string Error)[] Forbidden =
    [
        ("_ = named.Publish(dacpac, strict);", "CS1061"),
        ("_ = ((SqlServer.Database)named).Publish(dacpac, strict);", "CS1061"),
        ("_ = named.Permissive(strict);", "CS1061"),
        ("_ = PublishProfile.Permissive.Of(strict);", "CS0117"),
        ("_ = new SqlServer.Copy(" + string.Join(", ", Enumerable.Repeat("default!", CopyConstructor().GetParameters().Length)) + ");", "CS1729"),
        ("_ = SqlServer.AggregateQuery.Of(\"SELECT COUNT(*) FROM dbo.Customer;\", \"planted\");", "CS0117"),
        ("_ = DacFx.Publish(named, null!, strict);", "CS1503"),
    ];

    [Fact]
    [Trait("Category", "build")]
    [Trait("Law", "a named environment cannot be written")]
    [Trait("Value", "S7")]
    [Trait("Value", "G6")]
    [Trait("Exit", "M1.5")]
    public void No_verb_writes_to_a_named_environment()
    {
        using var plant = ScratchFolder.UnderRepository("plant");
        var project = plant.File("Planted.csproj", Project());

        var (refusedExit, refused, errors) = PlantedProject.Build(project, Before, [.. Forbidden.Select(f => "        " + f.Use)], After);
        var (builtExit, built, _) = PlantedProject.Build(project, Before, [], After);

        Assert.True(refusedExit != 0, refused);
        Assert.Equal(Forbidden.Select(f => (string?)f.Error), errors);
        Assert.True(builtExit == 0, "publishing to a Copy that LocalServer.Create made does not build:\n" + built);
    }

    /// <summary>
    /// VALUES.md S1: nothing in io but a Copy calls Permissive's maker, a plan takes the Strict profile alone, and no public member of a
    /// named environment, or of the database type both share, takes or returns a profile or a way to publish.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    [Trait("Value", "S1")]
    [Trait("Exit", "M1.5")]
    public void Permissive_never_reaches_an_environment()
    {
        var of = typeof(PublishProfile.Permissive).GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(["DbChange.Io.SqlServer+Copy.Permissive"], Callers(of).Select(Named));
        Assert.Equal(typeof(PublishProfile.Strict), typeof(DacFx).GetMethod(nameof(DacFx.Plan), BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters().Single(p => typeof(PublishProfile).IsAssignableFrom(p.ParameterType)).ParameterType);
        foreach (var type in (Type[])[typeof(SqlServer.EnvironmentDatabase), typeof(SqlServer.Database)])
        {
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Assert.DoesNotContain(members, m => m.Name is "Publish" or "Permissive");
            Assert.DoesNotContain(members.OfType<MethodInfo>(), m => Touches(m.ReturnType) || m.GetParameters().Any(p => Touches(p.ParameterType)));
            Assert.DoesNotContain(members.OfType<PropertyInfo>(), p => Touches(p.PropertyType));
        }

        Assert.True(typeof(SqlServer.EnvironmentDatabase).IsSealed && typeof(SqlServer.Copy).IsSealed && !typeof(SqlServer.Copy).IsAssignableFrom(typeof(SqlServer.EnvironmentDatabase)));
    }

    /// <summary>A Copy is made in io/LocalServer alone: its constructor is not public, and only LocalServer's methods, its lambdas included, call it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    [Trait("Value", "S7")]
    [Trait("Exit", "M1.5")]
    public void Nothing_but_LocalServer_makes_a_Copy()
    {
        var made = CopyConstructor();

        Assert.False(made.IsPublic || made.IsFamily || made.IsFamilyOrAssembly);
        Assert.Empty(typeof(SqlServer.Copy).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.NotEmpty(Callers(made));
        Assert.Empty(Callers(made).Where(caller => !Within(caller.DeclaringType, nameof(LocalServer))).Select(Named));
    }

    private static ConstructorInfo CopyConstructor() => typeof(SqlServer.Copy).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();

    /// <summary>The planted file around the uses: a Copy made by LocalServer.Create and published to, Strict then Permissive, then a method the uses stand in.</summary>
    private static readonly string[] Before =
    [
        "using DbChange.Io;", "using DbChange.Kernel;", "", "namespace Planted;", "", "public static class Uses", "{",
        "    public static Result<SqlServer.Copy> Made(string root, string dacpac, PublishProfile.Strict strict) =>",
        "        LocalServer.Create(root).Bind(copy => copy.Publish(dacpac, strict)).Bind(copy => copy.Publish(dacpac, copy.Permissive(strict)));",
        "", "    public static void Refused(SqlServer.EnvironmentDatabase named, string dacpac, PublishProfile.Strict strict)", "    {",
    ];

    private static readonly string[] After = ["    }", "}", ""];

    /// <summary>A library referencing the io and kernel assemblies this test runs against, restored without a lock file, so CI's locked restore has nothing to check.</summary>
    private static string Project() => string.Join('\n',
        "<Project Sdk=\"Microsoft.NET.Sdk\">",
        "  <PropertyGroup>",
        "    <ImplicitUsings>disable</ImplicitUsings>",
        "    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>",
        "    <RestoreLockedMode>false</RestoreLockedMode>",
        "  </PropertyGroup>",
        "  <ItemGroup>",
        "    <Reference Include=\"DbChange.Io\" HintPath=\"" + Path.Combine(AppContext.BaseDirectory, "DbChange.Io.dll") + "\" />",
        "    <Reference Include=\"DbChange.Kernel\" HintPath=\"" + Path.Combine(AppContext.BaseDirectory, "DbChange.Kernel.dll") + "\" />",
        "  </ItemGroup>",
        "</Project>",
        "");

    /// <summary>Whether a type is, or carries, a profile, a copy or a publish result.</summary>
    private static bool Touches(Type type) => typeof(PublishProfile).IsAssignableFrom(type) || type == typeof(SqlServer.Copy)
        || type.GenericTypeArguments.Any(Touches);

    /// <summary>Each method and constructor io compiles, compiler-made ones included, whose IL holds <paramref name="callee"/>'s metadata token.</summary>
    private static IEnumerable<MethodBase> Callers(MethodBase callee) => typeof(SqlServer).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared)))
        .Where(m => m.GetMethodBody()?.GetILAsByteArray() is { } il && Calls(il, callee.MetadataToken));

    /// <summary>Whether IL holds a call, callvirt, newobj, ldftn or ldvirtftn of the member whose metadata token is <paramref name="token"/>: the token after its opcode, never the same four bytes elsewhere.</summary>
    private static bool Calls(byte[] il, int token) => Enumerable.Range(1, Math.Max(0, il.Length - 4)).Any(i =>
        il.AsSpan(i, 4).SequenceEqual(BitConverter.GetBytes(token)) && (il[i - 1] is 0x28 or 0x6F or 0x73 || (i >= 2 && il[i - 2] == 0xFE && il[i - 1] is 0x06 or 0x07)));

    private static bool Within(Type? type, string name) => type is not null && (type.Name == name || Within(type.DeclaringType, name));

    private static string Named(MethodBase method) => method.DeclaringType!.FullName + "." + method.Name;
}
