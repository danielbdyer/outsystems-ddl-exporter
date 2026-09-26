using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DbChange.Cli;
using DbChange.Io;
using DbChange.Kernel;
using NetArchTest.Rules;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// Dependencies point one way: the kernel depends on neither io nor the CLI, io does not depend on the CLI, and no
/// project reaches into knowledge/ or ci/ except by reading a file at run time.
/// </summary>
public sealed class DependenciesPointOneWay
{
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "dependencies point one way")]
    [Trait("Value", "L7")]
    public void Io_does_not_depend_on_the_cli_and_the_kernel_on_neither()
    {
        var io = Types.InAssembly(typeof(Write).Assembly).ShouldNot().HaveDependencyOn("DbChange.Cli").GetResult();
        var kernel = Types.InAssembly(typeof(SortedArray).Assembly).ShouldNot().HaveDependencyOnAny("DbChange.Io", "DbChange.Cli").GetResult();

        Assert.True(io.IsSuccessful, "io depends on the CLI: " + string.Join(", ", io.FailingTypeNames ?? []));
        Assert.True(kernel.IsSuccessful, "the kernel depends on io or the CLI: " + string.Join(", ", kernel.FailingTypeNames ?? []));
        Assert.DoesNotContain(typeof(Write).Assembly.GetReferencedAssemblies(), a => a.Name == typeof(Contract).Assembly.GetName().Name);
        Assert.False(Types.InAssembly(typeof(Contract).Assembly).ShouldNot().HaveDependencyOn("DbChange.Io").GetResult().IsSuccessful);   // the rule goes red the other way
    }

    /// <summary>
    /// The cli renders and decides nothing of DacFx's (spec C11): no type it compiles depends on DacFx's namespaces, so a deploy report, a
    /// serialized type name or a DacFx exception is read in io alone.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "dependencies point one way")]
    public void The_cli_references_no_DacFx_type()
    {
        var cli = Types.InAssembly(typeof(Contract).Assembly).ShouldNot().HaveDependencyOnAny("Microsoft.SqlServer.Dac", "Microsoft.SqlServer.Dac.Model").GetResult();

        Assert.True(cli.IsSuccessful, "the cli depends on DacFx: " + string.Join(", ", cli.FailingTypeNames ?? []));
        Assert.False(Types.InAssembly(typeof(Write).Assembly).ShouldNot().HaveDependencyOn("Microsoft.SqlServer.Dac").GetResult().IsSuccessful);   // the rule goes red on io, which does
    }

    /// <summary>
    /// The cli parses a verb's arguments, calls that verb's io use case and renders its answer; it composes no adapter (V3_ARCHITECTURE.md
    /// §6.3, and M1 of the pre-M2 review, which found read, diff and doctor calling SqlServer.Resolve, DacFx.Extract and Ssdt.Build in
    /// cli). Every io type and member cli.dll references, read from its metadata, is a use case's, a type its answer holds, or one of
    /// the services Program and the renderers use; a verb that calls an adapter again fails here, naming the member.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "dependencies point one way")]
    public void The_cli_reaches_io_through_its_use_cases_alone()
    {
        var referenced = IoReferences(typeof(Contract).Assembly.Location);

        Assert.Equal([], referenced.Where(r => !Admitted(r)).Order(StringComparer.Ordinal));
        Assert.Contains("DbChange.Io.ModelRead.Run", referenced);   // the scan reads member references: the use case read calls is among them
    }

    /// <summary>The io types cli may name, each of whose members it may call: the use cases and their answers, and the services Program and the renderers use.</summary>
    private static readonly string[] Whole =
    [
        "Checkout", "Standing", "ModelRead", "ModelRead+Source", "ModelDiff", "ModelDiff+Answer", "DriftCheck", "DriftCheck+Request", "DriftCheck+Answer",
        "Doctor+Readiness", "Doctor+Prerequisite", "Doctor+Item", "Ssdt+ModelElements", "SqlServer+QueryLog", "SqlServer+Reads",
        "Write", "Json", "SchemaText", "PrintedScript", "PasswordForm", "Telemetry", "Interruption", "Runner", "Ran",
    ];

    /// <summary>The members cli may call of the io types it may otherwise only name: the target grammar, the doctor's use case and the program runner it is given.</summary>
    private static readonly string[] Members = ["SqlServer", "SqlServer.Target", "Doctor", "Doctor.Run", "Command", "Command.Run", "Ssdt"];

    private static bool Admitted(string reference)
    {
        var name = reference["DbChange.Io.".Length..];
        var type = name.Split('.')[0];
        return Members.Contains(name) || Whole.Any(whole => type == whole || type.StartsWith(whole + "+", StringComparison.Ordinal));
    }

    /// <summary>
    /// Each io type an assembly's metadata references (a TypeRef whose resolution scope is io, nested types as Outer+Inner) and each io
    /// member (a MemberRef whose parent is such a type), as Type.Member: what the assembly's IL and signatures can reach of io.
    /// </summary>
    private static IReadOnlySet<string> IoReferences(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var io = typeof(Write).Assembly.GetName().Name;
        bool FromIo(EntityHandle scope) => scope.Kind switch
        {
            HandleKind.TypeReference => FromIo(metadata.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope),
            HandleKind.AssemblyReference => metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name) == io,
            _ => false,
        };
        string Named(TypeReferenceHandle handle)
        {
            var type = metadata.GetTypeReference(handle);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? Named((TypeReferenceHandle)type.ResolutionScope) + "+" + metadata.GetString(type.Name)
                : metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        }

        var types = metadata.TypeReferences.Where(t => FromIo(t)).Select(Named);
        var members = metadata.MemberReferences.Select(metadata.GetMemberReference)
            .Where(m => m.Parent.Kind == HandleKind.TypeReference && FromIo(m.Parent))
            .Select(m => Named((TypeReferenceHandle)m.Parent) + "." + metadata.GetString(m.Name));
        return types.Concat(members).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "dependencies point one way")]
    [Trait("Value", "L7")]
    public void No_project_reaches_into_the_knowledge_files_or_ci()
    {
        var reaching = Repository.MsBuildFiles
            .SelectMany(f => Repository.PathsNamedBy(f).Where(p => p.StartsWith("knowledge/", StringComparison.Ordinal) || p.StartsWith("ci/", StringComparison.Ordinal)).Select(p => f + " names " + p));

        Assert.Empty(reaching);
    }
}
