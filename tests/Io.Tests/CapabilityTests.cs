using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// §2.1 rule 3, capabilities are types (M1 exit 5, R15; VALUES.md S1, S7, G6): a named environment and a disposable copy are
/// different types. Publish exists only on a Copy, a Copy has no public constructor and only io/ScratchServer makes one, and the
/// Permissive profile is made only by a Copy, for itself. The compile-fail test plants each forbidden use in a project of its own
/// that references io, and the build refuses each on its line; the same file without them, publishing to a Copy that
/// ScratchServer.Create made, builds.
/// </summary>
public sealed class CapabilityTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Each forbidden use, one per line, with the compiler error that refuses it: no Publish or Permissive on a named environment or on
    /// the type both databases share (CS1061); and, io's internals being invisible to any other assembly, no Permissive.Of (CS0117) and
    /// no constructor of a Copy, called with as many arguments as io's own takes (CS1729).
    /// </summary>
    private static readonly (string Use, string Error)[] Forbidden =
    [
        ("_ = named.Publish(dacpac, strict);", "CS1061"),
        ("_ = ((SqlServer.Database)named).Publish(dacpac, strict);", "CS1061"),
        ("_ = named.Permissive(strict);", "CS1061"),
        ("_ = PublishProfile.Permissive.Of(strict);", "CS0117"),
        ("_ = new SqlServer.Copy(" + string.Join(", ", Enumerable.Repeat("default!", CopyConstructor().GetParameters().Length)) + ");", "CS1729"),
    ];

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    public void No_verb_writes_to_a_named_environment()
    {
        var plant = Path.Combine(Repository.Root, ".estate", "plant", "capabilities-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(plant);
        try
        {
            var (first, lines) = Planted(Forbidden.Select(f => f.Use));
            File.WriteAllText(Path.Combine(plant, "Planted.csproj"), Project());
            File.WriteAllText(Path.Combine(plant, "Planted.cs"), string.Join('\n', lines));
            var (refusedExit, refused) = Build(plant);

            var errors = Regex.Matches(refused, @"Planted\.cs\((\d+),\d+\): error (CS\d+)").Select(m => (Line: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Code: m.Groups[2].Value)).ToHashSet();
            Assert.NotEqual(0, refusedExit);
            Assert.Equal(Forbidden.Select((f, i) => (first + i, f.Error)).ToHashSet(), errors);

            File.WriteAllText(Path.Combine(plant, "Planted.cs"), string.Join('\n', Planted([]).Lines));
            var (builtExit, built) = Build(plant);
            Assert.True(builtExit == 0, "publishing to a Copy that ScratchServer.Create made does not build:\n" + built);
        }
        finally
        {
            Directory.Delete(plant, recursive: true);
        }
    }

    /// <summary>
    /// VALUES.md S1: nothing in io but a Copy calls Permissive's maker, a plan takes the Strict profile alone, and no public member of a
    /// named environment, or of the database type both share, takes or returns a profile or a way to publish.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    public void Permissive_never_reaches_an_environment()
    {
        var of = typeof(PublishProfile.Permissive).GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(["Estate.Io.SqlServer+Copy.Permissive"], Callers(of).Select(Named));
        Assert.Equal(typeof(PublishProfile.Strict), typeof(SqlServer).GetMethod(nameof(SqlServer.Plan))!.GetParameters().Single(p => typeof(PublishProfile).IsAssignableFrom(p.ParameterType)).ParameterType);
        foreach (var type in (Type[])[typeof(SqlServer.Named), typeof(SqlServer.Database)])
        {
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Assert.DoesNotContain(members, m => m.Name is "Publish" or "Permissive");
            Assert.DoesNotContain(members.OfType<MethodInfo>(), m => Touches(m.ReturnType) || m.GetParameters().Any(p => Touches(p.ParameterType)));
            Assert.DoesNotContain(members.OfType<PropertyInfo>(), p => Touches(p.PropertyType));
        }

        Assert.True(typeof(SqlServer.Named).IsSealed && typeof(SqlServer.Copy).IsSealed && !typeof(SqlServer.Copy).IsAssignableFrom(typeof(SqlServer.Named)));
    }

    /// <summary>A Copy is made in io/ScratchServer alone: its constructor is not public, and only ScratchServer's methods, its lambdas included, call it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    public void Nothing_but_ScratchServer_makes_a_Copy()
    {
        var made = CopyConstructor();

        Assert.False(made.IsPublic || made.IsFamily || made.IsFamilyOrAssembly);
        Assert.Empty(typeof(SqlServer.Copy).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.NotEmpty(Callers(made));
        Assert.Empty(Callers(made).Where(caller => !Within(caller.DeclaringType, nameof(ScratchServer))).Select(Named));
    }

    private static ConstructorInfo CopyConstructor() => typeof(SqlServer.Copy).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();

    /// <summary>A planted file: a Copy made by ScratchServer.Create and published to, Strict then Permissive, then the uses given, one per line from the first line it returns.</summary>
    private static (int First, List<string> Lines) Planted(IEnumerable<string> uses)
    {
        var lines = new List<string>
        {
            "using Estate.Io;", "using Estate.Kernel;", "", "namespace Planted;", "", "public static class Uses", "{",
            "    public static Result<SqlServer.Copy> Made(string root, string dacpac, PublishProfile.Strict strict) =>",
            "        ScratchServer.Create(root).Bind(copy => copy.Publish(dacpac, strict)).Bind(copy => copy.Publish(dacpac, copy.Permissive(strict)));",
            "", "    public static void Refused(SqlServer.Named named, string dacpac, PublishProfile.Strict strict)", "    {",
        };
        var first = lines.Count + 1;
        lines.AddRange(uses.Select(use => "        " + use));
        lines.AddRange(["    }", "}", ""]);
        return (first, lines);
    }

    /// <summary>A library referencing the io and kernel assemblies this test runs against, restored without a lock file, so CI's locked restore has nothing to check.</summary>
    private static string Project() => string.Join('\n',
        "<Project Sdk=\"Microsoft.NET.Sdk\">",
        "  <PropertyGroup>",
        "    <ImplicitUsings>disable</ImplicitUsings>",
        "    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>",
        "    <RestoreLockedMode>false</RestoreLockedMode>",
        "  </PropertyGroup>",
        "  <ItemGroup>",
        "    <Reference Include=\"Estate.Io\" HintPath=\"" + Path.Combine(AppContext.BaseDirectory, "Estate.Io.dll") + "\" />",
        "    <Reference Include=\"Estate.Kernel\" HintPath=\"" + Path.Combine(AppContext.BaseDirectory, "Estate.Kernel.dll") + "\" />",
        "  </ItemGroup>",
        "</Project>",
        "");

    private static (int Exit, string Output) Build(string plant)
    {
        var start = new ProcessStartInfo("dotnet", ["build", Path.Combine(plant, "Planted.csproj"), "-nologo", "-v", "q", "-clp:NoSummary", "-nodeReuse:false"])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = plant,
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.Result);
    }

    /// <summary>Whether a type is, or carries, a profile, a copy or a publish result.</summary>
    private static bool Touches(Type type) => typeof(PublishProfile).IsAssignableFrom(type) || type == typeof(SqlServer.Copy)
        || type.GenericTypeArguments.Any(Touches);

    /// <summary>Each method and constructor io compiles, compiler-made ones included, whose IL holds <paramref name="callee"/>'s metadata token.</summary>
    private static IEnumerable<MethodBase> Callers(MethodBase callee) => typeof(SqlServer).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared)))
        .Where(m => m.GetMethodBody()?.GetILAsByteArray() is { } il && il.AsSpan().IndexOf(BitConverter.GetBytes(callee.MetadataToken)) >= 0);

    private static bool Within(Type? type, string name) => type is not null && (type.Name == name || Within(type.DeclaringType, name));

    private static string Named(MethodBase method) => method.DeclaringType!.FullName + "." + method.Name;
}
