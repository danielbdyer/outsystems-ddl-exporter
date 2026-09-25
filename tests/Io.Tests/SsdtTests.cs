using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Microsoft.SqlServer.Dac.Model;
using Xunit;
using Contract = Estate.Cli.Contract;

namespace Estate.Io.Tests;

/// <summary>
/// io/Ssdt's build and load (WP 1.1): a classic project builds against the published tool folder with no Visual Studio,
/// into a folder under .estate/build/ that its inputs' fingerprint names; its package carries the refactorlog and both
/// deploy scripts, and Load reads them back. A broken .sql is exit 7 naming its file and line; a machine without the SDK
/// band global.json names is exit 6 with the remedy. Each test builds its own copy of tests/Golden/ under .estate/.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class SsdtTests(PublishedTool tool) : IDisposable
{
    private readonly string scratch = Path.Combine(Repository.Root, ".estate", "ssdt", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);

    private string Output => Path.Combine(scratch, "build");

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
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
    public void A_project_with_a_pre_deploy_script_builds_both_deploy_scripts_in_and_Load_reads_them_the_model_and_the_rename()
    {
        var project = Golden();
        var directory = Path.GetDirectoryName(project)!;
        File.WriteAllText(Path.Combine(directory, "Script.PreDeployment.sql"), "PRINT N'pre-deploy ran';\n");
        var xml = XDocument.Load(project);
        XNamespace msbuild = "http://schemas.microsoft.com/developer/msbuild/2003";
        xml.Root!.Add(new XElement(msbuild + "ItemGroup", new XElement(msbuild + "PreDeploy", new XAttribute("Include", "Script.PreDeployment.sql"))));
        xml.Save(project);

        using var package = Ok(Ssdt.Load(Ok(Ssdt.Build(project, tool.Folder, Output)).Path));

        Assert.Contains("pre-deploy ran", package.PreDeploy, StringComparison.Ordinal);
        Assert.Contains("post-deploy ran", package.PostDeploy, StringComparison.Ordinal);
        Assert.Contains(package.Model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass), t => t.Name.ToString() == "[dbo].[Customer]");
        Assert.Equal(
            new Ssdt.RefactorEntry("3f6a2c1e-8a4b-4d1e-9d2f-7c0b1a2e3d4f", "Rename Refactor", "09/23/2026 10:00:00", "[dbo].[Customer].[FirstName]", "SqlSimpleColumn", "[dbo].[Customer]", "SqlTable", "[GivenName]", null),
            Assert.Single(package.Refactors));
        Assert.Equal(package.Refactors, Ok(Ssdt.RefactorLog(Path.Combine(directory, "ClassicMinimal.refactorlog"))));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_syntax_error_in_a_sql_file_is_the_build_failed_error_exit_7_naming_the_file_and_line()
    {
        var project = Golden();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "dbo", "Tables", "Customer.sql"), "CREATE TABLE [dbo].[Customer]\n(\n    [Id] INT NOT NULL,,\n);\n");

        var error = Failed(Ssdt.Build(project, tool.Folder, Output));

        Assert.Equal(("build.failed", 7), (error.Code, Contract.Exit(error)));
        Assert.Contains("dbo/Tables/Customer.sql(3,", error.Message, StringComparison.Ordinal);
        Assert.Contains("SQL46010", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_machine_without_the_SDK_band_global_json_names_fails_before_the_build_with_exit_6_and_the_remedy()
    {
        var pin = (string)JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "global.json")))!["sdk"]!["version"]!;

        var error = Failed(Ssdt.Build(Golden(), tool.Folder, Output, (_, _) => new Ran.Exited(0, "8.0.100 [sdk]\n9.0.314 [sdk]\n", "")));

        Assert.Equal(("sdk.missing", 6), (error.Code, Contract.Exit(error)));
        Assert.Contains(pin[..^2] + "xx", error.Message, StringComparison.Ordinal);
        Assert.Contains("Install the .NET SDK " + pin, error.Remedy, StringComparison.Ordinal);
        Assert.Contains("estate doctor", error.Remedy, StringComparison.Ordinal);
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
    /// The variables an enclosing MSBuild sets (MSBuildSDKsPath, MSBuildExtensionsPath, MSBUILD_EXE_PATH, as when estate runs inside dotnet test
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
    public void A_build_past_its_timeout_is_build_timed_out_at_exit_7_quoting_its_last_lines()
    {
        Ran Hangs(Command c, System.Threading.CancellationToken t) => c.Arguments[0] == "build" ? new Ran.TimedOut(c.Timeout, "  Determining projects to restore...\n  ClassicMinimal -> building\n", "") : Command.Run(c, t);

        var error = Failed(Ssdt.Build(Golden(), tool.Folder, Output, Hangs));

        Assert.Equal(("build.timed-out", 7), (error.Code, Contract.Exit(error)));
        Assert.Contains("10 minutes", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("ClassicMinimal -> building", error.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet build ClassicMinimal.sqlproj -v:n", error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>The command the build runs: dotnet build from the project's folder, for ten minutes, telemetry and the SDK's first-run actions off, English, UTF-8, no MSBuild server, ESTATE_SQL withheld.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_build_runs_dotnet_from_the_project_s_folder_with_telemetry_and_first_run_actions_off_and_ESTATE_SQL_withheld()
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
        Assert.Equal(["DACFX_TELEMETRY_OPTOUT=1", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false", "DOTNET_CLI_FORCE_UTF8_ENCODING=1", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "DOTNET_CLI_UI_LANGUAGE=en-US",
            "DOTNET_CLI_USE_MSBUILD_SERVER=0", "DOTNET_GENERATE_ASPNET_CERTIFICATE=false", "DOTNET_NOLOGO=1", "ESTATE_SQL="], built.Environment.Select(v => v.Key + "=" + v.Value).Order(StringComparer.Ordinal));
        Assert.Contains("-nodeReuse:false", built.Arguments);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_file_that_is_not_a_package_or_a_refactorlog_is_unparsed_input_at_exit_2()
    {
        var sql = Path.Combine(Path.GetDirectoryName(Golden())!, "dbo", "Tables", "Customer.sql");

        Assert.Equal(("package.unreadable", 2), (Failed(Ssdt.Load(sql)).Code, Contract.Exit(Failed(Ssdt.Load(sql)))));
        Assert.Equal(("refactorlog.unreadable", 2), (Failed(Ssdt.RefactorLog(sql)).Code, Contract.Exit(Failed(Ssdt.RefactorLog(sql)))));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_tool_folder_is_the_running_estates_then_ESTATE_TOOLs_then_the_repositorys_dist_estate_and_otherwise_exit_6()
    {
        var machine = Directory.CreateTempSubdirectory("estate-tool-").FullName;   // outside the repository, so no dist/estate above it
        try
        {
            var (running, named, bare) = (Published(machine, "running"), Published(machine, "named"), Directory.CreateDirectory(Path.Combine(machine, "bare")).FullName);
            var dist = Published(machine, Path.Combine("repository", "dist", "estate"));
            var inside = Directory.CreateDirectory(Path.Combine(machine, "repository", "src", "db")).FullName;

            Assert.Equal(running, Ok(Ssdt.Tool(running, named, inside)));
            Assert.Equal(named, Ok(Ssdt.Tool(bare, named, inside)));
            Assert.Equal(dist, Ok(Ssdt.Tool(bare, null, inside)));
            Assert.All([Ssdt.Tool(bare, bare, inside), Ssdt.Tool(bare, null, bare)], r => Assert.Equal(("tool.missing", 6), (Failed(r).Code, Contract.Exit(Failed(r)))));
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

    /// <summary>A folder holding, empty, the files a published tool folder carries beside estate.</summary>
    private static string Published(string root, string name)
    {
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, name, file))!);
            File.WriteAllText(Path.Combine(root, name, file), "");
        }

        return Path.Combine(root, name);
    }

    private static T Ok<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    private static Error Failed<T>(Result<T> result) => Assert.IsType<Result<T>.Failed>(result).Error;
}
