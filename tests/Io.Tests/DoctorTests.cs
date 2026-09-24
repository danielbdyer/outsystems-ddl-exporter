using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Estate.Budgets.Tests;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>io/Doctor at M0: four read-only checks, and a remedy for each item missing (M0 exit 3), on a machine the test describes.</summary>
public sealed class DoctorTests : IDisposable
{
    private readonly string machine = Directory.CreateTempSubdirectory("estate-doctor-").FullName;

    public void Dispose() => Directory.Delete(machine, recursive: true);

    [Fact]
    [Trait("Category", "fast")]
    public void A_bare_machine_gets_a_remedy_for_each_item_missing()
    {
        var checks = Doctor.Examine(machine, machine, (_, _) => null);   // no global.json, no tool folder, and nothing installed

        Assert.Equal(["sdk", "tool", "substrate", "image"], checks.Select(c => c.Item));
        Assert.Equal(["sdk", "tool", "substrate"], checks.Where(c => c.Remedy is not null).Select(c => c.Item));
        Assert.All(checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Found)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_machine_with_what_M0_checks_misses_nothing()
    {
        Publish();
        File.WriteAllText(Path.Combine(machine, "global.json"), """{ "sdk": { "version": "10.0.401", "rollForward": "latestPatch" } }""");

        var checks = Doctor.Examine(machine, Directory.CreateDirectory(Path.Combine(machine, "estate", "src")).FullName, Answers(new()
        {
            ["dotnet --list-sdks"] = (0, "9.0.314 [x]\n10.0.402 [x]\n"),
            ["docker info"] = (0, "29.5.3\n"),
            ["docker image"] = (0, "sha256:5b0916c7af8c\n"),
        }));

        Assert.All(checks, c => Assert.Null(c.Remedy));
        Assert.Equal(["sdk=10.0.402", "tool=published", "substrate=docker 29.5.3", "image=present"], checks.Select(c => c.Item + "=" + c.Found));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("10.0.415", true)]              // a later patch in the band: rollForward latestPatch
    [InlineData("10.0.400", false)]             // below the pin
    [InlineData("10.0.500", false)]             // the next feature band
    [InlineData("10.0.402-rc.1.25451.1", false)]  // a preview is not the band's release
    [InlineData("9.0.314", false)]
    public void The_sdk_is_found_only_in_the_band_global_json_names(string installed, bool found)
    {
        File.WriteAllText(Path.Combine(machine, "global.json"), """{ "sdk": { "version": "10.0.401" } }""");

        var sdk = Doctor.Examine(machine, machine, Answers(new() { ["dotnet --list-sdks"] = (0, installed + " [x]\n") }))[0];

        Assert.Equal(found, sdk.Remedy is null);
        Assert.Contains(found ? installed : "10.0.4xx", sdk.Found, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Without_Docker_LocalDB_is_the_substrate_and_no_image_is_needed()
    {
        var checks = Doctor.Examine(machine, machine, Answers(new() { ["docker info"] = (1, "Cannot connect to the Docker daemon"), ["sqllocaldb info"] = (0, "MSSQLLocalDB\n") }));

        Assert.Equal(("localdb, CDC not provable here", null), (checks[2].Found, checks[2].Remedy));
        Assert.Null(checks[3].Remedy);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Docker_without_the_pinned_image_names_its_pull()
    {
        var image = Doctor.Examine(machine, machine, Answers(new() { ["docker info"] = (0, "29.5.3\n"), ["docker image"] = (1, "No such image") }))[3];

        Assert.Equal("absent", image.Found);
        Assert.Contains("ci/sql.sh up", image.Remedy, StringComparison.Ordinal);
        Assert.Contains(Doctor.SqlServerImage, image.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_sql_scripts_pin_the_image_the_doctor_checks()
    {
        Assert.Matches("^mcr.microsoft.com/mssql/server:2022-latest@sha256:[0-9a-f]{64}$", Doctor.SqlServerImage);
        Assert.All(["sql.sh", "sql.ps1"], script => Assert.Contains(Doctor.SqlServerImage, File.ReadAllText(Path.Combine(Repository.Root, "ci", script)), StringComparison.Ordinal));
    }

    /// <summary>The files a published tool folder holds beside estate: the SqlTasks targets and the reference stub.</summary>
    private void Publish()
    {
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(machine, file))!);
            File.WriteAllText(Path.Combine(machine, file), "");
        }
    }

    /// <summary>A machine that answers a program and its first argument as given, and has nothing else installed.</summary>
    private static Doctor.Command Answers(Dictionary<string, (int Exit, string Output)> answers) =>
        (file, arguments) => answers.TryGetValue(file + " " + arguments[0], out var answer) ? answer : null;
}
