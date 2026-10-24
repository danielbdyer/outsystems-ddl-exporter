using System;
using System.IO;
using System.Linq;
using System.Threading;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class SourceTests : IDisposable
{
    private const string Closed = "DBCHANGE_LITE_TEST_CLOSED";

    private readonly Scratch scratch = new();

    public SourceTests()
    {
        Environment.SetEnvironmentVariable("DBCHANGE_LITE_TOOL", Repo.Tool);
        Environment.SetEnvironmentVariable(Closed, "Server=127.0.0.1,1;Initial Catalog=Estate;User ID=reader;Password=unused;Connect Timeout=2;Encrypt=False");
    }

    [Fact]
    [Trait("Category", "build")]
    public void Predict_of_ref_HEAD_builds_the_commit_and_warns_that_the_working_trees_uncommitted_edit_is_not_in_it()
    {
        var repository = GoldenProject.Copy(scratch, "repository");
        Directory.CreateDirectory(Path.Combine(repository.Folder, "dbchange"));
        File.WriteAllText(Path.Combine(repository.Folder, "dbchange", "environments.json"),
            "{ \"environments\": { \"dev\": { \"host\": \"dev.invalid\", \"connection\": \"env:" + Closed + "\", \"profile\": \"project/profiles/pipeline.publish.xml\", \"deployOrder\": 1 } } }\n");
        Git(repository.Folder, "init", "-q", "-b", "main");
        Git(repository.Folder, "add", ".");
        Git(repository.Folder, "commit", "-q", "-m", "base");
        File.AppendAllText(Path.Combine(repository.Project, "Script.PostDeployment.sql"), "\nPRINT N'not committed';\n");

        var (exit, answer) = Cli.Run(repository.Folder, "predict", "--env", "dev", "--to", "ref:HEAD", "--onto", "none");
        Assert.True(exit == 4, answer.GetProperty("message").GetString());
        var warning = answer.GetProperty("findings").EnumerateArray().Single(f => f.GetProperty("code").GetString() == "source.uncommitted");
        Assert.Equal("The working tree holds 1 uncommitted change, which ref:HEAD does not include.", warning.GetProperty("message").GetString());
        Assert.Equal(Git(repository.Folder, "rev-parse", "HEAD").Trim(), answer.GetProperty("provenance").GetProperty("change").GetString());
    }

    [Fact]
    [Trait("Category", "build")]
    public void A_build_that_fails_on_the_post_deployment_script_names_the_SSDT_error_code_and_its_file()
    {
        var from = GoldenProject.Copy(scratch, "base");
        var to = GoldenProject.Copy(scratch, "broken");
        File.AppendAllText(Path.Combine(to.Project, "Script.PostDeployment.sql"), "\nINSERT INTO WHERE;\n");
        var (exit, answer) = Cli.Run(scratch.Path, "diff", "--from", "project:" + from.Folder, "--to", "project:" + to.Folder);
        Assert.True(exit == 7, answer.GetProperty("message").GetString());
        var finding = answer.GetProperty("findings")[0];
        Assert.Equal("build.failed", finding.GetProperty("code").GetString());
        Assert.Contains("SQL72007", finding.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Script.PostDeployment.sql", finding.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static string Git(string repository, params string[] args)
    {
        var result = Ssdt.Exec("git", ["-C", repository, "-c", "user.name=test", "-c", "user.email=test@localhost", "-c", "commit.gpgsign=false", .. args], repository, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(result.Code == 0, "git " + string.Join(' ', args) + ": " + result.Error);
        return result.Output;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Closed, null);
        scratch.Dispose();
    }
}
