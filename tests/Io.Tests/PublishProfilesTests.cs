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
using DbChange.Budgets.Tests;
using DbChange.Budgets.Tests.Register;
using DbChange.Cli;
using DbChange.Kernel;
using Microsoft.SqlServer.Dac;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// io/PublishProfiles and the environments file reader in io/Environments (WP 1.5): the pipeline's publish profile read into its deploy options and
/// SQLCMD values alone, with a note for each element DacFx ignores, and dbchange/environments.json read into named environments. An error in
/// either exits 6 through the CLI's category table, names the key, the file or the property, and never quotes the value; Strict is the
/// profile as loaded, Permissive differs from it in BlockOnPossibleDataLoss alone and nothing in io but a Copy makes one, and nothing
/// either prints carries a value a reference names or a literal holds.
/// </summary>
public sealed class PublishProfilesTests : IDisposable
{
    private const string Planted = "Pa55!planted#7f3a";

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string Golden = Path.Combine(Repository.Root, "tests", "Golden");

    private static readonly string Pipeline = Path.Combine(Golden, "project", "profiles", "pipeline.publish.xml");

    /// <summary>A password set in any connection string, however spelled or spaced: what no output may carry.</summary>
    internal static readonly Regex PasswordSetting = new(@"(?:password|pwd)\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly string scratch = Directory.CreateTempSubdirectory("dbchange-profiles-").FullName;

    public static TheoryData<string> Ways => new(ErrorPaths.All.Select(c => c.Label));

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Fact]
    [Trait("Category", "fast")]
    public void The_sample_environments_file_reads_into_its_named_environments_each_with_the_pipelines_profile()
    {
        var environmentsFile = Made(EnvironmentsFile.Read(Golden));
        var dev = environmentsFile.All.Single(e => e.Name.ToString() == "dev");

        Assert.Equal(["dev", "prod", "qa", "uat"], environmentsFile.All.Select(e => e.Name.ToString()));
        Assert.Equal(["dev-sql.corp.example", "prod-sql.corp.example", "qa-sql.corp.example", "uat-sql.corp.example"], environmentsFile.All.Select(e => e.Host.ToString()));
        Assert.Equal(("docker", (string?)"project/profiles/pipeline.publish.xml"), (environmentsFile.LocalServer?.ToString(), environmentsFile.SharedProfile?.ToString()));
        Assert.Equal("env:dev (synthetic, confirmed by the dev lead on 2026-09-20)", dev.ToString());
        Assert.Equal(["env:prod (real)", "env:qa (real)", "env:uat (real)"], environmentsFile.All.Where(e => e != dev).Select(e => e.ToString()));
        Assert.Equal(["developers", "leads"], dev.ReaderGroups);
        Assert.Equal(("env:DBCHANGE_DEV", (string?)"file:.dbchange/principals/dev-ossys.connection"), (dev.Connection.ToString(), dev.Metamodel?.ToString()));
        Assert.Equal("$(EnvironmentTag), a literal | $(ServiceAccountPassword) from env:DBCHANGE_DEV_SERVICE_PASSWORD", string.Join(" | ", dev.SqlCmd));
        Assert.Equal("dev", dev.SqlCmd[0].Match(text => text, reference => "from " + reference));
        Assert.All(environmentsFile.All, e => Assert.True(Made(PublishProfiles.Of(e, Golden)).Options().BlockOnPossibleDataLoss));
    }

