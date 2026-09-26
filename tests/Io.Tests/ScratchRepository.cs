using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DbChange.Budgets.Tests;
using DbChange.Cli;
using Json.Schema;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// An SSDT repository for the verbs (WP 1.7), in a git repository of its own: the golden project with its stop files, a dbchange/environments.json
/// naming no environment and the sample toolchain ledger, committed as Base; then Customer.Email made mandatory, committed as Head, the
/// make-mandatory sample change. dbchange runs in this process against it, its tool folder dist/dbchange/ (PublishedTool).
/// </summary>
public sealed class ScratchRepository : IDisposable
{
    public const string Profile = "project/profiles/pipeline.publish.xml";

    private readonly Scratch scratch = new();

    public ScratchRepository()
    {
        Tool = new PublishedTool();
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var stop in (string[])["Directory.Build.props", "Directory.Packages.props"])
        {
            File.Copy(Path.Combine(golden, stop), Path.Combine(Root, stop));
        }

        ToolFolderTests.Copy(Path.Combine(golden, "project"), Path.Combine(Root, "project"));
        Ledger(Root);
        Base = scratch.Commit("the golden project", (".gitignore", ".dbchange/\n"), ("dbchange/environments.json", "{ \"environments\": {} }\n"));
        var customer = Path.Combine(Root, "project", "Modules", "Customer.sql");
        var text = File.ReadAllText(customer);
        Assert.True(text.Split("Email           NVARCHAR(256)   NULL,").Length == 2, customer + " does not hold Email's declaration once");
        Head = scratch.Commit("Customer.Email made mandatory", ("project/Modules/Customer.sql", text.Replace("Email           NVARCHAR(256)   NULL,", "Email           NVARCHAR(256)   NOT NULL,", StringComparison.Ordinal)));
    }

    public PublishedTool Tool { get; }

    public string Root => scratch.Root;

    /// <summary>The golden project's commit.</summary>
    public string Base { get; }

    /// <summary>The make-mandatory sample change's commit.</summary>
    public string Head { get; }

    /// <summary>dbchange run at the repository's root.</summary>
    public (int Exit, string Output) Run(params string[] arguments) => RunAt(Root, arguments);

    /// <summary>dbchange run in this process with <paramref name="root"/> as the repository root and dist/dbchange/ as its tool folder: its exit and its output.</summary>
    public (int Exit, string Output) RunAt(string root, params string[] arguments)
    {
        using var output = new MemoryStream();
        var exit = Cli.Program.Run(arguments, output, new Checkout(root, root, Tool.Folder));
        return (exit, Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>
    /// A repository root beside this repository's own, under it, whose environments file names environments: each a reference to a connection file,
    /// under the golden profile; its registry, its runs and its builds are its own.
    /// </summary>
    public string Named(params (string Name, string Connection)[] environments)
    {
        var root = Path.Combine(Root, "named-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "profiles"));
        File.Copy(Path.Combine(Root, Profile), Path.Combine(root, "profiles", "pipeline.publish.xml"));
        Ledger(root);
        var environmentsFile = new JsonObject();
        foreach (var (name, connection) in environments)
        {
            environmentsFile[name] = new JsonObject { ["host"] = "localhost", ["connection"] = "file:" + connection.Replace('\\', '/'), ["profile"] = "profiles/pipeline.publish.xml" };
        }

        File.WriteAllText(Path.Combine(root, "dbchange", "environments.json"), new JsonObject { ["environments"] = environmentsFile }.ToJsonString());
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

    /// <summary>The sample toolchain ledger, UNPINNED for this dbchange version, under a repository root.</summary>
    private static void Ledger(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "dbchange", "ledgers"));
        File.Copy(Path.Combine(Repository.Root, "tests", "Golden", "dbchange", "ledgers", "toolchain.md"), Path.Combine(root, "dbchange", "ledgers", "toolchain.md"));
    }
}
