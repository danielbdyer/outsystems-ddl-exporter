using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Kernel;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// estate read and estate diff (WP 1.7, V3_ARCHITECTURE.md §8.1 and §8.5) against a git repository holding the golden project at Base and
/// the make-mandatory archetype at Head: M1 exit 2, and each verb's JSON against its schema under cli/schemas/.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class DiffTests(ScratchEstate estate) : IClassFixture<ScratchEstate>
{
    /// <summary>M1 exit 2: the diff of the make-mandatory archetype prints Customer.Email's Nullable true → false, and nothing else.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Diff_from_the_base_ref_to_the_make_mandatory_head_prints_Customer_Email_s_Nullable_true_to_false_and_nothing_else()
    {
        var (exit, output) = estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Head);

        Assert.Equal(0, exit);
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false\n", output);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Diff_json_validates_against_estate_diff_1_and_carries_the_one_property_both_fingerprints_and_the_engine()
    {
        var (exit, output) = estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Head, "--json");

        var answer = JsonNode.Parse(output)!;
        ScratchEstate.Valid("estate.diff.1.schema.json", answer);
        Assert.Equal(0, exit);
        var changed = Assert.Single(answer["diff"]!["change"]!["changed"]!.AsArray())!;
        Assert.Equal("Column [dbo].[Customer].[Email]", (string?)changed["key"]);
        var property = Assert.Single(changed["properties"]!.AsArray())!;
        Assert.Equal(("Nullable", true, false), ((string?)property["name"], (bool)property["before"]!, (bool)property["after"]!));
        Assert.NotEqual((string?)answer["diff"]!["from"]!["fingerprint"], (string?)answer["diff"]!["to"]!["fingerprint"]);
        Assert.Equal(Doctor.DacFx, (string?)answer["engine"]!["dacfx"]);
        Assert.Null(answer["receipt"]);
    }

    /// <summary>--fail-on-change makes a change exit 5 (the drift check in CI); an unchanged pair still exits 0 and says so.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Diff_with_fail_on_change_exits_5_on_a_change_and_0_on_none()
    {
        Assert.Equal(5, estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Head, "--fail-on-change").Exit);

        var (exit, output) = estate.Estate("diff", "--from", "ref:" + estate.Base, "--to", "ref:" + estate.Base, "--fail-on-change");

        Assert.Equal(0, exit);
        Assert.Equal("No change from ref:" + estate.Base + " to ref:" + estate.Base + ".\n", output);
    }

    /// <summary>A ref and the package its build wrote read to one fingerprint, the walk's; read's JSON validates against estate.read/1.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Read_of_a_ref_and_of_the_package_its_build_wrote_fingerprint_alike_and_validate_against_estate_read_1()
    {
        var (exit, output) = estate.Estate("read", "--from", "ref:" + estate.Base, "--json");
        var dacpac = Path.Combine(estate.Root, ".estate", "build", estate.Base, "SampleCatalog.dacpac");
        var (packageExit, package) = estate.Estate("read", "--from", "dacpac:" + dacpac, "--json");

        var (fromRef, fromPackage) = (JsonNode.Parse(output)!, JsonNode.Parse(package)!);
        ScratchEstate.Valid("estate.read.1.schema.json", fromRef);
        ScratchEstate.Valid("estate.read.1.schema.json", fromPackage);
        Assert.Equal((0, 0), (exit, packageExit));
        using var loaded = GitTests.Ok(Ssdt.Load(dacpac));
        var walked = GitTests.Ok(Ssdt.Walk(loaded)).Elements;
        Assert.Equal("sha256:" + Fingerprint.Of(walked), (string?)fromRef["read"]!["fingerprint"]);
        Assert.Equal((string?)fromRef["read"]!["fingerprint"], (string?)fromPackage["read"]!["fingerprint"]);
        Assert.Equal(walked.Count, fromRef["read"]!["elements"]!.AsArray().Count);
        var email = fromRef["read"]!["elements"]!.AsArray().Single(e => (string?)e!["key"] == "Column [dbo].[Customer].[Email]")!;
        Assert.True((bool)email["properties"]!["Nullable"]!);
        Assert.StartsWith("ref:" + estate.Base + ": " + walked.Count + " elements, fingerprint sha256:", estate.Estate("read", "--from", "ref:" + estate.Base).Output, StringComparison.Ordinal);
    }

    /// <summary>Bad arguments are exit 1 and still validate against the verb's schema, with what it adds null.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("read", "estate.read.1.schema.json", new[] { "--from" })]
    [InlineData("read", "estate.read.1.schema.json", new[] { "--from", "sql:dev" })]
    [InlineData("diff", "estate.diff.1.schema.json", new[] { "--from", "ref:main" })]
    [InlineData("diff", "estate.diff.1.schema.json", new[] { "--from", "ref:main", "--to", "ref:main", "--from", "ref:main" })]
    public void A_verb_given_bad_arguments_exits_1_and_its_answer_validates_against_its_schema(string verb, string schema, string[] arguments)
    {
        var (exit, output) = estate.Estate([verb, .. arguments, "--json"]);

        var answer = JsonNode.Parse(output)!;
        ScratchEstate.Valid(schema, answer);
        Assert.Equal(1, exit);
        Assert.Null(answer[verb]);
    }
}
