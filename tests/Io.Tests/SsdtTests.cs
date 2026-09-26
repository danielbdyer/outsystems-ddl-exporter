using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac.Model;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// io/Ssdt's build and load (WP 1.1): a classic project builds against the published tool folder with no Visual Studio,
/// into a folder under .dbchange/build/ that its inputs' fingerprint names; its package carries the refactorlog and both
/// deploy scripts, and Open reads them back. A broken .sql is build.failed naming its file and line; a machine without the SDK
/// band global.json names is sdk.missing with the remedy. Each test builds its own copy of tests/Golden/ under .dbchange/.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class SsdtTests(PublishedTool tool) : IDisposable
{
    private readonly string scratch = Path.Combine(Repository.Root, ".dbchange", "ssdt", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>The classic-minimal project's path from the root of <see cref="Golden"/>'s copy.</summary>
    private const string Minimal = "classic-minimal/ClassicMinimal.sqlproj";

    private string Output => Path.Combine(scratch, "build");

    /// <summary><see cref="Golden"/>'s copy standing in for a ref's worktree at a commit, which a build reads as a folder and a commit.</summary>
    private Git.Worktree At => new(Path.Combine(scratch, "golden"), "0123456789abcdef0123456789abcdef01234567");

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Exit", "M0.4")]
    public void The_classic_minimal_project_builds_into_the_folder_its_inputs_name_with_its_refactorlog_and_post_deploy_script()
    {
        var project = Golden();

        var dacpac = Ok(Ssdt.Build(project, tool.Folder, Output));

        using var package = ZipFile.OpenRead(dacpac.Path);
        var folder = Path.GetDirectoryName(dacpac.Path)!;
        Assert.Equal(Output, Path.GetDirectoryName(folder));
        Assert.StartsWith(Path.GetFileName(folder), dacpac.Inputs.ToString(), StringComparison.Ordinal);
        Assert.Superset(new HashSet<string>(["model.xml", "refactor.xml", "postdeploy.sql"]), package.Entries.Select(e => e.FullName).ToHashSet());
        Assert.DoesNotContain(Directory.GetDirectories(Path.GetDirectoryName(project)!), d => Path.GetFileName(d) is "bin" or "obj");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_project_with_a_pre_deploy_script_builds_both_deploy_scripts_in_and_Open_reads_them_the_model_and_the_rename()
    {
        var project = Golden();
        var directory = Path.GetDirectoryName(project)!;
        File.WriteAllText(Path.Combine(directory, "Script.PreDeployment.sql"), "PRINT N'pre-deploy ran';\n");
        Edited(project, new XElement(MsBuild + "ItemGroup", new XElement(MsBuild + "PreDeploy", new XAttribute("Include", "Script.PreDeployment.sql"))));

        using var package = Ok(Ssdt.Open(Ok(Ssdt.Build(project, tool.Folder, Output)).Path));

        Assert.Contains("pre-deploy ran", package.PreDeploy, StringComparison.Ordinal);
        Assert.Contains("post-deploy ran", package.PostDeploy, StringComparison.Ordinal);
        Assert.Contains(package.Model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass), t => t.Name.ToString() == "[dbo].[Customer]");
        var rename = Assert.Single(package.Refactors);
        Assert.Equal(
            new Ssdt.RefactorLogOperation("3f6a2c1e-8a4b-4d1e-9d2f-7c0b1a2e3d4f", "Rename Refactor", "09/23/2026 10:00:00", "[dbo].[Customer].[FirstName]", "SqlSimpleColumn", "[dbo].[Customer]", "SqlTable", "[GivenName]", null),
            rename);
        Assert.Equal(Ssdt.RefactorOperationKind.Rename, rename.Kind);
        Assert.Equal(package.Refactors, Ok(Ssdt.RefactorLog(Path.Combine(directory, "ClassicMinimal.refactorlog"))));
    }

    /// <summary>
    /// DECISIONS.md, 2026-09-24: a package is opened from its bytes, never by path, so nothing holds the file and no assembly the build wrote
    /// beside it loads into this process: the package deletes while it is open, and a plan of it against itself loads none of them.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_package_opens_from_one_stream_and_holds_its_assemblies_in_no_process()
    {
        var dacpac = Ok(Ssdt.Build(Golden(), tool.Folder, Output)).Path;
        var folder = Path.GetDirectoryName(dacpac)!;

        using (var package = Ok(Ssdt.Open(dacpac)))
        {
            File.Delete(dacpac);
            Assert.True(Ok(DacFx.Plan(package, package, "ClassicMinimal", Strict(), [])).Report.IsEmpty);
            Assert.False(File.Exists(dacpac));
        }

        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => !a.IsDynamic && a.Location.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A package whose refactor.xml SSDT could not have written is refused as a refactorlog, naming the part and the package; a missing package is named as missing.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_package_whose_refactor_xml_is_malformed_is_refused_as_a_refactorlog_naming_the_package_and_a_missing_one_as_absent()
    {
        var dacpac = Ok(Ssdt.Build(Golden(), tool.Folder, Output)).Path;
        using (var zip = ZipFile.Open(dacpac, ZipArchiveMode.Update))
        {
            zip.GetEntry("refactor.xml")!.Delete();
            using var log = new StreamWriter(zip.CreateEntry("refactor.xml").Open());
            log.Write("<Operations xmlns=\"http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02\"><Operation Key=\"k\" /></Operations>");
        }

        var malformed = Failed(Ssdt.Open(dacpac));
        var missing = Failed(Ssdt.Open(Path.Combine(scratch, "none.dacpac")));

        Assert.Equal("refactorlog.unreadable", malformed.Code);
        Assert.StartsWith("refactor.xml inside " + dacpac + " is not a refactorlog SSDT reads:", malformed.Message, StringComparison.Ordinal);
        Assert.Equal(("package.unreadable", "No package at " + Path.Combine(scratch, "none.dacpac") + "."), (missing.Code, missing.Message));
    }

    /// <summary>
    /// DF-9: the golden classic project declares no SQLCMD variable and targets Sql160; the same project given a SqlCmdVariable builds to a
    /// package that declares it (DacPackage.SqlCmdVariables), and a plan that gives it no value is refused before DacFx plans, naming the
    /// variable, while one whose profile gives it a value plans.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_declared_variable_with_no_value_is_refused_before_the_plan_and_names_the_variable()
    {
        var project = Golden();
        using (var golden = Ok(Ssdt.Open(Ok(Ssdt.Build(project, tool.Folder, Output)).Path)))
        {
            Assert.Equal(("", "Sql160"), (string.Join(",", golden.Declared), golden.Platform.ToString()));
        }

        Edited(project, new XElement(MsBuild + "ItemGroup", new XElement(MsBuild + "SqlCmdVariable", new XAttribute("Include", "Tag"), new XElement(MsBuild + "Value", "$(SqlCmdVar__1)"))));
        using var tagged = Ok(Ssdt.Open(Ok(Ssdt.Build(project, tool.Folder, Path.Combine(scratch, "tagged"))).Path));
        var given = Strict(("Tag", "dev"));

        var undefined = Failed(DacFx.Plan(tagged, tagged, "ClassicMinimal", Strict(), []));

        Assert.Equal(["Tag"], tagged.Declared.Select(n => n.ToString()));
        Assert.Equal("sqlcmd.undefined", undefined.Code);
        Assert.Contains("$(Tag)", undefined.Message, StringComparison.Ordinal);
        Assert.Contains(":setvar Tag \"dev\"", Ok(DacFx.Plan(tagged, tagged, "ClassicMinimal", given, [])).Script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The measurement DF-9's refusal waited on: a project whose SqlCmdVariable has a DefaultValue, planned by DacServices.Script with no
    /// value given, gets an empty value in its script (measured on DacFx 170.5.96): DacFx does not apply the project's default, so a declared
    /// variable no profile or environments file gives is refused whether or not the project defaults it.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_declared_variable_s_default_value_is_not_what_DacFx_plans_when_no_value_is_given()
    {
        var project = Golden();
        Edited(project, new XElement(MsBuild + "ItemGroup", new XElement(MsBuild + "SqlCmdVariable", new XAttribute("Include", "Tag"),
            new XElement(MsBuild + "DefaultValue", "dev"), new XElement(MsBuild + "Value", "$(SqlCmdVar__1)"))));
        using var package = Ok(Ssdt.Open(Ok(Ssdt.Build(project, tool.Folder, Output)).Path));

        var script = Microsoft.SqlServer.Dac.DacServices.Script(package.Dac, package.Dac, "ClassicMinimal",
            new Microsoft.SqlServer.Dac.PublishOptions { GenerateDeploymentScript = true, DeployOptions = Strict().Options() }).DatabaseScript;

        Assert.Equal([":setvar Tag \"\""], script.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith(":setvar Tag", StringComparison.Ordinal)));
        Assert.Equal("sqlcmd.undefined", Failed(DacFx.Plan(package, package, "ClassicMinimal", Strict(), [])).Code);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_syntax_error_in_a_sql_file_is_build_failed_naming_the_file_and_line()
    {
        var project = Golden();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "dbo", "Tables", "Customer.sql"), "CREATE TABLE [dbo].[Customer]\n(\n    [Id] INT NOT NULL,,\n);\n");

        var error = Failed(Ssdt.Build(project, tool.Folder, Output));

        Assert.Equal("build.failed", error.Code);
        Assert.Contains("dbo/Tables/Customer.sql(3,", error.Message, StringComparison.Ordinal);
        Assert.Contains("SQL46010", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_machine_without_the_SDK_band_global_json_names_fails_before_the_build_with_sdk_missing_and_the_remedy()
    {
        var pin = (string)JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "global.json")))!["sdk"]!["version"]!;

        var error = Failed(Ssdt.Build(Golden(), tool.Folder, Output, (_, _) => new Ran.Exited(0, "8.0.100 [sdk]\n9.0.314 [sdk]\n", "")));

        Assert.Equal("sdk.missing", error.Code);
        Assert.Contains(pin[..^2] + "xx", error.Message, StringComparison.Ordinal);
        Assert.Contains("Install the .NET SDK " + pin, error.Remedy, StringComparison.Ordinal);
        Assert.Contains("dbchange doctor", error.Remedy, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Output));
    }

    /// <summary>
    /// Fact 3 of the specification: MSBuild splits a property value at ',' and ';' (MSB1006) and unescapes %XX, so a checkout under a folder such as
    /// C:\Users\Doe, John\ failed to build until the build escaped each path it passes as a property. The package lands under that folder.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_project_under_a_folder_whose_name_holds_a_comma_a_semicolon_and_a_percent_sign_builds()
    {
        const string Folder = "Doe, John; 50%41";
        var project = Golden(Folder);

        var dacpac = Ok(Ssdt.Build(project, tool.Folder, Path.Combine(scratch, Folder, "build")));

        Assert.Contains(Path.DirectorySeparatorChar + Folder + Path.DirectorySeparatorChar, dacpac.Path, StringComparison.Ordinal);
        Assert.True(File.Exists(dacpac.Path), dacpac.Path + " was not written");
    }

    /// <summary>
    /// Fact 1 of the specification: the SDK writes MSBuild's lines to a pipe as UTF-8, and a build read with the console's code page (437 on a
    /// Windows console) turned a non-ASCII folder in the " -> …dacpac" line into another path, so the build of a checkout under such a folder
    /// was refused as build.failed. io/Command reads the build as UTF-8 whatever the console holds, and the package's path is found.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_project_under_a_folder_named_in_other_than_ASCII_builds_whatever_the_console_s_code_page()
    {
        var project = Golden("Café-Ω");

        var dacpac = Ok(Ssdt.Build(project, tool.Folder, Path.Combine(scratch, "Café-Ω", "build")));

        Assert.Contains(Path.DirectorySeparatorChar + "Café-Ω" + Path.DirectorySeparatorChar, dacpac.Path, StringComparison.Ordinal);
        Assert.True(File.Exists(dacpac.Path), dacpac.Path + " was not written");
    }

    /// <summary>
    /// The variables an enclosing MSBuild sets (MSBuildSDKsPath, MSBuildExtensionsPath, MSBUILD_EXE_PATH, as when dbchange runs inside dotnet test
    /// or an Exec task) reach the build's environment beneath the build's own settings, and a classic project still builds under bogus values.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_classic_project_under_a_bogus_MSBuildSDKsPath_builds()
    {
        Ran Enclosed(Command c, System.Threading.CancellationToken t)
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["MSBuildSDKsPath"] = Path.Combine(scratch, "bogus", "sdks"), ["MSBuildExtensionsPath"] = Path.Combine(scratch, "bogus", "ext"), ["MSBUILD_EXE_PATH"] = Path.Combine(scratch, "bogus", "MSBuild.dll"),
            };
            foreach (var (name, value) in c.Environment)
            {
                environment[name] = value;   // the build's own settings win
            }

            return Command.Run(c with { Environment = environment }, t);
        }

        var dacpac = Ok(Ssdt.Build(Golden(), tool.Folder, Output, Enclosed));

        Assert.True(File.Exists(dacpac.Path), dacpac.Path + " was not written");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_build_past_its_timeout_is_build_timed_out_quoting_its_last_lines()
    {
        Ran Hangs(Command c, System.Threading.CancellationToken t) => c.Arguments[0] == "build" ? new Ran.TimedOut(c.Timeout, "  Determining projects to restore...\n  ClassicMinimal -> building\n", "") : Command.Run(c, t);

        var error = Failed(Ssdt.Build(Golden(), tool.Folder, Output, Hangs));

        Assert.Equal("build.timed-out", error.Code);
        Assert.Contains("10 minutes", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("ClassicMinimal -> building", error.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet build ClassicMinimal.sqlproj -v:n", error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ruling of 2026-09-25: a second build of one commit against the same targets returns the package the first wrote and asks the
    /// runner for nothing; a changed byte in the tool folder's SqlTasks targets changes the targets' fingerprint, and the build runs again,
    /// into another folder under the commit's.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_second_build_of_one_commit_with_the_same_targets_runs_no_dotnet()
    {
        Golden();
        var folder = Path.Combine(scratch, "tool");
        ToolFolderTests.Copy(tool.Folder, folder);
        var calls = new List<Command>();
        Ran Counted(Command c, System.Threading.CancellationToken t)
        {
            calls.Add(c);
            return Command.Run(c, t);
        }

        var first = Ok(Ssdt.Build(At, Minimal, folder, Output, Counted));
        var afterFirst = calls.Count;
        var second = Ok(Ssdt.Build(At, Minimal, folder, Output, Counted));
        var afterSecond = calls.Count;
        File.AppendAllText(Path.Combine(folder, Ssdt.BuildTargets.Targets), "\n");
        var third = Ok(Ssdt.Build(At, Minimal, folder, Output, Counted));

        Assert.Equal(first, second);
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(Path.Combine(Output, At.Commit), Path.GetDirectoryName(Path.GetDirectoryName(first.Path)));
        Assert.NotEqual(first.Targets.Fingerprint, third.Targets.Fingerprint);
        Assert.NotEqual(Path.GetDirectoryName(first.Path), Path.GetDirectoryName(third.Path));
        Assert.Equal(2, calls.Count(c => c.Arguments[0] == "build"));
    }

    /// <summary>A package whose bytes changed after its build no longer fingerprints as its marker says, so the next build of that commit runs dotnet again and writes it anew.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_cached_package_whose_bytes_changed_is_built_again()
    {
        Golden();
        var builds = 0;
        Ran Counted(Command c, System.Threading.CancellationToken t)
        {
            builds += c.Arguments[0] == "build" ? 1 : 0;
            return Command.Run(c, t);
        }

        var first = Ok(Ssdt.Build(At, Minimal, tool.Folder, Output, Counted));
        File.WriteAllBytes(first.Path, [0x50]);
        var second = Ok(Ssdt.Build(At, Minimal, tool.Folder, Output, Counted));

        Assert.Equal((2, first.Path), (builds, second.Path));
        using var package = Ok(Ssdt.Open(second.Path));
        Assert.Contains(package.Model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass), t => t.Name.ToString() == "[dbo].[Customer]");
    }

    /// <summary>Two builds of one commit at once take the output folder's lock in turn: one runs dotnet, and the other, once the lock is free, returns the package the first wrote.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public async Task Two_processes_building_one_commit_serialize_on_the_lock()
    {
        Golden();
        var builds = 0;
        Ran Slow(Command c, System.Threading.CancellationToken t)
        {
            if (c.Arguments[0] == "build")
            {
                System.Threading.Interlocked.Increment(ref builds);
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));
            }

            return Command.Run(c, t);
        }

        var both = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => Ssdt.Build(At, Minimal, tool.Folder, Output, Slow))));

        Assert.Equal(1, builds);
        Assert.Equal(Ok(both[0]), Ok(both[1]));
    }

    /// <summary>A build that exits 0 and names no package it wrote (a project whose OutputType is not Database) is build.no-package, whose remedy names OutputType, and not build.failed.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_project_that_writes_no_package_is_refused_as_no_package_and_not_as_a_failed_build()
    {
        Ran Quiet(Command c, System.Threading.CancellationToken t) => c.Arguments[0] == "build" ? new Ran.Exited(0, "  Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n", "") : Command.Run(c, t);

        var error = Failed(Ssdt.Build(Golden(), tool.Folder, Output, Quiet));

        Assert.Equal("build.no-package", error.Code);
        Assert.Contains("OutputType", error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// §1.1: a tool folder whose build task is not the DacFx dbchange runs is refused before the build, as toolchain.targets-mismatch:
    /// an empty file in the task's place carries no file version, and a task of another release names both releases.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_tool_folder_whose_task_is_another_DacFx_is_refused_before_the_build()
    {
        var folder = Path.Combine(scratch, "mismatched");
        foreach (var file in (string[])[Ssdt.BuildTargets.Targets, "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, file))!);
            File.Copy(Path.Combine(tool.Folder, file), Path.Combine(folder, file));
        }

        File.WriteAllBytes(Path.Combine(folder, Ssdt.BuildTargets.Task), []);
        var builds = 0;

        var empty = Failed(Ssdt.Build(Golden(), folder, Output, (c, t) =>
        {
            builds += c.Arguments[0] == "build" ? 1 : 0;
            return Command.Run(c, t);
        }));
        var older = Failed(Ssdt.BuildTargets.Of(tool.Folder, DacFxVersion.Of("170.4.71")));

        Assert.Equal(("toolchain.targets-mismatch", 0), (empty.Code, builds));
        Assert.Contains("no file version", empty.Message, StringComparison.Ordinal);
        Assert.Equal("The tool folder's build task is DacFx 170.5.96 and dbchange runs DacFx 170.4.71.", older.Message);
        Assert.Contains("ci/publish.sh", older.Remedy, StringComparison.Ordinal);
    }

    /// <summary>The command the build runs: dotnet build from the project's folder, for ten minutes, telemetry and the SDK's first-run actions off, English, UTF-8, no MSBuild server, DBCHANGE_SQL withheld.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_build_runs_dotnet_from_the_project_s_folder_with_telemetry_and_first_run_actions_off_and_DBCHANGE_SQL_withheld()
    {
        Command? built = null;
        Ran Records(Command c, System.Threading.CancellationToken t)
        {
            built = c.Arguments[0] == "build" ? c : built;
            return c.Arguments[0] == "build" ? new Ran.Exited(1, "", "") : Command.Run(c, t);
        }

        var project = Golden();
        Failed(Ssdt.Build(project, tool.Folder, Output, Records));

        Assert.NotNull(built);
        Assert.Equal(("dotnet", Path.GetDirectoryName(project), Ssdt.BuildTimeout), (built.Program, built.Directory, built.Timeout));
        Assert.Equal(["DACFX_TELEMETRY_OPTOUT=1", "DBCHANGE_SQL=", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false", "DOTNET_CLI_FORCE_UTF8_ENCODING=1", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "DOTNET_CLI_UI_LANGUAGE=en-US",
            "DOTNET_CLI_USE_MSBUILD_SERVER=0", "DOTNET_GENERATE_ASPNET_CERTIFICATE=false", "DOTNET_NOLOGO=1"], built.Environment.Select(v => v.Key + "=" + v.Value).Order(StringComparer.Ordinal));
        Assert.Contains("-nodeReuse:false", built.Arguments);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_file_that_is_not_a_package_or_a_refactorlog_is_package_unreadable_or_refactorlog_unreadable()
    {
        var sql = Path.Combine(Path.GetDirectoryName(Golden())!, "dbo", "Tables", "Customer.sql");

        Assert.Equal("package.unreadable", Failed(Ssdt.Open(sql)).Code);
        Assert.Equal("refactorlog.unreadable", Failed(Ssdt.RefactorLog(sql)).Code);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_tool_folder_is_the_running_dbchange_s_then_DBCHANGE_TOOL_s_then_the_repository_s_dist_dbchange_and_otherwise_tool_missing()
    {
        var machine = Directory.CreateTempSubdirectory("dbchange-tool-").FullName;   // outside the repository, so no dist/dbchange above it
        try
        {
            var (running, named, bare) = (Published(machine, "running"), Published(machine, "named"), Directory.CreateDirectory(Path.Combine(machine, "bare")).FullName);
            var dist = Published(machine, Path.Combine("repository", "dist", "dbchange"));
            var inside = Directory.CreateDirectory(Path.Combine(machine, "repository", "src", "db")).FullName;

            Assert.Equal(running, Ok(Ssdt.Tool(running, named, inside)));
            Assert.Equal(named, Ok(Ssdt.Tool(bare, named, inside)));
            Assert.Equal(dist, Ok(Ssdt.Tool(bare, null, inside)));
            Assert.All([Ssdt.Tool(bare, bare, inside), Ssdt.Tool(bare, null, bare)], r => Assert.Equal("tool.missing", Failed(r).Code));
        }
        finally
        {
            Directory.Delete(machine, recursive: true);
        }
    }

    /// <summary>A fresh copy of tests/Golden/ under the scratch folder's <paramref name="under"/>, its stop files included so the engine's build settings stay out; its classic-minimal project.</summary>
    private string Golden(string under = "golden")
    {
        var from = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(from, f)))
        {
            if (!file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch, under, file))!);
                File.Copy(Path.Combine(from, file), Path.Combine(scratch, under, file));
            }
        }

        return Path.Combine(scratch, under, "classic-minimal", "ClassicMinimal.sqlproj");
    }

    /// <summary>A folder holding, empty, the files a published tool folder carries beside dbchange.</summary>
    private static string Published(string root, string name)
    {
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, name, file))!);
            File.WriteAllText(Path.Combine(root, name, file), "");
        }

        return Path.Combine(root, name);
    }

    private static readonly XNamespace MsBuild = "http://schemas.microsoft.com/developer/msbuild/2003";

    /// <summary>The project with one more element under its root, saved in place.</summary>
    private static void Edited(string project, XElement added)
    {
        var xml = XDocument.Load(project);
        xml.Root!.Add(added);
        xml.Save(project);
    }

    /// <summary>The golden pipeline profile; with values given, a copy of it under the scratch folder that gives each as a SQLCMD variable.</summary>
    private PublishProfile.Strict Strict(params (string Name, string Value)[] values)
    {
        var pipeline = GoldenProject.Profile;
        if (values.Length == 0)
        {
            return Ok(PublishProfiles.Load(pipeline));
        }

        var profile = XDocument.Load(pipeline);
        profile.Root!.Add(new XElement(MsBuild + "ItemGroup", values.Select(v => new XElement(MsBuild + "SqlCmdVariable", new XAttribute("Include", v.Name), new XElement(MsBuild + "Value", v.Value)))));
        var path = Path.Combine(scratch, "given.publish.xml");
        Directory.CreateDirectory(scratch);
        profile.Save(path);
        return Ok(PublishProfiles.Load(path));
    }

    private static T Ok<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    private static Error Failed<T>(Result<T> result) => Assert.IsType<Result<T>.Failed>(result).Error;
}
