using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
/// never quotes the value; Strict is the profile as loaded, Permissive differs from it in BlockOnPossibleDataLoss alone and has
/// no public maker, and nothing either prints carries a value a reference names or a literal holds.
/// </summary>
public sealed class ProfilesTests : IDisposable
{
    private const string Planted = "Pa55!planted#7f3a";

    private static readonly string Golden = Path.Combine(Repository.Root, "tests", "Golden");

    private static readonly string Pipeline = Path.Combine(Golden, "proving-ground", "profiles", "pipeline.publish.xml");

    /// <summary>A password set in any connection string, however spelled or spaced: what no output may carry.</summary>
    private static readonly Regex PasswordSetting = new(@"(?:password|pwd)\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
        Assert.Equal("dev", dev.SqlCmd[0].Literal);
        Assert.All(environments, e => Assert.True(Made(Profiles.Of(e, Golden)).Options().BlockOnPossibleDataLoss));
    }

    /// <summary>VALUES.md X1: a literal connection string in the posture or a profile, or given where a reference goes, is exit 6; the refusal names its place and quotes nothing.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("connection", "posture.literal-connection", "environments.dev.connection")]
    [InlineData("metamodel", "posture.literal-connection", "environments.dev.metamodel")]
    [InlineData("sqlcmd", "posture.literal-connection", "environments.dev.sqlcmd.LinkedServer.literal")]
    [InlineData("cohort", "posture.literal-connection", "environments.dev.cohorts[0]")]
    [InlineData("key", "posture.literal-connection", "environments.dev.sqlcmd.#1")]
    [InlineData("profile", "profile.password", "inline.publish.xml")]
    [InlineData("profile's SQLCMD value", "profile.password", "inline.publish.xml")]
    [InlineData("argument", "reference.malformed", "--connection")]
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
            "profile's SQLCMD value" => Refused(Profiles.Load(Profile("inline", "", ("LinkedServer", "Server=db;UID=sa;PWD=" + Planted)))),
            _ => Refused(SecretReference.Of("--connection", credential)),   // how WP 1.4 reads a connection argument
        };

        Assert.Equal((code, 6), (refusal.Code, Contract.Exit(refusal)));
        Assert.Contains(named, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, refusal.Message + refusal.Remedy, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("{ \"environments\": {}, \"substrate\": \"docker\" }", "substrate")]
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
        Assert.DoesNotContain(Held(Profiles.Permissive.Of(strict)), text => text.Contains("sentinel", StringComparison.Ordinal));
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
        var permissive = Profiles.Permissive.Of(strict);
        var (before, after) = (Settings(strict.Options()), Settings(permissive.Options()));

        Assert.Equal([("BlockOnPossibleDataLoss", "True", "False")], before.Keys.Where(k => before[k] != after[k]).Select(k => (k, before[k], after[k])));
        Assert.Equal((strict.Source, strict.SqlCmd), (permissive.Source, permissive.SqlCmd));
        Assert.True(strict.Options().BlockOnPossibleDataLoss);
        Assert.Equal("Permissive: " + strict.Source, permissive.ToString());
    }

    /// <summary>
    /// §2.1 rule 3: the Permissive profile is constructed only for a copy. Nothing public makes one today; WP 1.4's Copy is to be
    /// its one maker, so a public member of Copy, or one taking a Copy, passes here and any other fails. The same scan finds the
    /// public makers of a Strict, so it is no scan that finds nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Nothing_public_makes_a_Permissive_profile_and_a_Copy_is_to_be_its_one_maker()
    {
        Assert.Empty(typeof(Profiles.Permissive).GetConstructors());
        Assert.Empty(Makers(typeof(Profiles.Permissive)));
        Assert.Equal(["Estate.Io.Profiles.Load", "Estate.Io.Profiles.Of"], Makers(typeof(Profiles.Strict)));
    }

    /// <summary>VALUES.md X2: every refusal, driven with a password planted in its input wherever the input can carry one, returns neither the password nor a password setting in its code, its message or its remedy, and throws nothing.</summary>
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

        var printed = string.Join('\n', (object[])[environment, .. environment.SqlCmd, strict, .. strict.SqlCmd, Profiles.Permissive.Of(strict)]);

        Assert.Equal(Planted, environment.SqlCmd.Single(v => v.Name == "EnvironmentTag").Literal);
        Assert.DoesNotContain(Planted, printed, StringComparison.Ordinal);
        Assert.DoesNotMatch(PasswordSetting, printed);
    }

    /// <summary>
    /// Every public member of the v3 assemblies that makes or hands out a <paramref name="type"/>, less a member of Copy or one
    /// taking a Copy: the makers the type has beyond a copy.
    /// </summary>
    private static List<string> Makers(Type type) => new[] { typeof(Seq).Assembly, typeof(Profiles).Assembly, typeof(Contract).Assembly }
        .SelectMany(a => a.GetExportedTypes())
        .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(m => m switch
        {
            ConstructorInfo c => c.DeclaringType == type,
            MethodInfo x => !x.IsSpecialName && Mentions(x.ReturnType, type),
            PropertyInfo p => Mentions(p.PropertyType, type),
            FieldInfo f => Mentions(f.FieldType, type),
            _ => false,
        })
        .Where(m => m.DeclaringType?.Name != "Copy" && !(m is MethodBase method && method.GetParameters().Any(p => p.ParameterType.Name == "Copy")))
        .Select(m => m.DeclaringType!.FullName!.Replace('+', '.') + "." + m.Name)
        .Order(StringComparer.Ordinal)
        .ToList();

    private static bool Mentions(Type type, Type made) =>
        type == made || (type.IsGenericType && type.GetGenericArguments().Any(a => Mentions(a, made))) || (type.HasElementType && Mentions(type.GetElementType()!, made));

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
