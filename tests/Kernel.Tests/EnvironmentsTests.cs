using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Estate.Tests;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>
/// estate/posture.json as data (WP 1.5, §4 row 14): the environments it names, each once and each with the host its server runs on, a
/// publish profile's path inside the estate, and the scratch server it prefers; a reference is env:NAME or file:path and prints as
/// itself, and no connection string passes as a path; an environment is real until a named lead's dated confirmation says synthetic;
/// and a SQLCMD value is a literal or a reference, read through Match, and a name shaped like a credential never holds a literal.
/// No error quotes the value it rejected.
/// </summary>
public sealed class EnvironmentsTests
{
    private const string Where = "environments.dev in estate/posture.json";

    private const string Pipeline = "estate/profiles/pipeline.publish.xml";

    private static readonly PlantedValue Planted = PlantedValue.Password;

    /// <summary>A value no message may carry: a password-like token that no word of a message spells.</summary>
    private static readonly Gen<string> Secret = Gen.Char["abcdefXYZ0123456789!#%"].Array[8, 24].Select(cs => "Pa5$" + new string(cs));

    [Fact]
    [Trait("Category", "fast")]
    public void A_reference_is_env_and_a_variable_or_file_and_a_path_and_prints_as_itself()
    {
        var variable = Expect.Value(SecretReference.Of(Where, "env:ESTATE_DEV"));
        var file = Expect.Value(SecretReference.Of(Where, "file:.estate/principals/dev.connection"));

        Assert.Equal(("env:ESTATE_DEV", "file:.estate/principals/dev.connection"), (variable.ToString(), file.ToString()));
        Assert.Equal("variable ESTATE_DEV", variable.Match(name => "variable " + name, path => "file " + path));
        Assert.Equal("file .estate/principals/dev.connection", file.Match(name => "variable " + name, path => "file " + path));
        Assert.Equal(variable, Expect.Value(SecretReference.Of("elsewhere", "env:ESTATE_DEV")));
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X1")]
    public void Anything_else_given_as_a_reference_is_refused_without_being_quoted()
    {
        Secret.Sample(secret =>
        {
            foreach (var text in (string[])[secret, "Server=db;User ID=sa;Password=" + secret, "file:Server=db;User ID=sa;Password=" + secret, "file:Password=" + secret,
                "file:" + secret + ";x", "env:" + secret, "ENV:ESTATE_DEV", "env:1" + secret, "file: " + secret, "file:"])
            {
                var error = Expect.Failed(SecretReference.Of(Where, text), "reference.malformed");
                new PlantedValue(secret).AbsentFrom(error);
                Assert.StartsWith(Where, error.Message, StringComparison.Ordinal);
            }
        });
        Expect.Failed(SecretReference.Of(Where, null), "reference.malformed");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_environment_is_real_until_a_named_lead_s_dated_confirmation_says_synthetic()
    {
        var confirmed = Expect.Value(Confirmation.Of(Where, "the dev lead", "2026-09-20"));

        Assert.Equal(("the dev lead", new DateOnly(2026, 9, 20)), (confirmed.Lead, confirmed.On));
        Assert.Equal(new Classification.Real(null), Expect.Value(Classification.Of(Where, null, null)));
        Assert.Equal(new Classification.Real(null), Expect.Value(Classification.Of(Where, "real", null)));
        Assert.Equal(new Classification.Real(confirmed), Expect.Value(Classification.Of(Where, "real", confirmed)));
        Assert.Equal(new Classification.Synthetic(confirmed), Expect.Value(Classification.Of(Where, "synthetic", confirmed)));
        Assert.Equal(("real", "real by the dev lead", "synthetic by the dev lead"), (Classified(Classification.Of(Where, null, null)),
            Classified(Classification.Of(Where, "real", confirmed)), Classified(Classification.Of(Where, "synthetic", confirmed))));
        Expect.Failed(Classification.Of(Where, "synthetic", null), "posture.unconfirmed");
        Expect.Failed(Classification.Of(Where, "Synthetic", confirmed), "posture.classification");
        Planted.AbsentFrom(Expect.Failed(Classification.Of(Where, Planted.Text, confirmed), "posture.classification"));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("the dev lead", "2026-02-30")]
    [InlineData("the dev lead", "20/09/2026")]
    [InlineData("the dev lead", "2026-09-20T00:00:00Z")]
    [InlineData("the dev lead", " 2026-09-20")]
    [InlineData("the dev lead", null)]
    [InlineData(" ", "2026-09-20")]
    [InlineData(null, "2026-09-20")]
    [InlineData("the dev\nlead", "2026-09-20")]
    public void A_confirmation_is_a_named_lead_and_a_calendar_date_as_written(string? lead, string? on) =>
        Expect.Failed(Confirmation.Of(Where, lead, on), "posture.confirmation");

    /// <summary>The date is data, as the lead committed it: a date after today is taken as written, since nothing here reads a clock.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_confirmation_dated_after_today_is_taken_as_written() =>
        Assert.Equal(new DateOnly(2999, 1, 1), Expect.Value(Confirmation.Of(Where, "the dev lead", "2999-01-01")).On);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("ServiceAccountPassword")]
    [InlineData("DbPwd")]
    [InlineData("CLIENT_SECRET")]
    [InlineData("AccessToken")]
    [InlineData("ApiKey")]
    [InlineData("passwd")]
    [InlineData("StorageCredential")]
    [Trait("Value", "X1")]
    public void A_SQLCMD_name_shaped_like_a_credential_never_holds_a_literal_and_takes_a_reference(string name)
    {
        var error = Expect.Failed(SqlCmdVariable.Of(Where, name, Planted.Text), "sqlcmd.literal-credential");
        var referenced = Expect.Value(SqlCmdVariable.Of(Where, name, Expect.Value(SecretReference.Of(Where, "env:ESTATE_SECRET"))));

        Planted.AbsentFrom(error);
        Assert.Equal((name, "from env:ESTATE_SECRET"), (referenced.Name.ToString(), Held(referenced)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_SQLCMD_literal_under_any_other_name_is_kept_and_a_name_sqlcmd_cannot_use_is_rejected_unquoted()
    {
        var literal = Expect.Value(SqlCmdVariable.Of(Where, "EnvironmentTag", "dev"));

        Assert.Equal(("EnvironmentTag", "the literal dev"), (literal.Name.ToString(), Held(literal)));
        foreach (var name in (string[])["", "Environment Tag", "1Tag", "Tag)", "Tag=" + Planted])
        {
            Planted.AbsentFrom(Expect.Failed(SqlCmdVariable.Of(Where, name, "dev"), "sqlcmd.name"));
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_named_environment_sorts_its_reader_groups_and_its_SQLCMD_variables()
    {
        var environment = Expect.Value(Environment("dev", readerGroups: ["leads", "developers"], sqlCmd: [Literal("Tag", "dev"), Referenced("ServicePassword", "env:ESTATE_PW")]));

        Assert.Equal(["developers", "leads"], environment.ReaderGroups);
        Assert.Equal(["ServicePassword", "Tag"], environment.SqlCmd.Select(v => v.Name.ToString()));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Dev")]
    [InlineData("dev qa")]
    [InlineData("-dev")]
    [InlineData("dev\n")]
    [InlineData("a-name-longer-than-thirty-two-chars")]
    [InlineData("")]
    public void An_environment_s_name_env_cannot_carry_is_refused(string name) => Expect.Failed(EnvironmentName.Of(Where, name), "posture.environment-name");

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("/estate/profiles/pipeline.publish.xml")]
    [InlineData("C:/estate/pipeline.publish.xml")]
    [InlineData("estate\\profiles\\pipeline.publish.xml")]
    [InlineData("../elsewhere/pipeline.publish.xml")]
    [InlineData("estate//pipeline.publish.xml")]
    [InlineData("estate/profiles/pipeline.xml")]
    [InlineData("")]
    [InlineData(null)]
    public void A_profile_path_outside_the_estate_or_naming_no_publish_profile_is_refused(string? path) => Expect.Failed(PublishProfilePath.Of(Where, path), "posture.profile-path");

    /// <summary>
    /// The duplicate check compares neighbours in the sorted variables, so it relies on the order putting two spellings of one name side by
    /// side: Tag, Version and tag sort Tag, tag, Version under the kernel's case-insensitive order, and Tag, Version, tag under an ordinal one.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_named_environment_rejects_a_blank_or_repeated_reader_group_and_a_SQLCMD_variable_given_twice_in_any_case()
    {
        Expect.Failed(Environment("dev", readerGroups: ["leads", "leads"]), "posture.reader-groups");
        Expect.Failed(Environment("dev", readerGroups: ["leads", " "]), "posture.reader-groups");
        Expect.Failed(Environment("dev", sqlCmd: [Literal("Tag", "a"), Literal("Version", "b"), Literal("tag", "c")]), "posture.sqlcmd-repeated");
    }

    /// <summary>The posture names each environment once; one environment is found by its name, and another name finds none.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_posture_names_each_environment_once_and_finds_one_by_its_name()
    {
        var (dev, qa) = (Expect.Value(Environment("dev")), Expect.Value(Environment("qa")));
        var environments = Expect.Value(Environments.Of("estate/posture.json", [qa, dev], null));

        Assert.Equal([dev, qa], environments.All);
        Assert.Same(qa, environments.Named(Expect.Value(EnvironmentName.Of(Where, "qa"))));
        Assert.Null(environments.Named(Expect.Value(EnvironmentName.Of(Where, "uat"))));
        Expect.Failed(Environments.Of("estate/posture.json", [dev, qa, Expect.Value(Environment("dev", profile: "estate/other.publish.xml"))], null), "posture.environment-name");
    }

    /// <summary>The profile a copy is planned under when no --profile names one: the one every environment names, else none (cli/Check.cs).</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_shared_profile_is_the_one_path_every_environment_names_and_else_none()
    {
        var shared = Expect.Value(Environments.Of("estate/posture.json", [Expect.Value(Environment("dev")), Expect.Value(Environment("qa"))], null)).SharedProfile;
        var differing = Expect.Value(Environments.Of("estate/posture.json", [Expect.Value(Environment("dev")), Expect.Value(Environment("qa", profile: "estate/qa.publish.xml"))], null)).SharedProfile;
        var none = Expect.Value(Environments.Of("estate/posture.json", [], null)).SharedProfile;

        Assert.Equal((Pipeline, null, null), (shared?.ToString(), differing?.ToString(), none?.ToString()));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("docker", "docker")]
    [InlineData("localdb", "localdb")]
    [InlineData("Docker", null)]
    [InlineData("podman", null)]
    [InlineData(null, null)]
    public void The_scratch_server_the_posture_prefers_is_docker_or_localdb(string? text, string? kind) =>
        Assert.Equal(kind ?? "posture.malformed", ScratchServerKind.Of("scratchServer in estate/posture.json", text).Match(k => k.ToString(), error => error.Code));

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    public void Nothing_a_named_environment_prints_carries_a_literal_or_what_a_reference_names()
    {
        var confirmed = Expect.Value(Confirmation.Of(Where, "the dev lead", "2026-09-20"));
        var environment = Expect.Value(NamedEnvironment.Of(Where, Expect.Value(EnvironmentName.Of(Where, "dev")), Expect.Value(Host.Of(Where, "dev-sql")), new Classification.Synthetic(confirmed), ["leads"],
            Reference("env:ESTATE_DEV"), Expect.Value(PublishProfilePath.Of(Where, Pipeline)), [Literal("Tag", Planted.Text), Referenced("ServicePassword", "file:.estate/dev.password")], null));

        var printed = string.Join("\n", environment.ToString(), string.Join(" ", environment.SqlCmd), environment.Classification, environment.Connection);

        Assert.Equal("env:dev (synthetic, confirmed by the dev lead on 2026-09-20)", environment.ToString());
        Assert.Equal("$(ServicePassword) from file:.estate/dev.password $(Tag), a literal", string.Join(" ", environment.SqlCmd));
        Planted.AbsentFrom(printed);
    }

    private static Result<NamedEnvironment> Environment(string name, string profile = Pipeline, IEnumerable<string>? readerGroups = null, IEnumerable<SqlCmdVariable>? sqlCmd = null) =>
        NamedEnvironment.Of(Where, Expect.Value(EnvironmentName.Of(Where, name)), Expect.Value(Host.Of(Where, "dev-sql.corp.example")), new Classification.Real(null), readerGroups ?? [],
            Reference("env:ESTATE_DEV"), Expect.Value(PublishProfilePath.Of(Where, profile)), sqlCmd ?? [], Reference("file:.estate/dev-ossys.connection"));

    private static SecretReference Reference(string text) => Expect.Value(SecretReference.Of(Where, text));

    private static SqlCmdVariable Literal(string name, string value) => Expect.Value(SqlCmdVariable.Of(Where, name, value));

    private static SqlCmdVariable Referenced(string name, string reference) => Expect.Value(SqlCmdVariable.Of(Where, name, Reference(reference)));

    /// <summary>What a SQLCMD variable holds, read through its Match: the literal's text, or the reference it is read from.</summary>
    private static string Held(SqlCmdVariable variable) => variable.Match(text => "the literal " + text, reference => "from " + reference);

    /// <summary>A classification read through its Match: real or synthetic, and by whom where a lead confirmed it.</summary>
    private static string Classified(Result<Classification> classification) =>
        Expect.Value(classification).Match(real => "real" + (real.Confirmation is { } by ? " by " + by.Lead : ""), synthetic => "synthetic by " + synthetic.Confirmation.Lead);
}
