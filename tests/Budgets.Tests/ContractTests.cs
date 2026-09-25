using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CsCheck;
using Estate.Cli;
using Estate.Io;
using Json.Schema;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>The CLI contract: the verb table, the envelope and the frozen exit table, checked against cli/schemas/.</summary>
public sealed class ContractTests
{
    public static TheoryData<string> Answers => new(Contract.Verbs.Select(v => v.Name).Concat(["no-such-verb", ""]));

    [Fact]
    [Trait("Category", "fast")]
    public void Help_json_validates_against_its_committed_schema()
    {
        var (exit, output) = Estate("--help", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain('\r', output);
        var help = JsonNode.Parse(output)!;
        AssertValid("estate.help.1.schema.json", help);
        Assert.Equal(
            ["doctor", "read", "diff", "classify", "predict", "measure", "synthetic-copy", "prove", "describe", "gate", "check", "knowledge", "--version"],
            help["verbs"]!.AsArray().Select(v => (string)v!["name"]!));
        help["exits"]![0]!["code"] = 8;
        Assert.False(Evaluate("estate.help.1.schema.json", help).IsValid, "the schema admits an exit code the table does not have");
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_committed_schemas_are_what_the_contract_generates()
    {
        var directory = Path.Combine(Repository.Root, "cli", "schemas");
        var generated = Render.Schemas().ToDictionary(s => Render.SchemaFile(s.Id), s => Io.Json.Text(s.Schema));
        if (Environment.GetEnvironmentVariable("ESTATE_BLESS") == "1")
        {
            foreach (var (file, text) in generated)
            {
                Write.Text(Path.Combine(directory, file), text);
            }
        }

        Assert.Equal(generated.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var (file, text) in generated)
        {
            Assert.True(text == File.ReadAllText(Path.Combine(directory, file)), $"cli/schemas/{file} is stale: regenerate with ESTATE_BLESS=1 dotnet test --filter Category=fast");
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_exit_table_never_shrinks_or_renumbers()
    {
        var frozen = File.ReadAllLines(Path.Combine(Repository.Root, "cli", "exits.frozen"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(' ', 2))
            .Select(p => (Code: int.Parse(p[0], CultureInfo.InvariantCulture), Name: p[1]))
            .ToList();
        var table = Contract.Exits.Select(e => (e.Code, e.Name)).ToList();

        Assert.Empty(frozen.Except(table));    // a frozen code is never removed, renamed or renumbered
        Assert.Empty(table.Except(frozen));    // a new code is frozen in the change that adds it
    }

    /// <summary>
    /// The kernel, io and the cli name what went wrong; the category table alone says which exit that is, by the code's category. The codes
    /// are Register.RefusalPaths', which Register.Refusals holds to every code the three packages construct, composed ones included, so an
    /// error of a new category fails here until the table gives the category a row; and a row for a category nothing constructs fails too.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_error_the_kernel_io_and_the_cli_construct_has_a_row_for_its_category_in_the_category_table()
    {
        var categories = Register.RefusalPaths.All.Select(c => c.Code.Split('.')[0]).Distinct().Order(StringComparer.Ordinal).ToList();

        Assert.DoesNotContain(categories, category => !Contract.ExitByCategory.ContainsKey(category));
        Assert.Empty(Contract.ExitByCategory.Keys.Except(categories));
        Assert.Empty(Contract.ExitByCategory.Values.Except(Contract.Exits.Select(e => e.Code)));
        Assert.Equal([1, 2, 2, 2, 6, 6, 7], ((string[])["arguments.unknown-flag", "name.blank", "element.property-name", "fingerprint.malformed", "sdk.missing", "dacfx.failed", "build.failed"])
            .Select(c => Contract.Exit(new Kernel.Error(c, "Failed.", "Do the other thing."))));
    }

    /// <summary>A category the category table lacks still answers with an envelope: exit 6, the error's own finding, and one naming the missing row.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_error_whose_category_has_no_row_answers_exit_6_with_a_finding_naming_the_category()
    {
        var answer = Contract.Failed(Contract.Verbs.Single(v => v.Name == "read"), new Kernel.Error("nowhere.failed", "Failed.", "Do the other thing."));

        Assert.Equal(6, answer.Exit);
        Assert.Equal(["nowhere.failed", "internal.unmapped-category"], answer.Findings.Select(f => f.Code));
        Assert.Contains("'nowhere'", answer.Findings[1].Message, StringComparison.Ordinal);
        AssertValid("estate.read.1.schema.json", Render.Json(answer));
    }

    /// <summary>
    /// Finding X-1: the committed schemas carry the kernel's one code pattern as a finding's code, so an error whose category is
    /// hyphenated, as scratch-server is, answers with an envelope that validates against the envelope's schema and its verb's; and over
    /// generated codes and their near misses (a capital, a space, a line break, a doubled or stray dot or hyphen), the kernel constructs
    /// an Error exactly when the envelope's schema admits the code as a finding's.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_schemas_admit_exactly_the_codes_the_kernel_constructs_a_hyphenated_category_included()
    {
        var answer = Render.Json(Contract.Failed(Contract.Verbs.Single(v => v.Name == "read"), new Kernel.Error("scratch-server.missing", "No SQL Server answers for copies.", "Run ci/sql.sh up.")));
        AssertValid("estate.read.1.schema.json", answer);
        AssertValid("estate.envelope.1.schema.json", answer);

        var word = Gen.Char["a0z9"].Array[1, 3].Select(cs => new string(cs)).Array[1, 2].Select(pieces => string.Join('-', pieces));
        var code = word.Array[2, 3].Select(words => string.Join('.', words));
        var nearMiss = Gen.Select(code, Gen.Int[0, 12], Gen.Char[".-A \n_"]).Select((text, at, mark) => text.Insert(at % (text.Length + 1), mark.ToString()));
        Gen.OneOf(code, nearMiss).Sample(text =>
            (Record.Exception(() => new Kernel.Error(text, "Failed.", "Do the other thing.")) is null)
            == Evaluate("estate.envelope.1.schema.json", Answer(Finds(0, "note", code: text))).IsValid);
    }

    /// <summary>
    /// The alignment review's reproduction (ARCH-03): a package whose table has a column named by a space, which DacFx builds and the kernel's
    /// Name rejects (name.blank), read with --json answers exit 2 with an envelope carrying the error, and throws nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Reading_a_package_with_a_column_named_by_a_space_answers_exit_2_with_the_name_blank_error()
    {
        Telemetry.OptOut();   // before DacFx loads, as estate's Main does
        var scratch = Directory.CreateTempSubdirectory("estate-blank-name-").FullName;
        try
        {
            var dacpac = Path.Combine(scratch, "blank.dacpac");
            using (var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160, new Microsoft.SqlServer.Dac.Model.TSqlModelOptions()))
            {
                model.AddObjects("CREATE TABLE dbo.Customer (Id INT NOT NULL, [ ] INT NULL);");
                Microsoft.SqlServer.Dac.DacPackageExtensions.BuildPackage(dacpac, model, new Microsoft.SqlServer.Dac.PackageMetadata());
            }

            var (exit, answer) = Answered(["read", "--from", "dacpac:" + dacpac, "--json"], new Checkout(scratch, scratch, null));

            Assert.Equal(2, exit);
            AssertValid("estate.read.1.schema.json", answer);
            Assert.Equal(["name.blank"], answer["findings"]!.AsArray().Select(f => (string)f!["code"]!));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>
    /// An exception no verb expected answers with an envelope: exit 6, one finding internal.unexpected naming the exception's type, and its
    /// message kept when the command read no named environment. A checkout with no working directory makes read throw ArgumentNullException.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_unexpected_exception_answers_exit_6_with_a_finding_naming_its_type_and_its_message()
    {
        var (exit, answer) = Answered(["read", "--from", "dacpac:none.dacpac", "--json"], new Checkout(Repository.Root, null!, null));

        Assert.Equal(6, exit);
        AssertValid("estate.read.1.schema.json", answer);
        var finding = Assert.Single(answer["findings"]!.AsArray())!;
        Assert.Equal(("internal.unexpected", "error"), ((string)finding["code"]!, (string)finding["severity"]!));
        Assert.Contains("ArgumentNullException: Value cannot be null.", (string)finding["message"]!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The withholding rule is keyed to what the run read, not to how the arguments are spelled: check drift naming env:dev or a copy, in a
    /// checkout with no root, throws ArgumentNullException before it reads any environment's connection, so the message is estate's own and
    /// is kept.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env:dev")]
    [InlineData("copy:estate_host_1_0a1b2c3d")]
    public void An_unexpected_exception_keeps_its_message_when_the_command_names_a_database_but_read_no_environment(string target)
    {
        var (exit, answer) = Answered(["check", "drift", "--target", target, "--at", "main", "--json"], new Checkout(null!, Repository.Root, null));

        Assert.Equal(6, exit);
        AssertValid("estate.check.1.schema.json", answer);
        var message = (string)Assert.Single(answer["findings"]!.AsArray())!["message"]!;
        Assert.Contains("ArgumentNullException: Value cannot be null.", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// VALUES.md X2 at the top-level catch, for M2's predict and M6's check environments, which read environments without an env: argument:
    /// a verb that resolves env:dev (through EnvironmentDatabase.Of) or a copy (through R15's read of dev's connection in io/ScratchServer) and then throws has
    /// its exception's message withheld, its type alone printed; one that resolves a git ref reads no environment and keeps the message.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env:dev", true)]
    [InlineData("copy:estate_host_1_0a1b2c3d", true)]
    [InlineData("ref:main", false)]
    public void An_unexpected_exception_withholds_its_message_when_the_run_read_a_named_environment_with_no_env_argument(string target, bool withheld)
    {
        const string Planted = "Server=tcp:192.0.2.10,1433;Password=Pa55!planted#7f3a";
        var root = Directory.CreateTempSubdirectory("estate-withheld-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "estate"));
            Directory.CreateDirectory(Path.Combine(root, ".estate"));
            File.WriteAllText(Path.Combine(root, "dev.connection"), "Server=tcp:192.0.2.10,1433;Initial Catalog=Estate;User ID=estate;Password=Pa55!planted#7f3a");
            File.WriteAllText(Path.Combine(root, "estate", "posture.json"),
                "{ \"environments\": { \"dev\": { \"connection\": \"file:dev.connection\", \"profile\": \"estate/profiles/pipeline.publish.xml\" } } }");
            File.WriteAllText(Path.Combine(root, ".estate", "copies.json"),
                "{ \"copies\": [ { \"name\": \"estate_host_1_0a1b2c3d\", \"server\": \"localhost,11433\", \"host\": \"host\", \"pid\": 1, \"created\": \"2026-09-25T00:00:00Z\" } ] }");
            var check = Contract.Verbs.Single(v => v.Name == "check") with
            {
                Body = (here, _) =>
                {
                    SqlServer.Target.Parse(target).Bind(parsed => SqlServer.Resolve(parsed, here.Root));
                    throw new InvalidOperationException(Planted);
                },
            };

            using var output = new MemoryStream();
            var exit = Cli.Program.Run(["check", "environments", "--json"], output, () => new Checkout(root, root, null), [check]);
            var answer = JsonNode.Parse(output.ToArray())!;

            Assert.Equal(6, exit);
            AssertValid("estate.check.1.schema.json", answer);
            var message = (string)Assert.Single(answer["findings"]!.AsArray())!["message"]!;
            Assert.Contains("InvalidOperationException", message, StringComparison.Ordinal);
            Assert.Equal(withheld, !answer.ToJsonString().Contains("planted", StringComparison.Ordinal));
            Assert.Equal(withheld, message.Contains("withheld", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The catch holds the whole command, not the verb's body alone: the checkout failing as Directory.GetCurrentDirectory does once the
    /// working directory is removed (a swept .estate/worktrees/&lt;commit&gt;/) answers internal.unexpected at exit 6 with read's schema.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_exception_raised_before_the_verb_body_runs_answers_exit_6_with_an_envelope()
    {
        using var output = new MemoryStream();
        var exit = Cli.Program.Run(["read", "--from", "dacpac:none.dacpac", "--json"], output,
            () => throw new FileNotFoundException("The working directory was removed."), Contract.Verbs);
        var answer = JsonNode.Parse(output.ToArray())!;

        Assert.Equal(6, exit);
        AssertValid("estate.read.1.schema.json", answer);
        var finding = Assert.Single(answer["findings"]!.AsArray())!;
        Assert.Equal("internal.unexpected", (string)finding["code"]!);
        Assert.Contains("FileNotFoundException: The working directory was removed.", (string)finding["message"]!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Writing the answer is inside the catch too: standard output refusing the first write gets the internal.unexpected answer naming the
    /// IOException; refusing every write gets nothing, and estate still exits 6 rather than the verb's exit or an unhandled exception.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Standard_output_refusing_the_answer_exits_6(int refusedWrites)
    {
        using var output = new RefusingStream(refusedWrites);
        var exit = Cli.Program.Run(["no-such-verb", "--json"], output, () => new Checkout(Repository.Root, Repository.Root, null), Contract.Verbs);

        Assert.Equal(6, exit);
        if (refusedWrites == 1)
        {
            var finding = Assert.Single(JsonNode.Parse(output.ToArray())!["findings"]!.AsArray())!;
            Assert.Equal("internal.unexpected", (string)finding["code"]!);
            Assert.Contains("IOException: The pipe is closed.", (string)finding["message"]!, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(0, output.Length);
        }
    }

    /// <summary>A stream that throws IOException on its first <paramref name="refused"/> writes, as standard output does once its reader has gone, and keeps what it accepts after.</summary>
    private sealed class RefusingStream(int refused) : MemoryStream
    {
        private int refusals;

        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (refusals++ < refused)
            {
                throw new IOException("The pipe is closed.");
            }

            base.Write(buffer, offset, count);
        }
    }

    /// <summary>estate run in this process for the checkout given: its exit and its --json answer.</summary>
    private static (int Exit, JsonNode Answer) Answered(string[] arguments, Checkout here)
    {
        using var output = new MemoryStream();
        var exit = Cli.Program.Run(arguments, output, here);
        return (exit, JsonNode.Parse(output.ToArray())!);
    }

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Answers))]
    public void Every_verb_answers_with_an_envelope_that_validates(string verb)
    {
        var (exit, output) = Estate(verb, "--json");

        var envelope = JsonNode.Parse(output)!;
        AssertValid("estate.envelope.1.schema.json", envelope);
        Assert.Equal(exit, (int)envelope["exit"]!);
        if (Contract.Verbs.SingleOrDefault(v => v.Name == verb) is { Content: not null } built)
        {
            AssertValid(Render.SchemaFile(built.Output), envelope);   // its own schema too, whatever the exit
        }
    }

    /// <summary>WP 1.7: each verb built at M1 writes its own schema, the envelope and what the verb adds, committed under cli/schemas/.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_verb_built_at_M1_has_its_own_schema_and_its_error_answer_validates_against_it()
    {
        var built = Contract.Verbs.Where(v => v.Arrives == 1).ToList();

        Assert.Equal(["doctor", "read", "diff", "check"], built.Select(v => v.Name));
        foreach (var verb in built)
        {
            Assert.True(File.Exists(Path.Combine(Repository.Root, "cli", "schemas", Render.SchemaFile(verb.Output))), verb.Output + " has no schema under cli/schemas/");
            using var output = new MemoryStream();
            var exit = Cli.Program.Run([verb.Name, "--no-such-flag", "--json"], output, new Checkout(Repository.Root, Repository.Root, null));
            var answer = JsonNode.Parse(output.ToArray())!;
            Assert.Equal(1, exit);
            AssertValid(Render.SchemaFile(verb.Output), answer);
            Assert.All(verb.Content!, added => Assert.Null(answer[added.Key]));
            answer[verb.Content!.First().Key] = new JsonObject();
            Assert.False(Evaluate(Render.SchemaFile(verb.Output), answer).IsValid, verb.Output + " admits what the verb adds in a shape it never writes");
        }
    }

    /// <summary>
    /// Each rule of the envelope schema as a minimal pair over the answer estate writes to --version --json: two changes
    /// that differ only in what the rule governs, one the rule admits and one it refuses. A rule made vacuous fails its
    /// pair; the admitted half shows that the refusal is the rule's, not a broken answer's.
    /// </summary>
    public static TheoryData<string> EnvelopeRules => new(EnvelopePairs.Keys);

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(EnvelopeRules))]
    public void Each_envelope_rule_refuses_the_answer_that_breaks_it(string rule)
    {
        var (admitted, refused) = EnvelopePairs[rule];

        AssertValid("estate.envelope.1.schema.json", Answer(admitted));
        Assert.False(Evaluate("estate.envelope.1.schema.json", Answer(refused)).IsValid, "the envelope schema admits an answer that breaks: " + rule);
    }

    /// <summary>§4 row 15, through the contract's own types: exit 3 names how the data blocked, and no other exit has a kind.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(3, Blocked.DataLossCheck, "guard")]
    [InlineData(3, Blocked.Violation, "violation")]
    [InlineData(3, null, null)]
    [InlineData(0, null, null)]
    [InlineData(0, Blocked.DataLossCheck, "guard")]
    public void A_verdict_names_its_kind_exactly_when_the_data_blocked(int exit, Blocked? kind, string? written)
    {
        var answer = Contract.Answer("estate.prove/1", "blocked", "Msg 50000: rows were detected.", [], exit);

        var json = Render.Json(answer with { Verdict = answer.Verdict with { Kind = kind } });

        Assert.Equal(written, (string?)json["verdict"]!["kind"]);
        Assert.Equal((exit == 3) == kind.HasValue, Evaluate("estate.envelope.1.schema.json", json).IsValid);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_verb_not_built_yet_says_the_milestone_it_arrives_in()
    {
        var (exit, output) = Estate("predict");
        Assert.Equal(6, exit);
        Assert.Contains("M2 (Predict)", output, StringComparison.Ordinal);

        var (checkExit, check) = Estate("check", "outsystems");
        Assert.Equal(6, checkExit);
        Assert.Contains("M6 (After deploy)", check, StringComparison.Ordinal);
    }

    /// <summary>WP 1.7's doctor on a bare machine: DEGRADED, exit 6, one finding of severity error with its remedy per missing item, and no milestone deferred to.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Doctor_on_a_bare_machine_prints_DEGRADED_with_a_remedy_per_missing_item()
    {
        var bare = Directory.CreateTempSubdirectory("estate-bare-").FullName;
        try
        {
            var checks = Doctor.Examine(bare, null, bare, (_, _) => null, Contract.Version);

            var json = Render.Json(Verbs.Doctor(checks, Doctor.Toolchain(bare, Contract.Version)));

            AssertValid("estate.doctor.1.schema.json", json);
            Assert.Equal(6, (int)json["exit"]!);
            var line = (string)json["verdict"]!["message"]!;
            Assert.StartsWith("estate doctor DEGRADED | sdk=", line, StringComparison.Ordinal);
            Assert.Contains(" | dacfx=" + Doctor.DacFx + " (UNPINNED) | ", line, StringComparison.Ordinal);
            Assert.DoesNotContain("M1", line, StringComparison.Ordinal);
            var findings = json["findings"]!.AsArray().Select(f => ((string)f!["code"]!, (string)f["severity"]!, (string?)f["remedy"])).ToList();
            Assert.Equal(["doctor.sdk", "doctor.tool", "doctor.build", "doctor.scratch-server", "doctor.lfs"], findings.Select(f => f.Item1));
            Assert.Equal(checks.Where(c => c.Remedy is not null).Select(c => c.Remedy), findings.Select(f => f.Item3));
            Assert.All(findings, f => Assert.Equal("error", f.Item2));
            Assert.Equal(checks.Select(c => c.Item), json["checks"]!.AsArray().Select(c => (string)c!["item"]!));
        }
        finally
        {
            Directory.Delete(bare, recursive: true);
        }
    }

    /// <summary>WP 1.7's doctor with every item present: READY and exit 0, naming the SDK and runtime, the tool and its DacFx against the ledger, the build route, the scratch server and LFS.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Doctor_with_every_item_present_prints_READY_and_exits_0()
    {
        var machine = Directory.CreateTempSubdirectory("estate-ready-").FullName;
        try
        {
            foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(machine, file))!);
                File.WriteAllText(Path.Combine(machine, file), "");
            }

            File.WriteAllText(Path.Combine(machine, "global.json"), """{ "sdk": { "version": "10.0.401" } }""");
            Doctor.Command answers = (file, arguments) => (file + " " + arguments[0]) switch
            {
                "dotnet --list-sdks" => (0, "10.0.402 [x]\n"),
                "docker info" => (0, "29.5.3\n"),
                "docker image" => (0, "sha256:5b09\n"),
                "git lfs" => (0, "git-lfs/3.4.0 (GitHub; windows amd64; go 1.21.1)\n"),
                _ => null,
            };

            var answer = Verbs.Doctor(Doctor.Examine(machine, null, machine, answers, Contract.Version), Doctor.Toolchain(machine, Contract.Version));

            var json = Render.Json(answer);
            AssertValid("estate.doctor.1.schema.json", json);
            Assert.Equal((0, "ready"), (answer.Exit, answer.Verdict.Outcome));
            Assert.Equal("estate doctor READY | sdk=10.0.402 | runtime=" + Environment.Version + " | tool=published | dacfx=" + Doctor.DacFx + " (UNPINNED) | build=dotnet with the tool folder's targets"
                + " | scratch-server=docker 29.5.3 | image=present | lfs=git-lfs/3.4.0", answer.Verdict.Message);
            Assert.Empty(answer.Findings);
            Assert.Equal(("170.5.96", Doctor.ImageDigest, "UNPINNED"), ((string?)json["engine"]!["dacfx"], (string?)json["engine"]!["sqlserver"], (string?)json["engine"]!["pin"]));
        }
        finally
        {
            Directory.Delete(machine, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Main_opts_out_of_telemetry_before_anything_else()
    {
        var main = typeof(Cli.Program).GetMethod(nameof(Cli.Program.Main))!;
        var il = main.GetMethodBody()!.GetILAsByteArray()!;
        var first = Array.FindIndex(il, b => b != 0x00);    // past the nops a debug build emits
        Assert.Equal(0x28, il[first]);                      // call
        Assert.Equal(typeof(Telemetry).GetMethod(nameof(Telemetry.OptOut)), main.Module.ResolveMethod(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(first + 1))));
        Assert.Null(typeof(Cli.Program).TypeInitializer);   // no static initializer runs ahead of Main

        Environment.SetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT", null);
        Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", null);
        Telemetry.OptOut();
        Assert.Equal("1", Environment.GetEnvironmentVariable("DACFX_TELEMETRY_OPTOUT"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT"));
    }

    private static void AssertValid(string schemaFile, JsonNode instance)
    {
        var results = Evaluate(schemaFile, instance);
        Assert.True(results.IsValid, JsonSerializer.Serialize(results));
    }

    /// <summary>Formats are asserted, not only annotated, so the receipt's date-time is a rule and not a comment.</summary>
    private static EvaluationResults Evaluate(string schemaFile, JsonNode instance) =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(Repository.Root, "cli", "schemas", schemaFile)))
            .Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    /// <summary>
    /// The envelope's rules, each named by the sentence it pins, as (admitted, refused). The exit codes and the field
    /// names are written out rather than read from the contract, so a rule narrowed there fails here.
    /// </summary>
    private static readonly Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)> EnvelopePairs = Pairs();

    private static Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)> Pairs()
    {
        var pairs = new Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)>(StringComparer.Ordinal)
        {
            ["exit is a code of the frozen table"] = (a => a["exit"] = 7, a => a["exit"] = 8),
            ["exit 3 names its kind"] = (BlockedBy("guard"), BlockedBy(null)),
            ["exit 3's kind is guard or violation"] = (BlockedBy("violation"), BlockedBy("other")),
            ["no other exit has a kind"] = (_ => { }, a => a["verdict"]!["kind"] = "guard"),
            ["a verdict names its kind, as null when the data did not block"] = (_ => { }, a => a["verdict"]!.AsObject().Remove("kind")),
            ["a finding of severity error carries a remedy"] = (Finds(1, "warning", remedy: null), Finds(1, "error", remedy: null)),
            ["a receipt names its data facts, as null when it lacks them"] = (WithReceipt(r => r["dataFacts"] = null), WithReceipt(r => r.Remove("dataFacts"))),
            ["a receipt's at is a date-time"] = (WithReceipt(_ => { }), WithReceipt(r => r["at"] = "yesterday")),
            ["a receipt names the input it lacks, as null when it lacks none"] = (WithReceipt(r => r["lacking"] = null), WithReceipt(r => r["lacking"] = "seed")),
            ["the engine names the SQL Server image by its digest"] = (WithReceipt(_ => { }), WithReceipt(r => r["engine"]!["sqlserver"] = "16.0.4295.3")),
        };

        // Instruction architecture §9.1: a remedy is required for severity error and for exits 2, 4, 6 and 9.
        foreach (var exit in (int[])[2, 4, 6, 9])
        {
            var code = exit.ToString(CultureInfo.InvariantCulture);
            pairs["exit " + code + " names a finding"] = (Finds(exit, "error", "estate doctor"), Finds(exit, severity: null));
            pairs["exit " + code + " gives every finding a remedy"] = (Finds(exit, "warning", "estate doctor"), Finds(exit, "warning", remedy: null));
        }

        // Milestones §3: the receipt's five inputs, where and when; the engine names the tool, DacFx and SQL Server, null when unknown.
        foreach (var field in (string[])["delta", "target", "engine", "profile", "where", "at", "lacking"])
        {
            pairs["a receipt carries its " + field] = (WithReceipt(_ => { }), WithReceipt(r => r.Remove(field)));
        }

        foreach (var field in (string[])["estate", "dacfx", "sqlserver", "pin"])
        {
            pairs["the engine names its " + field] = (_ => { }, a => a["engine"]!.AsObject().Remove(field));
        }

        // The envelope writes a fingerprint as sha256: and the digest's 64 lowercase hex digits, and nothing else.
        (string Name, string Text)[] malformed = [("63 digits", "sha256:" + new string('a', 63)), ("upper case", "sha256:" + new string('A', 64)), ("another algorithm", "md5:" + new string('a', 64)), ("no algorithm", new string('a', 64))];
        foreach (var field in (string[])["delta", "target", "dataFacts", "profile"])
        {
            foreach (var (name, text) in malformed)
            {
                pairs["a receipt's " + field + " is a sha256 fingerprint, not " + name] = (WithReceipt(_ => { }), WithReceipt(r => r[field] = text));
            }
        }

        return pairs;
    }

    /// <summary>The answer estate writes to --version --json, then changed.</summary>
    private static JsonNode Answer(Action<JsonObject> change)
    {
        using var output = new MemoryStream();
        Cli.Program.Run(["--version", "--json"], output);
        var answer = JsonNode.Parse(output.ToArray())!.AsObject();
        change(answer);
        return answer;
    }

    /// <summary>Blocked by the data: exit 3, with the verdict's kind, or with none.</summary>
    private static Action<JsonObject> BlockedBy(string? kind) => answer =>
    {
        answer["exit"] = 3;
        answer["verdict"]!["kind"] = kind;
    };

    /// <summary>An answer at <paramref name="exit"/> with one finding of <paramref name="severity"/> and <paramref name="code"/>, or with none when the severity is null.</summary>
    private static Action<JsonObject> Finds(int exit, string? severity, string? remedy = null, string code = "data-loss-check.rows-present") => answer =>
    {
        answer["exit"] = exit;
        answer["findings"] = severity is null ? new JsonArray() : new JsonArray(new JsonObject
        {
            ["code"] = code, ["severity"] = severity, ["subject"] = "dbo.Customer.Email",
            ["message"] = "The data-loss check stopped the publish: the table has rows.", ["remedy"] = remedy,
        });
    };

    /// <summary>An answer carrying a whole, well-formed receipt (§3's five inputs, where and when), then changed.</summary>
    private static Action<JsonObject> WithReceipt(Action<JsonObject> change) => answer =>
    {
        var receipt = new JsonObject
        {
            ["delta"] = "sha256:" + new string('1', 64), ["target"] = "sha256:" + new string('2', 64), ["dataFacts"] = "sha256:" + new string('3', 64),
            ["engine"] = new JsonObject { ["estate"] = "3.0.0", ["dacfx"] = "170.5.96", ["sqlserver"] = "sha256:" + new string('5', 64), ["pin"] = "UNPINNED" },
            ["profile"] = "sha256:" + new string('4', 64), ["where"] = "env:dev", ["at"] = "2026-09-23T20:47:51Z", ["lacking"] = "dataFacts",
        };
        change(receipt);
        answer["receipt"] = receipt;
    };

    /// <summary>Runs the built estate, as a process, and returns its exit code and standard output alone, which its JSON answer is.</summary>
    private static (int Exit, string Output) Estate(params string[] arguments)
    {
        var ran = new Command(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [Path.Combine(AppContext.BaseDirectory, "estate.dll"), .. arguments], Programs.Default).Finish();
        return (ran.Code, ran.Output);
    }
}
