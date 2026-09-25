using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Estate.Budgets.Tests;
using Estate.Cli;
using Json.Schema;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// An estate repository for the verbs (WP 1.7), in a git repository of its own: the golden project with its stop files, an estate/posture
/// naming no environment and the sample toolchain ledger, committed as Base; then Customer.Email made mandatory, committed as Head, the
/// make-mandatory archetype. estate runs in this process against it, its tool folder dist/estate/ (PublishedTool).
/// </summary>
public sealed class ScratchEstate : IDisposable
{
    public const string Profile = "project/profiles/pipeline.publish.xml";

    private readonly Scratch scratch = new();

    public ScratchEstate()
    {
        Tool = new PublishedTool();
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var stop in (string[])["Directory.Build.props", "Directory.Packages.props"])
        {
            File.Copy(Path.Combine(golden, stop), Path.Combine(Root, stop));
        }

        ToolFolderTests.Copy(Path.Combine(golden, "project"), Path.Combine(Root, "project"));
        Ledger(Root);
        Base = scratch.Commit("the golden project", (".gitignore", ".estate/\n"), ("estate/posture.json", "{ \"environments\": {} }\n"));
        var customer = Path.Combine(Root, "project", "Modules", "Customer.sql");
        var text = File.ReadAllText(customer);
        Assert.True(text.Split("Email           NVARCHAR(256)   NULL,").Length == 2, customer + " does not hold Email's declaration once");
        Head = scratch.Commit("Customer.Email made mandatory", ("project/Modules/Customer.sql", text.Replace("Email           NVARCHAR(256)   NULL,", "Email           NVARCHAR(256)   NOT NULL,", StringComparison.Ordinal)));
    }

    public PublishedTool Tool { get; }

    public string Root => scratch.Root;

    /// <summary>The golden project's commit.</summary>
    public string Base { get; }

    /// <summary>The make-mandatory archetype's commit.</summary>
    public string Head { get; }

    /// <summary>estate run at the repository's root.</summary>
    public (int Exit, string Output) Estate(params string[] arguments) => EstateAt(Root, arguments);

    /// <summary>estate run in this process with <paramref name="root"/> as the estate's root and dist/estate/ as its tool folder: its exit and its output.</summary>
    public (int Exit, string Output) EstateAt(string root, params string[] arguments)
    {
        using var output = new MemoryStream();
        var exit = Cli.Program.Run(arguments, output, new Checkout(root, root, Tool.Folder));
        return (exit, Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// An estate root beside the repository's own, under it, whose posture names environments: each a reference to a connection file,
    /// under the golden profile; its registry, its runs and its builds are its own.
    /// </summary>
    public string Named(params (string Name, string Connection)[] environments)
    {
        var root = Path.Combine(Root, "named-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "profiles"));
        File.Copy(Path.Combine(Root, Profile), Path.Combine(root, "profiles", "pipeline.publish.xml"));
        Ledger(root);
        var posture = new JsonObject();
        foreach (var (name, connection) in environments)
        {
            posture[name] = new JsonObject { ["connection"] = "file:" + connection.Replace('\\', '/'), ["profile"] = "profiles/pipeline.publish.xml" };
        }

        File.WriteAllText(Path.Combine(root, "estate", "posture.json"), new JsonObject { ["environments"] = posture }.ToJsonString());
        return root;
    }

    /// <summary>Validates an answer against its verb's schema under cli/schemas/.</summary>
    public static void Valid(string schema, JsonNode answer)
    {
        var results = JsonSchema.FromText(File.ReadAllText(Path.Combine(Repository.Root, "cli", "schemas", schema)))
            .Evaluate(answer, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        Assert.True(results.IsValid, schema + ": " + JsonSerializer.Serialize(results));
    }

    public void Dispose() => scratch.Dispose();

    /// <summary>The sample toolchain ledger, UNPINNED for this estate version, under an estate's root.</summary>
    private static void Ledger(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "estate", "ledgers"));
        File.Copy(Path.Combine(Repository.Root, "tests", "Golden", "estate", "ledgers", "toolchain.md"), Path.Combine(root, "estate", "ledgers", "toolchain.md"));
    }
}
