using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Estate.Budgets.Tests;
using Estate.Budgets.Tests.Register;
using Estate.Cli;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// io/Profiles (WP 1.5): estate/posture.json read into named environments, and the pipeline's publish profile read into its
/// deploy options and SQLCMD values alone. A refusal of either exits 6 through the CLI's table, names the key or the file, and
/// never quotes the value; Strict is the profile as loaded, Permissive differs from it in BlockOnPossibleDataLoss alone and
/// nothing in io but a Copy makes one, and nothing either prints carries a value a reference names or a literal holds.
/// </summary>
public sealed class ProfilesTests : IDisposable
{
    private const string Planted = "Pa55!planted#7f3a";

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string Golden = Path.Combine(Repository.Root, "tests", "Golden");

    private static readonly string Pipeline = Path.Combine(Golden, "proving-ground", "profiles", "pipeline.publish.xml");

    /// <summary>A password set in any connection string, however spelled or spaced: what no output may carry.</summary>
    internal static readonly Regex PasswordSetting = new(@"(?:password|pwd)\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly string scratch = Directory.CreateTempSubdirectory("estate-profiles-").FullName;

    public ProfilesTests() => Telemetry.OptOut();   // before DacFx loads, as estate's Main does

    public static TheoryData<string> Ways => new(RefusalPaths.All.Select(c => c.Label));

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Fact]
    [Trait("Category", "fast")]
    public void The_sample_posture_reads_into_its_named_environments_each_with_the_pipelines_profile()
    {
        var environments = Made(Profiles.Environments(Golden));
        var dev = environments.Single(e => e.Name == "dev");

        Assert.Equal(["dev", "prod", "qa", "uat"], environments.Select(e => e.Name));
        Assert.Equal("env:dev (synthetic, confirmed by the dev lead on 2026-09-20)", dev.ToString());
        Assert.Equal(["env:prod (real)", "env:qa (real)", "env:uat (real)"], environments.Where(e => e != dev).Select(e => e.ToString()));
        Assert.Equal(["developers", "leads"], dev.Cohorts);
        Assert.Equal(("env:ESTATE_DEV", "proving-ground/profiles/pipeline.publish.xml", (string?)"file:.estate/principals/dev-ossys.connection"), (dev.Connection.ToString(), dev.ProfilePath, dev.Metamodel?.ToString()));
        Assert.Equal("$(EnvironmentTag), a literal | $(ServiceAccountPassword) from env:ESTATE_DEV_SERVICE_PASSWORD", string.Join(" | ", dev.SqlCmd));
        Assert.Equal("dev", dev.SqlCmd[0].Match(text => text, reference => "from " + reference));
        Assert.All(environments, e => Assert.True(Made(Profiles.Of(e, Golden)).Options().BlockOnPossibleDataLoss));
    }

