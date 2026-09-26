using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DbChange.Budgets.Tests;
using DbChange.Cli;
using DbChange.Io;
using Json.Schema;
using Xunit;

namespace DbChange.Tests;

/// <summary>
/// A verb's JSON answer: dbchange run in this process with --json, its exit and its answer; and an answer held to the schema under
/// cli/schemas/ that its schema field names, formats asserted as well as annotated.
/// </summary>
internal static class VerbAnswer
{
    /// <summary>dbchange run in this process at <paramref name="here"/> with <paramref name="arguments"/> and --json: its exit and its answer, valid against its schema.</summary>
    public static (int Exit, JsonNode Answer) Of(Checkout here, params string[] arguments)
    {
        var (exit, answer, _) = Printing(here, arguments);
        return (exit, answer);
    }

    /// <summary>dbchange run as <see cref="Of"/> runs it, with the text it printed, for a search of what it wrote byte for byte.</summary>
    public static (int Exit, JsonNode Answer, string Printed) Printing(Checkout here, params string[] arguments)
    {
        using var output = new MemoryStream();
        var exit = Cli.Program.Run([.. arguments, "--json"], output, here);
        var printed = Encoding.UTF8.GetString(output.ToArray());
        var answer = JsonNode.Parse(printed)!;
        Valid(answer);
        return (exit, answer, printed);
    }

    /// <summary>An answer valid against the schema its schema field names: dbchange.check/1 is cli/schemas/dbchange.check.1.schema.json.</summary>
    public static void Valid(JsonNode answer) => Valid(Render.SchemaFile((string)answer["schema"]!), answer);

    /// <summary>An instance valid against a schema file under cli/schemas/.</summary>
    public static void Valid(string schemaFile, JsonNode instance)
    {
        var results = Evaluate(schemaFile, instance);
        Assert.True(results.IsValid, schemaFile + ": " + JsonSerializer.Serialize(results));
    }

    /// <summary>An instance evaluated against a schema file under cli/schemas/, formats asserted, so a date-time is a rule and not a comment.</summary>
    public static EvaluationResults Evaluate(string schemaFile, JsonNode instance) =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(Repository.Root, "cli", "schemas", schemaFile)))
            .Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
}
