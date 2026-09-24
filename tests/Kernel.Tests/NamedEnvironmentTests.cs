using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>
/// A named environment is data (WP 1.5): a reference is env:NAME or file:path and prints as itself; an environment is real
/// until a named lead's dated confirmation says synthetic; a SQLCMD name shaped like a credential never holds a literal; and
/// substitution is a pure function whose text exists only in the string it returns. No refusal quotes the value it refused.
/// </summary>
public sealed class NamedEnvironmentTests
{
    private const string Where = "environments.dev in estate/posture.json";

    private const string Planted = "Pa55!planted#7f3a";

    private const string Pipeline = "estate/profiles/pipeline.publish.xml";

    /// <summary>A value no message may carry: a password-like token that no word of a message spells.</summary>
    private static readonly Gen<string> Secret = Gen.Char["abcdefXYZ0123456789!#%"].Array[8, 24].Select(cs => "Pa5$" + new string(cs));

    [Fact]
    [Trait("Category", "fast")]
    public void A_reference_is_env_and_a_variable_or_file_and_a_path_and_prints_as_itself()
    {
        var variable = Made(SecretReference.Of(Where, "env:ESTATE_DEV"));
        var file = Made(SecretReference.Of(Where, "file:.estate/principals/dev.connection"));

        Assert.Equal(("env:ESTATE_DEV", "file:.estate/principals/dev.connection"), (variable.ToString(), file.ToString()));
        Assert.Equal("variable ESTATE_DEV", variable.Match(name => "variable " + name, path => "file " + path));
        Assert.Equal("file .estate/principals/dev.connection", file.Match(name => "variable " + name, path => "file " + path));
        Assert.Equal(variable, Made(SecretReference.Of("elsewhere", "env:ESTATE_DEV")));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Anything_else_given_as_a_reference_is_refused_without_being_quoted()
    {
        Secret.Sample(secret =>
        {
            foreach (var text in (string[])[secret, "Server=db;User ID=sa;Password=" + secret, "env:" + secret, "ENV:ESTATE_DEV", "env:1" + secret, "file: " + secret, "file:"])
            {
                var refusal = Refused(SecretReference.Of(Where, text));
                Assert.Equal("reference.malformed", refusal.Code);
                Assert.DoesNotContain(secret, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
                Assert.StartsWith(Where, refusal.Message, StringComparison.Ordinal);
            }
        });
        Assert.Equal("reference.malformed", Refused(SecretReference.Of(Where, null)).Code);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_environment_is_real_until_a_named_leads_dated_confirmation_says_synthetic()
    {
        var confirmed = Made(Confirmation.Of(Where, "the dev lead", "2026-09-20"));

        Assert.Equal(("the dev lead", new DateOnly(2026, 9, 20)), (confirmed.Lead, confirmed.On));
        Assert.Equal(new Classification.Real(null), Made(Classification.Of(Where, null, null)));
        Assert.Equal(new Classification.Real(null), Made(Classification.Of(Where, "real", null)));
        Assert.Equal(new Classification.Real(confirmed), Made(Classification.Of(Where, "real", confirmed)));
        Assert.Equal(new Classification.Synthetic(confirmed), Made(Classification.Of(Where, "synthetic", confirmed)));
        Assert.Equal("posture.unconfirmed", Refused(Classification.Of(Where, "synthetic", null)).Code);
        Assert.Equal("posture.classification", Refused(Classification.Of(Where, "Synthetic", confirmed)).Code);
        Assert.Equal("posture.classification", Refused(Classification.Of(Where, Planted, confirmed)).Code);
        Assert.DoesNotContain(Planted, Refused(Classification.Of(Where, Planted, confirmed)).Message, StringComparison.Ordinal);
    }

    /// <summary>The date is data, as the lead committed it: a date after today is taken as written, since nothing here reads a clock.</summary>
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
    public void A_confirmation_is_a_named_lead_and_a_calendar_date_as_written(string? lead, string? on)
    {
        Assert.Equal("posture.confirmation", Refused(Confirmation.Of(Where, lead, on)).Code);
        Assert.Equal(new DateOnly(2999, 1, 1), Made(Confirmation.Of(Where, "the dev lead", "2999-01-01")).On);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("ServiceAccountPassword")]
    [InlineData("DbPwd")]
    [InlineData("CLIENT_SECRET")]
    [InlineData("AccessToken")]
    [InlineData("ApiKey")]
    [InlineData("passwd")]
    [InlineData("StorageCredential")]
    public void A_SQLCMD_name_shaped_like_a_credential_never_holds_a_literal_and_takes_a_reference(string name)
    {
        var refusal = Refused(SqlCmdVariable.Of(Where, name, Planted));
        var referenced = Made(SqlCmdVariable.Of(Where, name, Made(SecretReference.Of(Where, "env:ESTATE_SECRET"))));

        Assert.Equal("sqlcmd.literal-credential", refusal.Code);
        Assert.DoesNotContain(Planted, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
        Assert.Equal((name, (string?)null, "env:ESTATE_SECRET"), (referenced.Name, referenced.Literal, referenced.Reference?.ToString()));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_SQLCMD_literal_under_any_other_name_is_kept_and_a_name_sqlcmd_cannot_use_is_refused_unquoted()
    {
        var literal = Made(SqlCmdVariable.Of(Where, "EnvironmentTag", "dev"));

        Assert.Equal(("EnvironmentTag", (string?)"dev", (SecretReference?)null), (literal.Name, literal.Literal, literal.Reference));
        foreach (var name in (string[])["", "Environment Tag", "1Tag", "Tag)", "Tag=" + Planted])
        {
            var refusal = Refused(SqlCmdVariable.Of(Where, name, "dev"));
            Assert.Equal("sqlcmd.name", refusal.Code);
            Assert.DoesNotContain(Planted, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_named_environment_holds_what_the_posture_gives_it_in_order()
    {
        var environment = Made(Environment("dev", Pipeline, cohorts: ["leads", "developers"], sqlCmd: [Literal("Tag", "dev"), Referenced("ServicePassword", "env:ESTATE_PW")]));

        Assert.Equal("dev", environment.Name);
        Assert.Equal(["developers", "leads"], environment.Cohorts);
        Assert.Equal(["ServicePassword", "Tag"], environment.SqlCmd.Select(v => v.Name));
        Assert.Equal(("env:ESTATE_DEV", Pipeline, (string?)"file:.estate/dev-ossys.connection"), (environment.Connection.ToString(), environment.ProfilePath, environment.Metamodel?.ToString()));
        Assert.Equal(new Classification.Real(null), environment.Classification);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Dev", Pipeline, "posture.environment-name")]
    [InlineData("dev qa", Pipeline, "posture.environment-name")]
    [InlineData("-dev", Pipeline, "posture.environment-name")]
    [InlineData("dev\n", Pipeline, "posture.environment-name")]
    [InlineData("a-name-longer-than-thirty-two-chars", Pipeline, "posture.environment-name")]
    [InlineData("dev", "/estate/profiles/pipeline.publish.xml", "posture.profile-path")]
    [InlineData("dev", "C:/estate/pipeline.publish.xml", "posture.profile-path")]
    [InlineData("dev", "estate\\profiles\\pipeline.publish.xml", "posture.profile-path")]
    [InlineData("dev", "../elsewhere/pipeline.publish.xml", "posture.profile-path")]
    [InlineData("dev", "estate//pipeline.publish.xml", "posture.profile-path")]
    [InlineData("dev", "estate/profiles/pipeline.xml", "posture.profile-path")]
    [InlineData("dev", "", "posture.profile-path")]
    public void A_named_environment_refuses_a_name_env_cannot_carry_and_a_profile_path_outside_the_estate(string name, string profile, string code) =>
        Assert.Equal(code, Refused(Environment(name, profile)).Code);

    [Fact]
    [Trait("Category", "fast")]
    public void A_named_environment_refuses_a_blank_or_repeated_cohort_and_a_SQLCMD_variable_given_twice_in_any_case()
    {
        Assert.Equal("posture.cohort", Refused(Environment("dev", Pipeline, cohorts: ["leads", "leads"])).Code);
        Assert.Equal("posture.cohort", Refused(Environment("dev", Pipeline, cohorts: ["leads", " "])).Code);
        Assert.Equal("posture.sqlcmd-repeated", Refused(Environment("dev", Pipeline, sqlCmd: [Literal("Tag", "a"), Literal("tag", "b")])).Code);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Substitution_replaces_each_variable_as_sqlcmd_does_ignoring_case_and_never_twice()
    {
        const string script = "PRINT N'$(EnvironmentTag)'; -- $(environmenttag)\nALTER USER [$(ServiceUser)] WITH DEFAULT_SCHEMA = dbo;";
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["EnvironmentTag"] = "dev", ["ServiceUser"] = "svc$(EnvironmentTag)" };

        Assert.Equal("PRINT N'dev'; -- dev\nALTER USER [svc$(EnvironmentTag)] WITH DEFAULT_SCHEMA = dbo;", Made(SqlCmdVariable.Substitute(script, values)));
        Assert.Equal("no variables here", Made(SqlCmdVariable.Substitute("no variables here", new Dictionary<string, string>())));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Substitution_refuses_a_variable_with_no_value_by_its_name_and_quotes_no_value()
    {
        var refusal = Refused(SqlCmdVariable.Substitute("PRINT '$(Tag)'; PRINT '$(Missing)';", new Dictionary<string, string> { ["Tag"] = Planted }));

        Assert.Equal("sqlcmd.undefined", refusal.Code);
        Assert.Contains("$(Missing)", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Nothing_a_named_environment_prints_carries_a_literal_or_what_a_reference_names()
    {
        var confirmed = Made(Confirmation.Of(Where, "the dev lead", "2026-09-20"));
        var environment = Made(NamedEnvironment.Of(Where, "dev", new Classification.Synthetic(confirmed), ["leads"], Reference("env:ESTATE_DEV"), Pipeline,
            [Literal("Tag", Planted), Referenced("ServicePassword", "file:.estate/dev.password")], null));

        var printed = string.Join("\n", environment.ToString(), string.Join(" ", environment.SqlCmd), environment.Classification, environment.Connection);

        Assert.Equal("env:dev (synthetic, confirmed by the dev lead on 2026-09-20)", environment.ToString());
        Assert.Equal("$(ServicePassword) from file:.estate/dev.password $(Tag), a literal", string.Join(" ", environment.SqlCmd));
        Assert.DoesNotContain(Planted, printed, StringComparison.Ordinal);
    }

    private static Result<NamedEnvironment> Environment(string name, string profile, IEnumerable<string>? cohorts = null, IEnumerable<SqlCmdVariable>? sqlCmd = null) =>
        NamedEnvironment.Of(Where, name, new Classification.Real(null), cohorts ?? [], Reference("env:ESTATE_DEV"), profile, sqlCmd ?? [], Reference("file:.estate/dev-ossys.connection"));

    private static SecretReference Reference(string text) => Made(SecretReference.Of(Where, text));

    private static SqlCmdVariable Literal(string name, string value) => Made(SqlCmdVariable.Of(Where, name, value));

    private static SqlCmdVariable Referenced(string name, string reference) => Made(SqlCmdVariable.Of(Where, name, Reference(reference)));

    private static T Made<T>(Result<T> result) => result.Match(value => value, refusal => throw new Xunit.Sdk.XunitException(refusal.Code + ": " + refusal.Message));

    private static Refusal Refused<T>(Result<T> result) => Assert.IsType<Result<T>.Refused>(result).Refusal;
}
