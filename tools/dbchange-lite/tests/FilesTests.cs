using System;
using System.IO;
using System.Linq;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class FilesTests
{
    private const string Profile = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Project ToolsVersion=\"4.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n  <PropertyGroup>\n"
        + "    <TargetDatabaseName>Estate</TargetDatabaseName>\n    <BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>\n    <GenerateSmartDefaults>False</GenerateSmartDefaults>\n"
        + "    <ProfileVersionNumber>1</ProfileVersionNumber>\n  </PropertyGroup>\n  <ItemGroup>\n    <SqlCmdVariable Include=\"EnvironmentTag\">\n      <Value>dev</Value>\n    </SqlCmdVariable>\n  </ItemGroup>\n</Project>\n";

    private const string EnvironmentsJson = "{ \"environments\": { \"dev\": { \"host\": \"dev-sql.corp.example\", \"connection\": \"env:DBCHANGE_LITE_TEST_DEV\", \"profile\": \"profiles/pipeline.publish.xml\","
        + " \"sqlcmd\": { \"EnvironmentTag\": { \"literal\": \"dev\", \"sensitive\": false } }, \"readerGroups\": [\"developers\"], \"deployOrder\": 1 },"
        + " \"prod\": { \"host\": \"prod-sql.corp.example\", \"connection\": \"env:DBCHANGE_LITE_TEST_PROD\", \"profile\": \"profiles/pipeline.publish.xml\", \"neverRead\": true, \"deployOrder\": 4 } } }";

    [Fact]
    [Trait("Category", "fast")]
    public void The_environments_file_is_read_as_v3_defines_it_with_neverRead_and_deployOrder()
    {
        using var scratch = new Scratch();
        scratch.Write("dbchange/environments.json", EnvironmentsJson);
        var environments = EnvironmentsFile.Read(scratch.Path);
        Assert.Equal(["dev", "prod"], environments.All.Select(e => e.Name).ToArray());
        Assert.True(environments.Named("prod").NeverRead);
        Assert.Equal("dev", environments.Named("dev").SqlCmd["EnvironmentTag"]);
        Assert.Equal(["dev", "prod"], environments.InDeployOrder(["prod", "dev"]).Select(e => e.Name).ToArray());
        Assert.Equal("environments.unknown", Assert.Throws<Stop>(() => environments.Named("qa")).Finding.Code);
        Assert.Equal(scratch.Path, Run.RootOf(scratch.Under("some/deeper/folder")));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_literal_connection_or_a_password_in_the_environments_file_is_refused_quoting_nothing()
    {
        using var scratch = new Scratch();
        scratch.Write("dbchange/environments.json", "{ \"environments\": { \"dev\": { \"host\": \"h\", \"connection\": \"Server=h;Database=d;Integrated Security=true\", \"profile\": \"p\" } } }");
        var literal = Assert.Throws<Stop>(() => EnvironmentsFile.Read(scratch.Path));
        Assert.Equal(6, literal.Outcome.Exit);
        Assert.Equal("environments.connection", literal.Finding.Code);
        Assert.DoesNotContain("Server=h", literal.Finding.Message, StringComparison.Ordinal);
        scratch.Write("dbchange/environments.json", "{ \"environments\": { \"dev\": { \"host\": \"h\", \"connection\": \"env:X\", \"profile\": \"p\", \"sqlcmd\": { \"Pw\": \"Password=secret1\" } } } }");
        var password = Assert.Throws<Stop>(() => EnvironmentsFile.Read(scratch.Path));
        Assert.Equal("environments.literal-credential", password.Finding.Code);
        Assert.DoesNotContain("secret1", password.Finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_reference_resolves_from_the_environment_or_a_file_and_an_unset_one_is_refused()
    {
        using var scratch = new Scratch();
        Environment.SetEnvironmentVariable("DBCHANGE_LITE_TEST_REF", "Server=x;Initial Catalog=y");
        Assert.Equal("Server=x;Initial Catalog=y", EnvironmentsFile.Resolve(scratch.Path, "env:DBCHANGE_LITE_TEST_REF"));
        Environment.SetEnvironmentVariable("DBCHANGE_LITE_TEST_REF", null);
        Assert.Equal("environments.reference-unset", Assert.Throws<Stop>(() => EnvironmentsFile.Resolve(scratch.Path, "env:DBCHANGE_LITE_TEST_REF")).Finding.Code);
        scratch.Write(".dbchange/principals/dev.connection", "Server=x;Initial Catalog=y\n");
        Assert.Equal("Server=x;Initial Catalog=y", EnvironmentsFile.Resolve(scratch.Path, "file:.dbchange/principals/dev.connection"));
        Assert.Equal("environments.reference-missing", Assert.Throws<Stop>(() => EnvironmentsFile.Resolve(scratch.Path, "file:nowhere.connection")).Finding.Code);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Hosts_are_local_by_loopback_or_LocalDB_and_the_same_by_spelling()
    {
        Assert.True(Hosts.IsLocal("127.0.0.1,11433"));
        Assert.True(Hosts.IsLocal("localhost"));
        Assert.True(Hosts.IsLocal(@"(localdb)\MSSQLLocalDB"));
        Assert.True(Hosts.IsLocal("."));
        Assert.False(Hosts.IsLocal("dev-sql.corp.example"));
        Assert.True(Hosts.Same("localhost", "LOCALHOST,1433"));
        Assert.False(Hosts.Same("127.0.0.1", "corpus.invalid"));
        Assert.Equal("host", Hosts.Of(@"tcp:host\inst,1433"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_publish_profile_loads_through_DacProfile_with_the_same_hash_under_LF_and_CRLF_and_refuses_a_password()
    {
        using var scratch = new Scratch();
        scratch.Write("profiles/lf.publish.xml", Profile);
        scratch.Write("profiles/crlf.publish.xml", Profile.Replace("\n", "\r\n", StringComparison.Ordinal));
        var lf = PublishProfiles.Load(scratch.Path, "profiles/lf.publish.xml");
        var crlf = PublishProfiles.Load(scratch.Path, "profiles/crlf.publish.xml");
        Assert.Equal(lf.Hash, crlf.Hash);
        Assert.StartsWith("sha256:", lf.Hash, StringComparison.Ordinal);
        Assert.True(lf.Options.BlockOnPossibleDataLoss);
        Assert.False(lf.Options.GenerateSmartDefaults);
        Assert.Equal("dev", lf.SqlCmd["EnvironmentTag"]);
        scratch.Write("profiles/pw.publish.xml", Profile.Replace("<TargetDatabaseName>Estate</TargetDatabaseName>", "<TargetConnectionString>Server=h;Password=s3</TargetConnectionString>", StringComparison.Ordinal));
        var stop = Assert.Throws<Stop>(() => PublishProfiles.Load(scratch.Path, "profiles/pw.publish.xml"));
        Assert.Equal("profile.credential", stop.Finding.Code);
        Assert.DoesNotContain("s3", stop.Finding.Message, StringComparison.Ordinal);
        Assert.Equal("profile.missing", Assert.Throws<Stop>(() => PublishProfiles.Load(scratch.Path, "profiles/none.publish.xml")).Finding.Code);
        var proof = PublishProfiles.ForProof(PublishProfiles.Load(scratch.Path, "profiles/lf.publish.xml").Options);
        Assert.True(proof.BlockOnPossibleDataLoss);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_toolchain_ledger_gives_the_pin_the_release_before_and_the_confirmed_profiles_and_holds_the_window()
    {
        using var scratch = new Scratch();
        Assert.Equal("UNPINNED", ToolchainLedger.Read(scratch.Path).Pin);
        scratch.Write("dbchange/ledgers/toolchain.md", "# The toolchain\n\n| Date | dbchange | Pinned DacFx | Release before |\n|---|---|---|---|\n| 2026-09-24 | 3.0.0 | UNPINNED | — |\n| 2026-10-30 | 3.0.1 | "
            + DacFxRelease.Running + " | 162.5.57 |\n\n| Date | Profile | SHA-256 | Confirmed by |\n|---|---|---|---|\n| 2026-10-30 | dbchange/profiles/pipeline.publish.xml | sha256:abc | the deployment step's owner |\n");
        var ledger = ToolchainLedger.Read(scratch.Path);
        Assert.Equal(DacFxRelease.Running, ledger.Pin);
        Assert.Equal("162.5.57", ledger.ReleaseBefore);
        Assert.True(ledger.Confirms("abc"));
        Assert.False(ledger.Confirms("sha256:def"));
        ledger.HoldWindow();
        var stop = Assert.Throws<Stop>(() => (ledger with { Pin = "150.0.0", ReleaseBefore = null }).HoldWindow());
        Assert.Equal("toolchain.outside-window", stop.Finding.Code);
        (ledger with { Pin = "150.0.0", ReleaseBefore = DacFxRelease.Running }).HoldWindow();
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_source_prints_as_it_is_written_and_two_loopback_hosts_are_one_machine()
    {
        foreach (var text in new[] { "ref:HEAD", "dacpac:release/1.4.dacpac", "project:src/Estate" })
        {
            Assert.Equal(text, Source.Parse(text, "--to").ToString());
        }

        Assert.True(Hosts.Same("127.0.0.1,11433", "localhost"));
        Assert.True(Hosts.Same("localhost", "(localdb)\\MSSQLLocalDB"));
        Assert.False(Hosts.Same("127.0.0.1", "db.corp.invalid"));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_one_project_of_a_worktree_under_dbchange_runs_is_found_and_a_copy_under_bin_is_not()
    {
        using var scratch = new Scratch();
        var worktree = Path.Combine(".dbchange", "runs", "20261024T000000Z-predict", "src-0123456789ab");
        var project = scratch.Write(Path.Combine(worktree, "project", "Estate.sqlproj"), "<Project />");
        scratch.Write(Path.Combine(worktree, "project", "bin", "Estate.sqlproj"), "<Project />");
        Assert.Equal(project, Ssdt.OneProject(scratch.Under(worktree)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_fingerprint_drops_the_byte_order_mark_and_the_line_ending_before_hashing()
    {
        var plain = Fingerprint.OfText("a\nb\n"u8.ToArray());
        Assert.Equal(plain, Fingerprint.OfText("﻿a\r\nb\r\n"u8.ToArray()));
        Assert.Equal(plain, Fingerprint.OfString("a\r\nb\r\n"));
        Assert.True(Fingerprint.Same(plain, plain[7..].ToUpperInvariant()));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("\"dev\": \"env:X\"", "environments.value")]
    [InlineData("\"dev\": { \"host\": \"h\", \"connection\": \"env:X\", \"profile\": \"p\", \"neverRead\": \"yes\" }", "environments.neverRead")]
    [InlineData("\"dev\": { \"host\": \"h\", \"connection\": \"env:X\", \"profile\": \"p\", \"deployOrder\": \"first\" }", "environments.deployOrder")]
    [InlineData("\"dev\": { \"host\": \"h\", \"connection\": \"env:X\", \"profile\": \"p\", \"deployOrder\": 1.5 }", "environments.deployOrder")]
    public void An_environment_entry_of_the_wrong_kind_is_configuration_refused_naming_its_key(string entry, string code)
    {
        using var scratch = new Scratch();
        scratch.Write("dbchange/environments.json", "{ \"environments\": { " + entry + " } }");
        var stop = Assert.Throws<Stop>(() => EnvironmentsFile.Read(scratch.Path));
        Assert.Equal((6, code), (stop.Outcome.Exit, stop.Finding.Code));
        Assert.Equal("dev", stop.Finding.Subject);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Two_runs_of_one_verb_in_the_same_second_from_two_processes_write_to_two_folders()
    {
        using var scratch = new Scratch();
        var run = Runs.At(scratch.Path, "predict");
        Assert.EndsWith("-" + Environment.ProcessId + "-predict", run.Folder, StringComparison.Ordinal);
        Assert.Equal(scratch.Path, run.WorkingDirectory);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_proof_options_keep_the_profiles_other_options_and_its_SQLCMD_values_and_force_BlockOnPossibleDataLoss()
    {
        var options = new Microsoft.SqlServer.Dac.DacDeployOptions { BlockOnPossibleDataLoss = false, GenerateSmartDefaults = true, DropObjectsNotInSource = true, CommandTimeout = 77 };
        options.SqlCommandVariableValues["EnvironmentTag"] = "qa";
        var proof = PublishProfiles.ForProof(options);
        Assert.True(proof.BlockOnPossibleDataLoss);
        Assert.Equal((true, true, 77), (proof.GenerateSmartDefaults, proof.DropObjectsNotInSource, proof.CommandTimeout));
        Assert.Equal("qa", proof.SqlCommandVariableValues["EnvironmentTag"]);
        Assert.False(options.BlockOnPossibleDataLoss);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_cached_package_is_built_once_and_leaves_no_partial_file_beside_it()
    {
        using var scratch = new Scratch();
        var run = Runs.At(scratch.Path, "predict");
        var source = Dacpacs.Of(scratch.Under("built.dacpac"), "CREATE TABLE [dbo].[T] ([Id] INT NOT NULL);");
        var builds = 0;
        var first = Cache.Package(run, "sha256:abc/def", () => { builds++; return source; });
        var second = Cache.Package(run, "sha256:abc/def", () => { builds++; return source; });
        Assert.Equal((1, first), (builds, second));
        Assert.Equal("abc_def.dacpac", Path.GetFileName(first));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(first)!, "*.partial"));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("exec xp_msver", System.Data.CommandType.Text, true)]
    [InlineData("  EXEC master.dbo.xp_msver N'ProductVersion'", System.Data.CommandType.Text, true)]
    [InlineData("xp_msver", System.Data.CommandType.StoredProcedure, true)]
    [InlineData("SELECT N'xp_msver'", System.Data.CommandType.Text, false)]
    [InlineData("SELECT SERVERPROPERTY('ProductVersion')", System.Data.CommandType.Text, false)]
    public void The_server_probe_refuses_an_EXEC_of_xp_msver_and_lets_a_SELECT_that_names_it_pass(string text, System.Data.CommandType type, bool refused)
    {
        using var command = new Microsoft.Data.SqlClient.SqlCommand(text) { CommandType = type };
        Assert.Equal(refused, ServerProbe.Refuses(command));
    }
}
