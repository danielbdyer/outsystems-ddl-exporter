using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DbChangeLite;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class CorpusCopy : IDisposable
{
    public CorpusCopy()
    {
        Environment.SetEnvironmentVariable("DBCHANGE_LITE_TOOL", Repo.Tool);
        Scratch = new Scratch();
        Database = "dbchange_lite_corpus_" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..6];
        Connection = new SqlConnectionStringBuilder(Repo.FixtureServer) { InitialCatalog = Database }.ConnectionString;
        Environment.SetEnvironmentVariable("DBCHANGE_LITE_CORPUS", Connection);
        Root = Scratch.Under("root");
        Directory.CreateDirectory(Path.Combine(Root, "dbchange"));
        Directory.CreateDirectory(Path.Combine(Root, "profiles"));
        File.Copy(Path.Combine(Repo.Golden, "project", "profiles", "pipeline.publish.xml"), Path.Combine(Root, "profiles", "pipeline.publish.xml"));
        Environments("profiles/pipeline.publish.xml");
        Base = GoldenProject.Copy(Scratch, "base");
        var run = Runs.At(Root, "fixture");
        BasePackage = Ssdt.Build(Base.Project, run);
        using var master = Session.Open(Repo.FixtureServer, run.Log, CancellationToken.None, "fixture server");
        LocalServer.Drop(master, Database);
        master.Execute("CREATE DATABASE " + Names.Quote(Database));
        var options = new DacDeployOptions { BlockOnPossibleDataLoss = false, IgnorePermissions = true, IgnoreRoleMembership = true, IgnoreUserSettingsObjects = true, ExcludeObjectTypes = [ObjectType.Users, ObjectType.Logins, ObjectType.RoleMembership, ObjectType.Permissions] };
        var deployed = DacFx.Deploy(Repo.FixtureServer, DacFx.Load(BasePackage), Database, options, CancellationToken.None);
        if (deployed.Outcome != "done")
        {
            throw new InvalidOperationException("The golden base did not deploy: " + string.Join(" | ", deployed.Messages));
        }
    }

    public Scratch Scratch { get; }

    public string Root { get; }

    public string Database { get; }

    public string Connection { get; }

    public GoldenProject Base { get; }

    public string BasePackage { get; }

    public void Environments(string profile) => File.WriteAllText(Path.Combine(Root, "dbchange", "environments.json"),
        "{ \"environments\": { \"corpus\": { \"host\": \"corpus.invalid\", \"connection\": \"env:DBCHANGE_LITE_CORPUS\", \"profile\": \"" + profile + "\", \"readerGroups\": [\"developers\"], \"deployOrder\": 1 } } }\n");

    public void CdcLedger(string? text)
    {
        var path = Path.Combine(Root, "dbchange", "ledgers", "cdc-tracked.md");
        if (text is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Deploy(string package)
    {
        var options = new DacDeployOptions { BlockOnPossibleDataLoss = false, IgnorePermissions = true, IgnoreRoleMembership = true, IgnoreUserSettingsObjects = true, ExcludeObjectTypes = [ObjectType.Users, ObjectType.Logins, ObjectType.RoleMembership, ObjectType.Permissions] };
        var deployed = DacFx.Deploy(Repo.FixtureServer, DacFx.Load(package), Database, options, CancellationToken.None);
        if (deployed.Outcome != "done")
        {
            throw new InvalidOperationException("The package did not deploy to the corpus copy: " + string.Join(" | ", deployed.Messages));
        }
    }

    public string Build(string projectFolder) => Ssdt.Package(new Source.Project(projectFolder), null, Runs.At(Root, "fixture")).PackagePath;

    public static int Operations(JsonElement answer) => answer.GetProperty("environments")[0].TryGetProperty("plan", out var plan) && plan.TryGetProperty("operations", out var operations) ? operations.GetArrayLength() : 0;

    public void Emptied()
    {
        Run("EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'");
        Run("EXEC sp_MSforeachtable 'DELETE FROM ?'");
        Run("EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'");
    }

    public void Run(string script)
    {
        using var session = Session.Open(Connection, Runs.At(Root, "fixture").Log, CancellationToken.None, Database);
        foreach (var batch in LocalServer.Batches(script))
        {
            session.Execute(batch);
        }
    }

    public (int Exit, JsonElement Answer) Predict(string projectFolder) => Cli.Run(Root, "predict", "--env", "corpus", "--to", "project:" + projectFolder, "--onto", "none");

    public static string Outcome(JsonElement answer) => answer.GetProperty("environments")[0].GetProperty("outcome").GetString()!;

    public static IReadOnlyList<string> Blockers(JsonElement answer)
    {
        var environment = answer.GetProperty("environments")[0];
        var blockers = new List<string>();
        if (environment.TryGetProperty("checks", out var checks))
        {
            blockers.AddRange(checks.EnumerateArray().Where(c => c.TryGetProperty("fires", out var f) && f.ValueKind == JsonValueKind.True).Select(c => "TableEmpty on " + c.GetProperty("table").GetString()));
        }

        if (environment.TryGetProperty("preconditions", out var preconditions))
        {
            blockers.AddRange(preconditions.EnumerateArray().Where(p => p.TryGetProperty("violated", out var v) && v.ValueKind == JsonValueKind.True)
                .Select(p => p.GetProperty("kind").GetString() + " on " + p.GetProperty("table").GetString() + Columns(p)));
        }

        return blockers.Order(StringComparer.Ordinal).ToList();
    }

    public static int NotPredictable(JsonElement answer) => answer.GetProperty("environments")[0].TryGetProperty("notPredictable", out var items) ? items.GetArrayLength() : 0;

    private static string Columns(JsonElement precondition)
    {
        var columns = precondition.GetProperty("columns").EnumerateArray().Select(c => "[" + c.GetString() + "]").ToList();
        return columns.Count == 0 ? "" : " (" + string.Join(", ", columns) + ")";
    }

    public void Dispose()
    {
        try
        {
            using var master = Session.Open(Repo.FixtureServer, Runs.At(Root, "fixture").Log, CancellationToken.None, "fixture server");
            LocalServer.Drop(master, Database);
        }
        finally
        {
            Scratch.Dispose();
        }
    }
}

public sealed class CorpusTests(CorpusCopy copy) : IClassFixture<CorpusCopy>
{
    public static TheoryData<string> Samples => new(GoldenProject.SampleChanges.Concat(GoldenProject.LiteChanges));

    [Theory]
    [Trait("Category", "fixture")]
    [MemberData(nameof(Samples))]
    public void Each_sample_change_predicts_on_the_empty_the_populated_and_the_violating_copy_what_its_predict_txt_states(string name)
    {
        var expected = Expected.Of(name);
        var project = GoldenProject.Copy(copy.Scratch, name).Edit(name);
        var violating = GoldenProject.Case(name, "violating.sql");
        var ledger = GoldenProject.Case(name, "cdc-tracked.md");
        var profile = GoldenProject.Case(name, "profile.publish.xml");
        var afterDeploy = GoldenProject.Case(name, "after-deploy.txt");
        var findings = GoldenProject.Case(name, "findings.txt");
        copy.CdcLedger(File.Exists(ledger) ? File.ReadAllText(ledger) : null);
        if (File.Exists(profile))
        {
            File.Copy(profile, Path.Combine(copy.Root, "profiles", name + ".publish.xml"), true);
            copy.Environments("profiles/" + name + ".publish.xml");
        }

        copy.Emptied();
        var answers = new Dictionary<string, (int Exit, JsonElement Answer)> { ["empty"] = copy.Predict(project.Folder) };
        copy.Run(File.ReadAllText(Path.Combine(Repo.Golden, "copies", "populated.sql")));
        answers["populated"] = copy.Predict(project.Folder);
        if (File.Exists(violating))
        {
            copy.Run(File.ReadAllText(violating));
        }

        answers["violating"] = copy.Predict(project.Folder);
        if (File.Exists(afterDeploy))
        {
            copy.Emptied();
            copy.Deploy(copy.Build(project.Folder));
            try
            {
                var (exit, answer) = copy.Predict(project.Folder);
                var want = File.ReadAllText(afterDeploy).Trim();
                Assert.True(exit is 0 or 3 or 8, name + " after deploy exited " + exit + ": " + answer.GetProperty("message").GetString());
                Assert.True(want == "after-deploy: " + CorpusCopy.Outcome(answer) + "; operations " + CorpusCopy.Operations(answer),
                    name + ": after-deploy.txt says " + want + "; dbchange-lite says after-deploy: " + CorpusCopy.Outcome(answer) + "; operations " + CorpusCopy.Operations(answer));
            }
            finally
            {
                copy.Deploy(copy.BasePackage);
            }
        }

        copy.CdcLedger(null);
        copy.Environments("profiles/pipeline.publish.xml");
        foreach (var want in expected)
        {
            var (exit, answer) = answers[want.Copy];
            Assert.True(exit is 0 or 3 or 8, name + " on " + want.Copy + " exited " + exit + ": " + answer.GetProperty("message").GetString());
            Assert.True(want.Outcome == CorpusCopy.Outcome(answer), name + " on " + want.Copy + ": predict.txt says " + want.Outcome + ", dbchange-lite says " + CorpusCopy.Outcome(answer) + " with " + string.Join(", ", CorpusCopy.Blockers(answer)));
            var comparable = want.Blockers.Where(b => !b.StartsWith("Fits on ", StringComparison.Ordinal) && !b.StartsWith("KeyLength on ", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
            Assert.True(comparable.SequenceEqual(CorpusCopy.Blockers(answer), StringComparer.Ordinal), name + " on " + want.Copy + ": predict.txt blocks by " + string.Join(", ", comparable) + "; dbchange-lite blocks by " + string.Join(", ", CorpusCopy.Blockers(answer)));
            if (want.ProveIt)
            {
                Assert.True(CorpusCopy.NotPredictable(answer) > 0, name + " on " + want.Copy + ": predict.txt says prove it, and dbchange-lite lists nothing not predictable");
            }

            foreach (var code in File.Exists(findings) ? File.ReadAllLines(findings).Where(l => l.Length > 0) : [])
            {
                Assert.True(answer.GetProperty("findings").EnumerateArray().Any(f => f.GetProperty("code").GetString() == code), name + " on " + want.Copy + ": findings.txt names " + code + ", and dbchange-lite wrote none");
            }
        }
    }
}