    /// <summary>
    /// VALUES.md X1: a literal connection string in the environments file, a SQLCMD value a profile gives, or an argument where a reference goes,
    /// file: before it or not, is exit 6, and so is a password anywhere in a profile, a comment splitting it or not; the refusal names its
    /// place and quotes nothing. A profile's password-free target is removed at load instead, as WP 1.5 has it and DECISIONS.md reads X1:
    /// "a profile naming a target keeps its SQLCMD values and nothing of the target".
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("connection", "environments.literal-connection", "environments.dev.connection")]
    [InlineData("metamodel", "environments.literal-connection", "environments.dev.metamodel")]
    [InlineData("sqlcmd", "environments.literal-connection", "environments.dev.sqlcmd.LinkedServer.literal")]
    [InlineData("reader", "environments.literal-connection", "environments.dev.readerGroups[0]")]
    [InlineData("key", "environments.literal-connection", "environments.dev.sqlcmd.#1")]
    [InlineData("profile", "profile.password", "inline.publish.xml")]
    [InlineData("profile, a comment splitting the password", "profile.password", "inline.publish.xml")]
    [InlineData("profile's SQLCMD value", "profile.password", "inline.publish.xml")]
    [InlineData("profile's SQLCMD value, password-free", "profile.literal-connection", "inline.publish.xml gives $(LinkedServer)")]
    [InlineData("argument", "reference.malformed", "--connection")]
    [InlineData("argument after file:", "reference.malformed", "--connection")]
    [InlineData("target", "connection.literal", "--target")]
    [Trait("Value", "X1")]
    [Trait("Exit", "M1.7")]
    public void Inline_credential_refused(string where, string code, string named)
    {
        const string credential = "Server=db;User ID=estate;Password=" + Planted;
        var error = where switch
        {
            "connection" => Failed(EnvironmentsFile.Read(RepositoryAt(Dev(connection: credential)))),
            "metamodel" => Failed(EnvironmentsFile.Read(RepositoryAt(Dev("\"metamodel\": \"" + credential + "\"")))),
            "sqlcmd" => Failed(EnvironmentsFile.Read(RepositoryAt(Dev("\"sqlcmd\": { \"LinkedServer\": { \"literal\": \"" + credential + "\", \"sensitive\": false } }")))),
            "reader" => Failed(EnvironmentsFile.Read(RepositoryAt(Dev("\"readerGroups\": [\"" + credential + "\"]")))),
            "key" => Failed(EnvironmentsFile.Read(RepositoryAt(Dev("\"sqlcmd\": { \"" + credential + "\": \"env:DBCHANGE_LINK\" }")))),
            "profile" => Failed(PublishProfiles.Load(Profile("inline", "<TargetConnectionString>" + credential + "</TargetConnectionString>"))),
            "profile, a comment splitting the password" =>
                Failed(PublishProfiles.Load(Profile("inline", "<TargetConnectionString>Server=db;User ID=sa;Pass<!-- -->word=" + Planted + "</TargetConnectionString>"))),
            "profile's SQLCMD value" => Failed(PublishProfiles.Load(Profile("inline", "", ("LinkedServer", "Server=db;UID=sa;PWD=" + Planted)))),
            "profile's SQLCMD value, password-free" =>
                Failed(PublishProfiles.Load(Profile("inline", "", ("LinkedServer", "Data Source=prod-sql;Initial Catalog=Orders;Integrated Security=True")))),
            "argument" => Failed(SecretReference.Of("--connection", credential)),   // how a connection argument is read
            "argument after file:" => Failed(SecretReference.Of("--connection", "file:" + credential)),
            _ => Failed(SqlServer.Target(credential, "--target")),            // WP 1.4's target grammar
        };

        Assert.Equal((code, 6), (error.Code, Contract.Exit(error)));
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>§4 row 14: beside its environments, the environments file holds the local server preference, docker or localdb and nothing else.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("\"docker\"", null)]
    [InlineData("\"localdb\"", null)]
    [InlineData("\"Docker\"", "environments.malformed")]
    [InlineData("\"podman\"", "environments.malformed")]
    [InlineData("true", "environments.malformed")]
    public void The_local_server_preference_is_docker_or_localdb(string preference, string? code) =>
        Assert.Equal(code, EnvironmentsFile.Read(RepositoryAt("{ \"environments\": {}, \"localServer\": " + preference + " }", raw: true)).Match<string?>(_ => null, r => r.Code));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("{ \"environments\": {}, \"targets\": [\"qa\"] }", "targets")]
    [InlineData("{ \"environments\": { \"dev\": { \"host\": \"dev-sql\", \"connection\": \"env:A\", \"profile\": \"dbchange/p.publish.xml\", \"Profile\": \"x\" } } }", "environments.dev.Profile")]
    [InlineData("{ \"environments\": { \"dev\": { \"host\": \"dev-sql\", \"connection\": \"env:A\", \"profile\": \"dbchange/p.publish.xml\", \"sqlcmd\": { \"Tag\": { \"literal\": \"dev\", \"sensitive\": false, \"value\": \"x\" } } } } }", "environments.dev.sqlcmd.Tag.value")]
    public void An_unknown_key_is_refused_by_its_place_in_the_environments_file(string environmentsFile, string at)
    {
        var error = Failed(EnvironmentsFile.Read(RepositoryAt(environmentsFile, raw: true)));

        Assert.Equal(("environments.unknown-key", 6), (error.Code, Contract.Exit(error)));
        Assert.Contains(at, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Every_error_of_the_environments_file_and_the_profile_is_exit_6()
    {
        foreach (var way in ErrorPaths.All.Where(c => c.Code.Split('.')[0] is "environments" or "profile" or "reference" or "sqlcmd"))
        {
            var error = way.Drive(Directory.CreateDirectory(Path.Combine(scratch, way.Label)).FullName, Planted);
            Assert.True(Contract.Exit(error) == 6, way.Label + " takes exit " + Contract.Exit(error));
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_pipeline_profile_loads_as_Strict_its_deploy_options_exactly_as_DacFx_reads_them()
    {
        var strict = Made(PublishProfiles.Load(Pipeline));

        Assert.Equal(Settings(DacProfile.Load(Pipeline).DeployOptions), Settings(strict.Options()));
        Assert.True(strict.Options().BlockOnPossibleDataLoss);
        Assert.NotSame(strict.Options(), strict.Options());
        Assert.Empty(strict.SqlCmd);
        Assert.Equal("Strict: " + Pipeline, strict.ToString());
    }

    /// <summary>
    /// DacProfile reads an element it does not know as nothing (measured): a misspelled BlockOnPossibleDataLos loads, and the option it
    /// misspells keeps its default. Load names each such element in a note, profile.unknown-option; the golden pipeline profile has none.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_misspelled_option_is_a_note_naming_the_element_and_the_option_keeps_its_default()
    {
        var misspelled = Made(PublishProfiles.Load(Profile("misspelled", "<BlockOnPossibleDataLos>False</BlockOnPossibleDataLos><blockonpossibledataloss>True</blockonpossibledataloss>")));

        Assert.True(misspelled.Options().BlockOnPossibleDataLoss);
        Assert.Equal([("profile.unknown-option", Severity.Note, "BlockOnPossibleDataLos")], misspelled.Notes.Select(n => (n.Code, n.Severity, n.Subject)));
        Assert.Empty(Made(PublishProfiles.Load(Pipeline)).Notes);
    }

    /// <summary>
    /// DacProfile refuses a value it cannot read with a message that quotes it ("Property BlockOnPossibleDataLoss has an invalid value: …",
    /// measured); profile.unreadable names the property and withholds the value, which can be a secret.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_value_DacFx_cannot_read_is_refused_naming_the_property_and_withholding_the_value()
    {
        var error = Failed(PublishProfiles.Load(Profile("unreadable", "<BlockOnPossibleDataLoss>" + Planted + "</BlockOnPossibleDataLoss>")));

        Assert.Equal(("profile.unreadable", 6), (error.Code, Contract.Exit(error)));
        Assert.Contains("sets BlockOnPossibleDataLoss to a value DacFx does not read", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// A receipt's profile input is the fingerprint of the profile as kept, and §3's transfer compares two receipts' profiles for
    /// equality, so a receipt written on Windows and one written on Linux must agree. The Windows and the Ubuntu CI jobs both run this
    /// test against the one constant; XDocument.Save's defaults had written CRLF and a byte-order mark on Windows and LF on Linux.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "D3")]
    [Trait("Value", "O2")]
    public void The_pipeline_profile_fingerprints_to_one_committed_value_on_every_operating_system() =>
        Assert.Equal("139ffe34dbec24fadeab8501028b8a50cab2ba547d7cffd28d8e9b20ac0536d5", Made(PublishProfiles.Load(Pipeline)).Fingerprint.ToString());

    /// <summary>A profile fingerprints by its content: the file saved with CRLF and a byte-order mark, as Visual Studio on Windows can save it, and saved with LF alone fingerprint alike.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "D3")]
    public void A_profile_saved_with_CRLF_and_a_byte_order_mark_fingerprints_as_the_same_profile_saved_with_LF()
    {
        var text = File.ReadAllText(Pipeline).ReplaceLineEndings("\n");
        var (lf, crlf) = (Path.Combine(scratch, "lf.publish.xml"), Path.Combine(scratch, "crlf.publish.xml"));
        File.WriteAllText(lf, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(crlf, text.ReplaceLineEndings("\r\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(Made(PublishProfiles.Load(lf)).Fingerprint, Made(PublishProfiles.Load(crlf)).Fingerprint);
        Assert.Equal(Made(PublishProfiles.Load(Pipeline)).Fingerprint, Made(PublishProfiles.Load(crlf)).Fingerprint);
    }

    /// <summary>§1 fact 10: a profile contributes its deploy options and SQLCMD values; its target is removed at load, and nothing the profile object holds or prints names it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_profile_naming_a_target_keeps_its_SQLCMD_values_and_nothing_of_the_target()
    {
        var strict = Made(PublishProfiles.Load(Profile("target",
            "<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss><TargetConnectionString>Data Source=sentinel.invalid;Initial Catalog=elsewhere_db;Integrated Security=True</TargetConnectionString><TargetDatabaseName>elsewhere_db</TargetDatabaseName>",
            ("EnvironmentTag", "dev"))));

        Assert.Equal("$(EnvironmentTag), a literal", Assert.Single(strict.SqlCmd).ToString());
        Assert.Equal("dev", strict.Options().SqlCommandVariableValues["EnvironmentTag"]);
        Assert.DoesNotContain(Held(strict), text => text.Contains("sentinel", StringComparison.Ordinal) || text.Contains("elsewhere_db", StringComparison.Ordinal));
        Assert.DoesNotContain(Held(PublishProfile.Permissive.Of(strict)), text => text.Contains("sentinel", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S1")]
    public void A_profile_that_allows_data_loss_is_refused_and_a_named_environment_using_it_is_refused_by_its_name()
    {
        var relaxed = Profile("relaxed", "<BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>");
        var root = RepositoryAt(Dev(profile: "dbchange/profiles/relaxed.publish.xml"));
        File.Copy(relaxed, Path.Combine(root, "dbchange", "profiles", "relaxed.publish.xml"));

        var bare = Failed(PublishProfiles.Load(relaxed));
        var named = Failed(PublishProfiles.Of(Made(EnvironmentsFile.Read(root)).All.Single(), root));

        Assert.Equal(("profile.data-loss-allowed", 6), (bare.Code, Contract.Exit(bare)));
        Assert.Equal(("profile.data-loss-allowed", 6), (named.Code, Contract.Exit(named)));
        Assert.StartsWith("env:dev's profile dbchange/profiles/relaxed.publish.xml", named.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S1")]
    public void Permissive_differs_from_Strict_in_BlockOnPossibleDataLoss_alone()
    {
        var strict = Made(PublishProfiles.Load(Profile("tagged", "<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss><IgnoreColumnOrder>True</IgnoreColumnOrder>", ("EnvironmentTag", "dev"))));
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
    [Trait("Value", "S1")]
    public void Nothing_but_a_Copy_makes_a_Permissive_profile()
    {
        var of = typeof(PublishProfile.Permissive).GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static)!;
        var made = typeof(PublishProfile.Permissive).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();

        Assert.True(made.IsPrivate && of.IsAssembly);
        Assert.Equal(["DbChange.Budgets.Tests", "DbChange.Io.Tests"], typeof(PublishProfile).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).Order(StringComparer.Ordinal));
        Assert.Equal(["DbChange.Io.PublishProfile+Permissive.Of"], Callers(made).Select(Named));
        Assert.NotEmpty(Callers(of));
        Assert.Empty(Callers(of).Where(caller => !InCopy(caller.DeclaringType)).Select(Named));
    }

    /// <summary>
    /// VALUES.md X2: every error, driven with a password planted in its input wherever the input can carry one, returns neither the
    /// password nor a password setting in its code, its message or its remedy, and throws nothing. The other half of the search is
    /// <see cref="DiffTests.DbChange_read_of_a_database_holding_a_SQL_login_prints_no_password"/>, which searches dbchange read's answer
    /// for a database holding a SQL login.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Ways))]
    [Trait("Value", "X2")]
    public void No_output_contains_Password(string label)
    {
        var way = ErrorPaths.All.Single(c => c.Label == label);

        var error = way.Drive(Directory.CreateDirectory(Path.Combine(scratch, "way")).FullName, Planted);
        var output = string.Join('\n', error.Code, error.Message, error.Remedy, error);

        Assert.Equal(way.Code, error.Code);
        Assert.DoesNotContain(Planted, output, StringComparison.Ordinal);
        Assert.DoesNotMatch(PasswordSetting, output);
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    public void Nothing_read_from_the_environments_file_or_a_profile_prints_a_literal_or_what_a_reference_names()
    {
        var root = RepositoryAt(Dev("\"sqlcmd\": { \"EnvironmentTag\": { \"literal\": \"" + Planted + "\", \"sensitive\": false }, \"ServicePassword\": \"env:DBCHANGE_PW\" }"));
        var environment = Made(EnvironmentsFile.Read(root)).All.Single();
        var strict = Made(PublishProfiles.Load(Profile("printed", "", ("EnvironmentTag", Planted))));

        var printed = string.Join('\n', (object[])[environment, .. environment.SqlCmd, strict, .. strict.SqlCmd, PublishProfile.Permissive.Of(strict)]);

        Assert.Equal(Planted, environment.SqlCmd.Single(v => v.Name.ToString() == "EnvironmentTag").Match(text => text, reference => "from " + reference));
        Assert.DoesNotContain(Planted, printed, StringComparison.Ordinal);
        Assert.DoesNotMatch(PasswordSetting, printed);
    }

    /// <summary>Each method and constructor io compiles, compiler-made ones included, whose IL holds <paramref name="callee"/>'s metadata token.</summary>
    private static IEnumerable<MethodBase> Callers(MethodBase callee) => typeof(PublishProfile).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared)))
        .Where(m => m.GetMethodBody()?.GetILAsByteArray() is { } il && Calls(il, callee.MetadataToken));

    /// <summary>Whether IL holds a call, callvirt, newobj, ldftn or ldvirtftn of the member whose metadata token is <paramref name="token"/>: the token after its opcode, never the same four bytes elsewhere.</summary>
    private static bool Calls(byte[] il, int token) => Enumerable.Range(1, Math.Max(0, il.Length - 4)).Any(i =>
        il.AsSpan(i, 4).SequenceEqual(BitConverter.GetBytes(token)) && (il[i - 1] is 0x28 or 0x6F or 0x73 || (i >= 2 && il[i - 2] == 0xFE && il[i - 1] is 0x06 or 0x07)));

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

    /// <summary>A dev environment in the environments file's JSON: its host, its connection, its profile and whatever else is given.</summary>
    private static string Dev(string extra = "", string connection = "env:DBCHANGE_DEV", string profile = "dbchange/profiles/pipeline.publish.xml") =>
        "\"dev\": { \"host\": \"dev-sql\", \"connection\": \"" + connection + "\", \"profile\": \"" + profile + "\"" + (extra.Length > 0 ? ", " + extra : "") + " }";

    /// <summary>A repository root under the scratch folder, holding dbchange/environments.json: the environments given, or the text given whole.</summary>
    private string RepositoryAt(string environments, bool raw = false)
    {
        var root = Directory.CreateDirectory(Path.Combine(scratch, "repository-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        Directory.CreateDirectory(Path.Combine(root, "dbchange", "profiles"));
        File.WriteAllText(Path.Combine(root, "dbchange", "environments.json"), raw ? environments : "{ \"environments\": { " + environments + " } }");
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

    private static T Made<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    private static Error Failed<T>(Result<T> result) => Assert.IsType<Result<T>.Failed>(result).Error;
}