    /// <summary>
    /// VALUES.md X1: a literal connection string in the posture, a SQLCMD value a profile gives, or an argument where a reference goes,
    /// file: before it or not, is exit 6, and so is a password anywhere in a profile, a comment splitting it or not; the refusal names its
    /// place and quotes nothing. A profile's password-free target is removed at load instead, as WP 1.5 has it and DECISIONS.md reads X1:
    /// "a profile naming a target keeps its SQLCMD values and nothing of the target".
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("connection", "posture.literal-connection", "environments.dev.connection")]
    [InlineData("metamodel", "posture.literal-connection", "environments.dev.metamodel")]
    [InlineData("sqlcmd", "posture.literal-connection", "environments.dev.sqlcmd.LinkedServer.literal")]
    [InlineData("cohort", "posture.literal-connection", "environments.dev.cohorts[0]")]
    [InlineData("key", "posture.literal-connection", "environments.dev.sqlcmd.#1")]
    [InlineData("profile", "profile.password", "inline.publish.xml")]
    [InlineData("profile, a comment splitting the password", "profile.password", "inline.publish.xml")]
    [InlineData("profile's SQLCMD value", "profile.password", "inline.publish.xml")]
    [InlineData("profile's SQLCMD value, password-free", "profile.literal-connection", "inline.publish.xml gives $(LinkedServer)")]
    [InlineData("argument", "reference.malformed", "--connection")]
    [InlineData("argument after file:", "reference.malformed", "--connection")]
    [InlineData("target", "connection.literal", "--target")]
    public void Inline_credential_refused(string where, string code, string named)
    {
        const string credential = "Server=db;User ID=estate;Password=" + Planted;
        var refusal = where switch
        {
            "connection" => Refused(Profiles.Environments(Estate(Dev(connection: credential)))),
            "metamodel" => Refused(Profiles.Environments(Estate(Dev("\"metamodel\": \"" + credential + "\"")))),
            "sqlcmd" => Refused(Profiles.Environments(Estate(Dev("\"sqlcmd\": { \"LinkedServer\": { \"literal\": \"" + credential + "\", \"sensitive\": false } }")))),
            "cohort" => Refused(Profiles.Environments(Estate(Dev("\"cohorts\": [\"" + credential + "\"]")))),
            "key" => Refused(Profiles.Environments(Estate(Dev("\"sqlcmd\": { \"" + credential + "\": \"env:ESTATE_LINK\" }")))),
            "profile" => Refused(Profiles.Load(Profile("inline", "<TargetConnectionString>" + credential + "</TargetConnectionString>"))),
            "profile, a comment splitting the password" =>
                Refused(Profiles.Load(Profile("inline", "<TargetConnectionString>Server=db;User ID=sa;Pass<!-- -->word=" + Planted + "</TargetConnectionString>"))),
            "profile's SQLCMD value" => Refused(Profiles.Load(Profile("inline", "", ("LinkedServer", "Server=db;UID=sa;PWD=" + Planted)))),
            "profile's SQLCMD value, password-free" =>
                Refused(Profiles.Load(Profile("inline", "", ("LinkedServer", "Data Source=prod-sql;Initial Catalog=Orders;Integrated Security=True")))),
            "argument" => Refused(SecretReference.Of("--connection", credential)),   // how a connection argument is read
            "argument after file:" => Refused(SecretReference.Of("--connection", "file:" + credential)),
            _ => Refused(SqlServer.Target.Parse(credential, "--target")),            // WP 1.4's target grammar
        };

        Assert.Equal((code, 6), (refusal.Code, Contract.Exit(refusal)));
        Assert.Contains(named, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
    }

    /// <summary>§4 row 14: beside its environments, the posture holds the substrate preference, docker or localdb and nothing else.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("\"docker\"", null)]
    [InlineData("\"localdb\"", null)]
    [InlineData("\"Docker\"", "posture.malformed")]
    [InlineData("\"podman\"", "posture.malformed")]
    [InlineData("true", "posture.malformed")]
    public void The_substrate_preference_is_docker_or_localdb(string substrate, string? code) =>
        Assert.Equal(code, Profiles.Environments(Estate("{ \"environments\": {}, \"substrate\": " + substrate + " }", raw: true)).Match<string?>(_ => null, r => r.Code));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("{ \"environments\": {}, \"targets\": [\"qa\"] }", "targets")]
    [InlineData("{ \"environments\": { \"dev\": { \"connection\": \"env:A\", \"profile\": \"estate/p.publish.xml\", \"Profile\": \"x\" } } }", "environments.dev.Profile")]
    [InlineData("{ \"environments\": { \"dev\": { \"connection\": \"env:A\", \"profile\": \"estate/p.publish.xml\", \"sqlcmd\": { \"Tag\": { \"literal\": \"dev\", \"sensitive\": false, \"value\": \"x\" } } } } }", "environments.dev.sqlcmd.Tag.value")]
    public void An_unknown_key_is_refused_by_its_place_in_the_posture(string posture, string at)
    {
        var refusal = Refused(Profiles.Environments(Estate(posture, raw: true)));

        Assert.Equal(("posture.unknown-key", 6), (refusal.Code, Contract.Exit(refusal)));
        Assert.Contains(at, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Every_refusal_of_the_posture_and_the_profile_is_exit_6()
    {
        foreach (var way in RefusalPaths.All.Where(c => c.Code.Split('.')[0] is "posture" or "profile" or "reference" or "sqlcmd"))
        {
            var refusal = way.Drive(Directory.CreateDirectory(Path.Combine(scratch, way.Label)).FullName, Planted);
            Assert.True(Contract.Exit(refusal) == 6, way.Label + " takes exit " + Contract.Exit(refusal));
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_pipeline_profile_loads_as_Strict_its_deploy_options_exactly_as_DacFx_reads_them()
    {
        var strict = Made(Profiles.Load(Pipeline));

        Assert.Equal(Settings(DacProfile.Load(Pipeline).DeployOptions), Settings(strict.Options()));
        Assert.True(strict.Options().BlockOnPossibleDataLoss);
        Assert.NotSame(strict.Options(), strict.Options());
        Assert.Empty(strict.SqlCmd);
        Assert.Equal("Strict: " + Pipeline, strict.ToString());
    }

    /// <summary>§1 fact 10: a profile contributes its deploy options and SQLCMD values; its target is removed at load, and nothing the profile object holds or prints names it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_profile_naming_a_target_keeps_its_SQLCMD_values_and_nothing_of_the_target()
    {
        var strict = Made(Profiles.Load(Profile("target",
            "<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss><TargetConnectionString>Data Source=sentinel.invalid;Initial Catalog=elsewhere_db;Integrated Security=True</TargetConnectionString><TargetDatabaseName>elsewhere_db</TargetDatabaseName>",
            ("EnvironmentTag", "dev"))));

        Assert.Equal("$(EnvironmentTag), a literal", Assert.Single(strict.SqlCmd).ToString());
        Assert.Equal("dev", strict.Options().SqlCommandVariableValues["EnvironmentTag"]);
        Assert.DoesNotContain(Held(strict), text => text.Contains("sentinel", StringComparison.Ordinal) || text.Contains("elsewhere_db", StringComparison.Ordinal));
        Assert.DoesNotContain(Held(PublishProfile.Permissive.Of(strict)), text => text.Contains("sentinel", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_profile_with_the_guard_off_is_refused_and_a_named_environment_using_it_is_refused_by_its_name()
    {
        var relaxed = Profile("relaxed", "<BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>");
        var root = Estate(Dev(profile: "estate/profiles/relaxed.publish.xml"));
        File.Copy(relaxed, Path.Combine(root, "estate", "profiles", "relaxed.publish.xml"));

        var bare = Refused(Profiles.Load(relaxed));
        var named = Refused(Profiles.Of(Made(Profiles.Environments(root)).Single(), root));

        Assert.Equal(("profile.guard-off", 6), (bare.Code, Contract.Exit(bare)));
        Assert.Equal(("profile.guard-off", 6), (named.Code, Contract.Exit(named)));
        Assert.StartsWith("env:dev's profile estate/profiles/relaxed.publish.xml", named.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Permissive_differs_from_Strict_in_BlockOnPossibleDataLoss_alone()
    {
        var strict = Made(Profiles.Load(Profile("tagged", "<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss><IgnoreColumnOrder>True</IgnoreColumnOrder>", ("EnvironmentTag", "dev"))));
        var permissive = PublishProfile.Permissive.Of(strict);
        var (before, after) = (Settings(strict.Options()), Settings(permissive.Options()));

        Assert.Equal([("BlockOnPossibleDataLoss", "True", "False")], before.Keys.Where(k => before[k] != after[k]).Select(k => (k, before[k], after[k])));
        Assert.Equal((strict.Source, strict.SqlCmd), (permissive.Source, permissive.SqlCmd));
        Assert.True(strict.Options().BlockOnPossibleDataLoss);
        Assert.Equal("Permissive: " + strict.Source, permissive.ToString());
    }

    /// <summary>
    /// §2.1 rule 3: the Permissive profile is made only for a copy. Its constructor is private and Of internal to io, which no
    /// assembly but its tests sees into; and the IL of every method io compiles is searched for a call to either. Of alone calls the
    /// constructor, and WP 1.4's Copy, through Copy.Permissive, alone calls Of: a call from Copy, or from a type nested in it, passes
    /// here and a call from anywhere else in io fails.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Nothing_but_a_Copy_makes_a_Permissive_profile()
    {
        var of = typeof(PublishProfile.Permissive).GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static)!;
        var made = typeof(PublishProfile.Permissive).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();

        Assert.True(made.IsPrivate && of.IsAssembly);
        Assert.Equal(["Estate.Io.Tests"], typeof(PublishProfile).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName));
        Assert.Equal(["Estate.Io.PublishProfile+Permissive.Of"], Callers(made).Select(Named));
        Assert.NotEmpty(Callers(of));
        Assert.Empty(Callers(of).Where(caller => !InCopy(caller.DeclaringType)).Select(Named));
    }

    /// <summary>
    /// VALUES.md X2: every refusal, driven with a password planted in its input wherever the input can carry one, returns neither the
    /// password nor a password setting in its code, its message or its remedy, and throws nothing. The other half of the search is
    /// <see cref="DiffTests.Estate_read_of_a_database_holding_a_SQL_login_prints_no_password"/>, which searches estate read's answer
    /// for a database holding a SQL login.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Ways))]
    public void No_output_contains_Password(string label)
    {
        var way = RefusalPaths.All.Single(c => c.Label == label);

        var refusal = way.Drive(Directory.CreateDirectory(Path.Combine(scratch, "way")).FullName, Planted);
        var output = string.Join('\n', refusal.Code, refusal.Message, refusal.Remedy, refusal);

        Assert.Equal(way.Code, refusal.Code);
        Assert.DoesNotContain(Planted, output, StringComparison.Ordinal);
        Assert.DoesNotMatch(PasswordSetting, output);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Nothing_read_from_the_posture_or_a_profile_prints_a_literal_or_what_a_reference_names()
    {
        var root = Estate(Dev("\"sqlcmd\": { \"EnvironmentTag\": { \"literal\": \"" + Planted + "\", \"sensitive\": false }, \"ServicePassword\": \"env:ESTATE_PW\" }"));
        var environment = Made(Profiles.Environments(root)).Single();
        var strict = Made(Profiles.Load(Profile("printed", "", ("EnvironmentTag", Planted))));

        var printed = string.Join('\n', (object[])[environment, .. environment.SqlCmd, strict, .. strict.SqlCmd, PublishProfile.Permissive.Of(strict)]);

        Assert.Equal(Planted, environment.SqlCmd.Single(v => v.Name == "EnvironmentTag").Match(text => text, reference => "from " + reference));
        Assert.DoesNotContain(Planted, printed, StringComparison.Ordinal);
        Assert.DoesNotMatch(PasswordSetting, printed);
    }

    /// <summary>Each method and constructor io compiles, compiler-made ones included, whose IL holds <paramref name="callee"/>'s metadata token.</summary>
    private static IEnumerable<MethodBase> Callers(MethodBase callee) => typeof(PublishProfile).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared)))
        .Where(m => m.GetMethodBody()?.GetILAsByteArray() is { } il && il.AsSpan().IndexOf(BitConverter.GetBytes(callee.MetadataToken)) >= 0);

    private static bool InCopy(Type? type) => type is not null && (type.Name == "Copy" || InCopy(type.DeclaringType));

    private static string Named(MethodBase method) => method.DeclaringType!.FullName + "." + method.Name;

    /// <summary>Every public property of the options, rendered culture-free, by name: what DacFx publishes with.</summary>
    private static Dictionary<string, string> Settings(DacDeployOptions options) =>
        typeof(DacDeployOptions).GetProperties().ToDictionary(p => p.Name, p => Rendered(p.GetValue(options)), StringComparer.Ordinal);

    private static string Rendered(object? value) => value switch
    {
        null => "null",
        string text => text,
        IDictionary<string, string> map => string.Join(";", map.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Key + "=" + v.Value)),
        IEnumerable items => string.Join(";", items.Cast<object?>().Select(Rendered)),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ when value.GetType().IsClass => string.Join(";", value.GetType().GetProperties().Select(p => p.Name + "=" + Rendered(p.GetValue(value)))),
        _ => value.ToString() ?? "",
    };

    /// <summary>What a profile object prints and holds, field by field through its base types: each string, and each byte array read as UTF-8.</summary>
    private static IEnumerable<string> Held(object profile)
    {
        yield return profile.ToString() ?? "";
        for (var type = profile.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return field.GetValue(profile) switch
                {
                    byte[] bytes => Encoding.UTF8.GetString(bytes),
                    IEnumerable items and not string => string.Join('\n', items.Cast<object?>()),
                    var value => value?.ToString() ?? "",
                };
            }
        }
    }

    /// <summary>A dev environment in posture JSON: its connection, its profile and whatever else is given.</summary>
    private static string Dev(string extra = "", string connection = "env:ESTATE_DEV", string profile = "estate/profiles/pipeline.publish.xml") =>
        "\"dev\": { \"connection\": \"" + connection + "\", \"profile\": \"" + profile + "\"" + (extra.Length > 0 ? ", " + extra : "") + " }";

    /// <summary>An estate's root under the scratch folder, holding estate/posture.json: the environments given, or the text given whole.</summary>
    private string Estate(string environments, bool raw = false)
    {
        var root = Directory.CreateDirectory(Path.Combine(scratch, "estate-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        Directory.CreateDirectory(Path.Combine(root, "estate", "profiles"));
        File.WriteAllText(Path.Combine(root, "estate", "posture.json"), raw ? environments : "{ \"environments\": { " + environments + " } }");
        return root;
    }

    /// <summary>A publish profile named <paramref name="name"/>.publish.xml under the scratch folder: the given properties, and one SQLCMD variable per pair.</summary>
    private string Profile(string name, string properties, params (string Name, string Value)[] sqlCmd)
    {
        XNamespace msbuild = "http://schemas.microsoft.com/developer/msbuild/2003";
        var file = Path.Combine(scratch, name + ".publish.xml");
        new XDocument(new XElement(msbuild + "Project",
            XElement.Parse("<PropertyGroup xmlns=\"" + msbuild.NamespaceName + "\">" + properties + "</PropertyGroup>"),
            new XElement(msbuild + "ItemGroup", sqlCmd.Select(v => new XElement(msbuild + "SqlCmdVariable", new XAttribute("Include", v.Name), new XElement(msbuild + "Value", v.Value))))))
            .Save(file);
        return file;
    }

    private static T Made<T>(Result<T> result) => result.Match(value => value, refusal => throw new Xunit.Sdk.XunitException(refusal.Code + ": " + refusal.Message));

    private static Refusal Refused<T>(Result<T> result) => Assert.IsType<Result<T>.Refused>(result).Refusal;
}
