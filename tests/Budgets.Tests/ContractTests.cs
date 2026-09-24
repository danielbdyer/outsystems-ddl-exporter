using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Estate.Cli;
using Estate.Io;
using Json.Schema;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>The CLI contract: the verb table, the envelope and the frozen exit table, checked against cli/schemas/.</summary>
public sealed class ContractTests
{
    public static TheoryData<string> Answers => new(Contract.Verbs.Select(v => v.Name).Concat(["no-such-verb", ""]));

    [Fact]
    [Trait("Category", "fast")]
    public void Help_json_validates_against_its_committed_schema()
    {
        var (exit, output) = Estate("--help", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain('\r', output);
        var help = JsonNode.Parse(output)!;
        AssertValid("estate.help.1.schema.json", help);
        Assert.Equal(
            ["doctor", "read", "diff", "classify", "predict", "profile", "twin", "prove", "record", "gate", "check", "knowledge", "--version"],
            help["verbs"]!.AsArray().Select(v => (string)v!["name"]!));
        help["exits"]![0]!["code"] = 8;
        Assert.False(Evaluate("estate.help.1.schema.json", help).IsValid, "the schema admits an exit code the table does not have");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_committed_schemas_are_what_the_contract_generates()
    {
        var directory = Path.Combine(Repository.Root, "cli", "schemas");
        var generated = Render.Schemas().ToDictionary(s => Render.SchemaFile(s.Id), s => Io.Json.Text(s.Schema));
        if (Environment.GetEnvironmentVariable("ESTATE_BLESS") == "1")
        {
            foreach (var (file, text) in generated)
            {
                Write.Text(Path.Combine(directory, file), text);
            }
        }

        Assert.Equal(generated.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var (file, text) in generated)
        {
            Assert.True(text == File.ReadAllText(Path.Combine(directory, file)), $"cli/schemas/{file} is stale: regenerate with ESTATE_BLESS=1 dotnet test --filter Category=fast");
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_exit_table_never_shrinks_or_renumbers()
    {
        var frozen = File.ReadAllLines(Path.Combine(Repository.Root, "cli", "exits.frozen"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(' ', 2))
            .Select(p => (Code: int.Parse(p[0], CultureInfo.InvariantCulture), Name: p[1]))
            .ToList();
        var table = Contract.Exits.Select(e => (e.Code, e.Name)).ToList();

        Assert.Empty(frozen.Except(table));    // a frozen code is never removed, renamed or renumbered
        Assert.Empty(table.Except(frozen));    // a new code is frozen in the change that adds it
    }

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Answers))]
    public void Every_verb_answers_with_an_envelope_that_validates(string verb)
    {
        var (exit, output) = Estate(verb, "--json");

        var envelope = JsonNode.Parse(output)!;
        AssertValid("estate.envelope.1.schema.json", envelope);
        Assert.Equal(exit, (int)envelope["exit"]!);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_verb_not_built_yet_says_the_milestone_it_arrives_in()
    {
        var (exit, output) = Estate("predict");
        Assert.Equal(6, exit);
        Assert.Contains("M2 (Predict)", output, StringComparison.Ordinal);

        var (doctorExit, doctor) = Estate("doctor");
        Assert.Equal(6, doctorExit);
        Assert.StartsWith("estate doctor DEGRADED", doctor, StringComparison.Ordinal);
        Assert.Contains("not built until M1", doctor, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Main_opts_out_of_telemetry_before_anything_else()
    {
        var main = typeof(Cli.Program).GetMethod(nameof(Cli.Program.Main))!;
        var il = main.GetMethodBody()!.GetILAsByteArray()!;
        var first = Array.FindIndex(il, b => b != 0x00);    // past the nops a debug build emits
        Assert.Equal(0x28, il[first]);                      // call
        Assert.Equal(typeof(Telemetry).GetMethod(nameof(Telemetry.OptOut)), main.Module.ResolveMethod(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(first + 1))));
        Assert.Null(typeof(Cli.Program).TypeInitializer);   // no static initializer runs ahead of Main

        Environment.SetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT", null);
        Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", null);
        Telemetry.OptOut();
        Assert.Equal("1", Environment.GetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT"));
    }

    private static void AssertValid(string schemaFile, JsonNode instance)
    {
        var results = Evaluate(schemaFile, instance);
        Assert.True(results.IsValid, JsonSerializer.Serialize(results));
    }

    private static EvaluationResults Evaluate(string schemaFile, JsonNode instance) =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(Repository.Root, "cli", "schemas", schemaFile)))
            .Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });

    /// <summary>Runs the built estate, as a process, and returns its exit code and standard output.</summary>
    private static (int Exit, string Output) Estate(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "estate.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
