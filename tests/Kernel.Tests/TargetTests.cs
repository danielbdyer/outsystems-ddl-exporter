using System;
using System.Linq;
using CsCheck;
using Estate.Tests;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>
/// The target grammar (V3_MILESTONES.md WP 1.4): env:, copy:, synthetic-copy, ref: and dacpac:, each read into its case of a closed
/// type and written back as it was read. An environment's name and a copy's name each have one grammar, the copy's the ruled
/// estate_&lt;host&gt;_&lt;pid&gt;_&lt;hex&gt; (DECISIONS.md, 2026-09-25), and a text either grammar refuses is refused without being quoted.
/// </summary>
public sealed class TargetTests
{
    private const string Subject = "--target";

    /// <summary>A text an environment's name may or may not be: lowercase and uppercase letters, digits, a hyphen and an underscore, up to 34 characters.</summary>
    private static readonly Gen<string> NameLike = Gen.Char["abcxyzABC019-_"].Array[0, 34].Select(cs => new string(cs));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env:dev", "environment", "dev")]
    [InlineData("env:uat-2", "environment", "uat-2")]
    [InlineData("copy:estate_danny_pc_4242_0a1b2c3d", "registered copy", "estate_danny_pc_4242_0a1b2c3d")]
    [InlineData("synthetic-copy", "synthetic copy", "")]
    [InlineData("ref:main", "git ref", "main")]
    [InlineData("ref:origin/release/2026.09", "git ref", "origin/release/2026.09")]
    [InlineData("dacpac:.estate/build/0a1b/SampleCatalog.dacpac", "package", ".estate/build/0a1b/SampleCatalog.dacpac")]
    public void The_target_grammar_reads_each_form_into_its_case_and_writes_it_back(string text, string form, string named)
    {
        var target = Expect.Value(Target.Parse(text, Subject));

        Assert.Equal((form, named), target.Match(
            environment => ("environment", environment.Name.ToString()), copy => ("registered copy", copy.Name.ToString()), () => ("synthetic copy", ""),
            reference => ("git ref", reference.Ref.ToString()), package => ("package", package.Path)));
        Assert.Equal(text, target.ToString());
        Assert.Equal(target, Expect.Value(Target.Parse(target.ToString(), Subject)));
    }

    /// <summary>An env: target is read exactly when its name is an environment's name, the one grammar the environments file's keys also answer to.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_env_target_is_read_exactly_when_its_name_is_an_environment_s_name() =>
        NameLike.Sample(name => (Target.Parse("env:" + name, Subject) is Result<Target>.Ok) == (EnvironmentName.Of("environments." + name, name) is Result<EnvironmentName>.Ok),
            print: name => "env:" + name);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("sql:dev")]
    [InlineData("dev")]
    [InlineData("env:")]
    [InlineData("env:DEV")]
    [InlineData("env:ESTATE_DEV")]
    [InlineData("env:a-name-longer-than-thirty-two-chars")]
    [InlineData("synthetic-copy:dev")]
    [InlineData("ref:")]
    [InlineData("ref:-n")]
    [InlineData("ref:main\u0000")]
    [InlineData("dacpac:")]
    [InlineData("dacpac:x\n.dacpac")]
    [InlineData("")]
    public void A_text_in_no_form_of_the_grammar_is_target_unknown(string text) =>
        Assert.StartsWith("The value of " + Subject + " is none of", Expect.Failed(Target.Parse(text, Subject), "target.unknown").Message, StringComparison.Ordinal);

    /// <summary>An argument can hold anything a caller pastes, so no refusal of the grammar quotes it.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("sql:")]
    [InlineData("env:")]
    [InlineData("env:-")]
    [InlineData("copy:")]
    [InlineData("copy:estate_")]
    [InlineData("ref:-")]
    [InlineData("dacpac:\n")]
    public void No_refusal_of_the_grammar_quotes_the_argument(string form)
    {
        var planted = new PlantedValue("Planted-7f3a");

        planted.AbsentFrom(Expect.Failed(Target.Parse(form + planted, Subject)));
    }

    /// <summary>The names estate gives copies read back as themselves, whatever the machine is called, the process's id and the eight hexadecimal digits.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_copy_name_estate_makes_reads_back_as_itself() =>
        Gen.Select(Gen.String, Gen.Int[0, int.MaxValue], Gen.UInt).Sample((machine, pid, suffix) =>
            CopyName.Make(machine, pid, suffix) is var made && CopyName.Of(Subject, made.ToString()) is Result<CopyName>.Ok(var read)
            && read == made && read.Pid == pid && read.Machine == CopyName.Make(machine, 0, 0).Machine);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("DANNY-PC", 4242, 0x0a1b2c3du, "estate_danny_pc_4242_0a1b2c3d")]
    [InlineData("runner.corp.example", 7, 0xffffffffu, "estate_runner_corp_example_7_ffffffff")]
    [InlineData("ÉTÉ", 1, 0u, "estate__t__1_00000000")]
    [InlineData("", 0, 1u, "estate___0_00000001")]
    [InlineData("a-machine-name-longer-than-forty-characters-in-all", 2, 2u, "estate_a_machine_name_longer_than_forty_charact_2_00000002")]
    public void A_copy_is_named_for_its_machine_in_lower_case_and_its_process(string machine, int pid, uint suffix, string name) =>
        Assert.Equal(name, CopyName.Make(machine, pid, suffix).ToString());

    /// <summary>A name the loose grammar [a-z0-9_]{1,128} of io/SqlServer.cs accepted before the ruling of 2026-09-25, which estate never gives a copy.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("estate_host")]
    [InlineData("estatehost_1_0a1b2c3d")]
    [InlineData("Estate_host_1_0a1b2c3d")]
    [InlineData("estate_host_1_0a1b2c3")]
    [InlineData("estate-host_1_0a1b2c3d")]
    [InlineData("estate_host_01_0a1b2c3d")]
    [InlineData("estate_host_2147483648_0a1b2c3d")]
    [InlineData("estate_host_1_0A1B2C3D")]
    [InlineData("orders")]
    [InlineData("")]
    public void A_name_estate_never_gives_a_copy_is_copy_unregistered_and_is_not_quoted(string text)
    {
        var error = Expect.Failed(CopyName.Of(Subject, text), "copy.unregistered");

        Assert.StartsWith(Subject + " names a copy", error.Message, StringComparison.Ordinal);
        Assert.Contains(".estate/copies.json", error.Message, StringComparison.Ordinal);
        Assert.All(new[] { text }.Where(t => t.Length > 0), t => Assert.DoesNotContain(t, error.Message + error.Remedy, StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("-n")]
    [InlineData("--output=x")]
    [InlineData("main\u0000")]
    [InlineData("")]
    [InlineData(null)]
    public void A_git_ref_git_would_read_as_an_option_or_that_holds_a_control_character_is_refused(string? text) => Expect.Failed(GitRef.Of("--at", text), "ref.malformed");

    [Fact]
    [Trait("Category", "fast")]
    public void A_default_name_ref_or_copy_name_is_none_of_them()
    {
        Assert.Throws<InvalidOperationException>(() => default(EnvironmentName).ToString());
        Assert.Throws<InvalidOperationException>(() => default(CopyName).ToString());
        Assert.Throws<InvalidOperationException>(() => default(GitRef).ToString());
    }
}
