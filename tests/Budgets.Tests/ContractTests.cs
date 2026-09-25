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
using System.Text.RegularExpressions;
using CsCheck;
using Estate.Cli;
using Estate.Io;
using Estate.Kernel;
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
    /// The kernel, io and the cli name what went wrong; the exit is the category's arm of Contract.ExitByCategory, a switch over the
    /// kernel's closed ErrorCategory with no discard arm, so a member without an arm fails the build. The codes are Register.RefusalPaths',
    /// which Register.Refusals holds to every code the three packages construct, composed ones included: a member no path constructs
    /// fails here, as does a path whose code names a word no member writes as, and every arm names a code of the frozen exit table.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_error_category_has_an_exit_of_the_frozen_table_and_is_constructed_by_some_path()
    {
        var members = Enum.GetValues<ErrorCategory>();
        var constructed = Register.RefusalPaths.All.Select(c => c.Code.Split('.')[0]).Distinct().Order(StringComparer.Ordinal).ToList();

        Assert.Equal(members.Select(ErrorCode.Text).Order(StringComparer.Ordinal), constructed);
        Assert.All(members, m => Assert.Contains(Contract.ExitByCategory(m), Contract.Exits.Select(e => e.Code)));
        Assert.Equal([1, 1, 2, 2, 2, 6, 6, 6, 6, 7, 4], ((string[])["arguments.unknown-flag", "arguments.unknown-verb", "name.blank", "element.property-name", "fingerprint.malformed",
            "sdk.missing", "dacfx.failed", "verb.not-built", "internal.unexpected", "build.failed", "scratch-server.missing"])
            .Select(c => Contract.Exit(new Kernel.Error(c, "Failed.", "Do the other thing."))));
        Assert.Equal((4, 9), (Contract.ExitByCategory(ErrorCategory.ScratchServer), Contract.ExitByCategory(ErrorCategory.AggregateQuery)));
    }

    /// <summary>
    /// Finding X-1: the committed schemas carry the kernel's one code pattern as a finding's code, so an error whose category is
    /// hyphenated, as scratch-server is, answers with an envelope that validates against the envelope's schema and its verb's; and over
    /// generated codes and their near misses (a capital, a space, a line break, a doubled or stray dot or hyphen), the kernel's pattern
    /// admits a code exactly when the envelope's schema admits it as a finding's. A code the pattern admits is still refused by the kernel
    /// when its category names no member.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_schemas_admit_exactly_the_codes_the_kernel_s_pattern_admits_a_hyphenated_category_included()
    {
        var answer = Render.Json(Contract.Failed(Contract.Verbs.Single(v => v.Name == "read"), new Kernel.Error("scratch-server.missing", "No SQL Server answers for copies.", "Run ci/sql.sh up.")));
        AssertValid("estate.read.1.schema.json", answer);
        AssertValid("estate.envelope.1.schema.json", answer);

        var pattern = new Regex(ErrorCode.Pattern, RegexOptions.CultureInvariant);
        var word = Gen.Char["a0z9"].Array[1, 3].Select(cs => new string(cs)).Array[1, 2].Select(pieces => string.Join('-', pieces));
        var code = word.Array[2, 3].Select(words => string.Join('.', words));
        var nearMiss = Gen.Select(code, Gen.Int[0, 12], Gen.Char[".-A \n_"]).Select((text, at, mark) => text.Insert(at % (text.Length + 1), mark.ToString()));
        Gen.OneOf(code, nearMiss).Sample(text => pattern.IsMatch(text) == Evaluate("estate.envelope.1.schema.json", Answer(Finds(0, "note", code: text))).IsValid);
        Assert.Throws<ArgumentException>(() => new Kernel.Error("nowhere.failed", "Failed.", "Do the other thing."));
    }

    /// <summary>
    /// Decision 2.25 on the alignment review's reproduction (ARCH-03): a package whose table has a column named by one space and
    /// one whose name holds a tab, both of which DacFx builds, is read whole at exit 0. The JSON holds the key with the space as it
    /// is and the tab as JSON's own escape; a name is refused for its length alone.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Reading_a_package_with_a_column_named_by_a_space_and_one_holding_a_tab_answers_exit_0_naming_both()
    {
        Telemetry.OptOut();   // before DacFx loads, as estate's Main does
        var scratch = Directory.CreateTempSubdirectory("estate-blank-name-").FullName;
        try
        {
            var (dacpac, bare) = (Path.Combine(scratch, "blank.dacpac"), Path.Combine(scratch, "bare.dacpac"));
            Package(dacpac, "CREATE TABLE dbo.Customer (Id INT NOT NULL, [ ] INT NULL, [a\tb] INT NULL);");
            Package(bare, "CREATE TABLE dbo.Customer (Id INT NOT NULL);");

            var (exit, answer) = Answered(["read", "--from", "dacpac:" + dacpac, "--json"], new Checkout(scratch, scratch, null));
            using var diff = new MemoryStream();
            var diffExit = Cli.Program.Run(["diff", "--from", "dacpac:" + bare, "--to", "dacpac:" + dacpac], diff, new Checkout(scratch, scratch, null));

            Assert.Equal(0, exit);
            AssertValid("estate.read.1.schema.json", answer);
            var keys = Elements(answer, scratch).Select(e => (string)e!["key"]!).ToList();
            Assert.Contains("Column [dbo].[Customer].[ ]", keys);
            Assert.Contains("Column [dbo].[Customer].[a\tb]", keys);
            Assert.Contains("\"Column [dbo].[Customer].[a\\tb]\"", Io.Json.Text(answer), StringComparison.Ordinal);
            Assert.Equal(0, diffExit);
            var lines = Encoding.UTF8.GetString(diff.ToArray()).Split('\n');
            Assert.Contains("created Column [dbo].[Customer].[ ]", lines);
            Assert.Contains("created Column [dbo].[Customer].[a\\u0009b]", lines);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>
    /// Decision 2.25's other half (C3): what leaves the tool as Markdown is escaped. A finding whose subject holds ESC and a
    /// right-to-left override (U+202E) and whose message holds a line feed and a lone surrogate renders as one line holding
    /// \u001B, ‮, \u000A and \uD800 and none of the raw characters, written through io/Write without throwing on the
    /// surrogate; an accented letter and an arrow print as they are.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Markdown_escapes_each_control_bidirectional_and_lone_surrogate_character_and_prints_the_rest_as_it_is()
    {
        var read = Contract.Verbs.Single(v => v.Name == "read");
        var answer = Contract.Answer(read.Output, read.Outcome("done"), 0, "Length 300 → 256 for café.",
            [Finding.Warning("drift.column", "Column [dbo].[Customer].[a\u001Bb‮c]", "line one\nline two \uD800 end", "Run estate diff\u0009now.")]);

        using var output = new MemoryStream();
        Write.Text(output, Render.Markdown(answer));
        var markdown = Encoding.UTF8.GetString(output.ToArray());

        var line = Assert.Single(markdown.Split('\n'), l => l.StartsWith("- warning", StringComparison.Ordinal));
        Assert.Contains("[a\\u001Bb\\u202Ec]", line, StringComparison.Ordinal);
        Assert.Contains("line one\\u000Aline two \\uD800 end", line, StringComparison.Ordinal);
        Assert.Contains("Remedy: Run estate diff\\u0009now.", line, StringComparison.Ordinal);
        Assert.StartsWith("Length 300 → 256 for café.\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(markdown, c => c is '\u001B' or '‮' or '\uD800' or '\t');
    }

    /// <summary>JSON escapes a bidirectional control as ‮, which System.Text.Json's own encoder writes raw, and a parser restores the character.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Json_escapes_a_bidirectional_control_and_a_parser_restores_it()
    {
        var read = Contract.Verbs.Single(v => v.Name == "read");
        var answer = Contract.Answer(read.Output, read.Outcome("done"), 0, "dacpac:a‮b.dacpac: 3 elements", []);

        var text = Render.JsonText(Render.Json(answer));

        Assert.Contains("\\u202E", text, StringComparison.Ordinal);
        Assert.DoesNotContain('‮', text);
        Assert.Equal("dacpac:a‮b.dacpac: 3 elements", (string?)JsonNode.Parse(text)!["message"]);
    }

    /// <summary>A package DacFx builds from the scripts, under the path given.</summary>
    private static void Package(string dacpac, params string[] scripts)
    {
        using var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160, new Microsoft.SqlServer.Dac.Model.TSqlModelOptions());
        foreach (var script in scripts)
        {
            model.AddObjects(script);
        }

        Microsoft.SqlServer.Dac.DacPackageExtensions.BuildPackage(dacpac, model, new Microsoft.SqlServer.Dac.PackageMetadata());
    }

    /// <summary>The elements a read's answer holds: in the answer itself, or in the run's answer.json when the answer was cut to its first entries.</summary>
    private static JsonArray Elements(JsonNode answer, string root) =>
        ((string?)answer["full"] is { } full ? JsonNode.Parse(File.ReadAllText(Path.Combine(root, full)))! : answer)["read"]!["elements"]!.AsArray();

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
                "{ \"environments\": { \"dev\": { \"host\": \"192.0.2.10\", \"connection\": \"file:dev.connection\", \"profile\": \"estate/profiles/pipeline.publish.xml\" } } }");
            File.WriteAllText(Path.Combine(root, ".estate", "copies.json"),
                "{ \"copies\": [ { \"name\": \"estate_host_1_0a1b2c3d\", \"server\": \"localhost,11433\", \"host\": \"host\", \"pid\": 1, \"created\": \"2026-09-25T00:00:00Z\" } ] }");
            var check = Contract.Verbs.Single(v => v.Name == "check") with
            {
                Body = (here, _) =>
                {
                    SqlServer.Target(target, "--target").Bind(parsed => SqlServer.Resolve(parsed, here.Root));
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

    /// <summary>
    /// §4 row 15, through the contract's own types: an answer at exit 3 names what blocked it, the data-loss check or a constraint
    /// violation, written as data-loss-check and constraint-violation; one at exit 3 naming nothing, or at another exit naming
    /// something, cannot be constructed, and the schema refuses the same answers written by hand.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(BlockedBy.DataLossCheck, "data-loss-check")]
    [InlineData(BlockedBy.ConstraintViolation, "constraint-violation")]
    public void A_blocking_answer_names_what_blocked_it_exactly_at_exit_3(BlockedBy blockedBy, string written)
    {
        var blocked = new Outcome("blocked", [3], "the data blocked the change");
        var answer = new Envelope("estate.prove/1", blocked, 3, "Msg 50000: rows were detected.", [], blockedBy);

        var json = Render.Json(answer);

        Assert.Equal(written, (string?)json["blockedBy"]);
        AssertValid("estate.envelope.1.schema.json", json);
        Assert.Throws<ArgumentException>("blockedBy", () => new Envelope("estate.prove/1", blocked, 3, "Msg 50000: rows were detected.", []));
        Assert.Throws<ArgumentException>("blockedBy", () => new Envelope("estate.version/1", new Outcome("done", [0], "done"), 0, "estate 3.0.0", [], blockedBy));
        json["blockedBy"] = null;
        Assert.False(Evaluate("estate.envelope.1.schema.json", json).IsValid, "the schema admits exit 3 naming nothing");
    }

    /// <summary>An outcome's word and its exit are one decision: check drift's matches is exit 0 and differs exit 5, and an answer that pairs them otherwise cannot be constructed.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_answer_whose_exit_its_outcome_does_not_list_cannot_be_constructed()
    {
        var check = Contract.Verbs.Single(v => v.Name == "check");

        Assert.Throws<ArgumentException>("exit", () => new Envelope(check.Output, check.Outcome("matches"), 5, "env:dev matches ref:main.", []));
        Assert.Equal(5, new Envelope(check.Output, check.Outcome("differs"), 5, "env:dev differs from ref:main.", []).Exit);
        Assert.Equal([0, 5], Contract.Verbs.Single(v => v.Name == "diff").Outcome("differs").Exits);
        Assert.Throws<InvalidOperationException>(() => check.Outcome("done"));
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
            var checks = Doctor.Examine(new Doctor.Machine(bare, null, bare, null, Path.Combine(bare, "no-sql.env"), Environment.Version), (c, _) => new Ran.NotFound(c.Program, "not installed"), Contract.Version);

            var json = Render.Json(Verbs.Doctor(checks, Doctor.Toolchain(bare, Contract.Version)));

            AssertValid("estate.doctor.1.schema.json", json);
            Assert.Equal((6, "degraded"), ((int)json["exit"]!, (string?)json["outcome"]));
            var line = (string)json["message"]!;
            Assert.StartsWith("estate doctor DEGRADED | sdk=", line, StringComparison.Ordinal);
            Assert.Contains(" | dacfx=" + Doctor.DacFx + " (UNPINNED) | ", line, StringComparison.Ordinal);
            Assert.DoesNotContain("M1", line, StringComparison.Ordinal);
            var findings = json["findings"]!.AsArray().Select(f => ((string)f!["code"]!, (string)f["severity"]!, (string?)f["remedy"])).ToList();
            Assert.Equal(["doctor.sdk", "doctor.tool", "doctor.build", "doctor.git", "doctor.scratch-server", "doctor.lfs"], findings.Select(f => f.Item1));
            Assert.Equal(checks.Where(c => c.Remedy is not null).Select(c => c.Remedy), findings.Select(f => f.Item3));
            Assert.All(findings, f => Assert.Equal("error", f.Item2));
            Assert.Equal(checks.Select(c => c.Item.Name), json["checks"]!.AsArray().Select(c => (string)c!["item"]!));
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
            File.WriteAllText(Path.Combine(machine, "sql.env"), "MSSQL_SA_PASSWORD=x\nESTATE_SQL_PORT=11433\n");
            Runner answers = (command, _) => (command.Program + " " + command.Arguments[0]) switch
            {
                "dotnet --list-sdks" => new Ran.Exited(0, "10.0.402 [x]\n", ""),
                "docker info" => new Ran.Exited(0, "29.5.3\n", ""),
                "docker image" => new Ran.Exited(0, "sha256:5b09\n", ""),
                "docker container" => new Ran.Exited(0, Doctor.SqlServerImage + "\n", ""),
                "git --version" => new Ran.Exited(0, "git version 2.31.1.windows.1\n", ""),
                "git lfs" => new Ran.Exited(0, "git-lfs/3.4.0 (GitHub; windows amd64; go 1.21.1)\n", ""),
                _ => new Ran.NotFound(command.Program, "not installed"),
            };

            var answer = Verbs.Doctor(Doctor.Examine(new Doctor.Machine(machine, null, machine, null, Path.Combine(machine, "sql.env"), Environment.Version), answers, Contract.Version), Doctor.Toolchain(machine, Contract.Version));

            var json = Render.Json(answer);
            AssertValid("estate.doctor.1.schema.json", json);
            Assert.Equal((0, "ready"), (answer.Exit, answer.Outcome.Word));
            Assert.Equal("estate doctor READY | sdk=10.0.402 | runtime=" + Environment.Version + " | tool=published | dacfx=" + Doctor.DacFx + " (UNPINNED) | build=dotnet with the tool folder's targets"
                + " | git=2.31.1 | scratch-server=estate-sql container (localhost,11433) | image=present | lfs=git-lfs/3.4.0", answer.Message);
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
    /// The envelope's rules, each named by the sentence it pins, as (admitted, refused). The exit codes, the outcome words and the
    /// field names are written out rather than read from the contract, so a rule narrowed there fails here.
    /// </summary>
    private static readonly Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)> EnvelopePairs = Pairs();

    private static Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)> Pairs()
    {
        var pairs = new Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Failed)>(StringComparer.Ordinal)
        {
            ["exit is a code of the frozen table"] = (WithOutcome("build-failed", 7), a => a["exit"] = 8),
            ["outcome is a word of a verb's set or a failure exit's name"] = (WithOutcome("matches", 0), WithOutcome("converged", 0)),
            ["outcome ties to its exits: a failure's name at its exit alone"] = (WithOutcome("unreachable", 4), WithOutcome("unreachable", 6)),
            ["outcome ties to its exits: differs at 0 or 5 and at no other"] = (WithOutcome("differs", 5), WithOutcome("differs", 6)),
            ["outcome ties to its exits: matches at 0 alone"] = (WithOutcome("matches", 0), WithOutcome("matches", 5)),
            ["outcome ties to its exits: ready at 0 and degraded at 6"] = (WithOutcome("degraded", 6), WithOutcome("ready", 6)),
            ["the message is present"] = (a => a["message"] = "estate 3.0.0", a => a["message"] = ""),
            ["exit 3 names what blocked it"] = (WithBlocked("data-loss-check"), WithBlocked(null)),
            ["what blocked it is the data-loss check or a constraint violation"] = (WithBlocked("constraint-violation"), WithBlocked("guard")),
            ["no other exit names what blocked it"] = (_ => { }, a => a["blockedBy"] = "data-loss-check"),
            ["an answer says what blocked it, as null when the data did not block"] = (_ => { }, a => a.Remove("blockedBy")),
            ["a finding's severity is error, warning or note"] = (Finds(1, "note", remedy: null), Finds(1, "warn", remedy: null)),
            ["a finding of severity error carries a remedy"] = (Finds(1, "warning", remedy: null), Finds(1, "error", remedy: null)),
            ["a finding's code is in the one code pattern"] = (Finds(1, "note", code: "scratch-server.missing"), Finds(1, "note", code: "Scratch-Server.missing")),
            ["an answer that names its whole file was cut"] = (WithCut(true, ".estate/runs/20260925T101502Z-4242-0a1b/answer.json"), WithCut(false, ".estate/runs/20260925T101502Z-4242-0a1b/answer.json")),
            ["an answer that was not cut names no file"] = (WithCut(false, null), WithCut(true, ".estate/runs/x/queries.log")),
            ["the whole file is the run's answer.json"] = (WithCut(true, ".estate/runs/20260925T101502Z-4242-0a1b/answer.json"), WithCut(true, "answer.json")),
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

    /// <summary>An answer whose outcome reads as <paramref name="word"/> at <paramref name="exit"/>, with a finding carrying a remedy where the exit requires one.</summary>
    private static Action<JsonObject> WithOutcome(string word, int exit) => answer =>
    {
        Finds(exit, exit is 2 or 4 or 6 or 9 ? "error" : null, "estate doctor")(answer);
        answer["outcome"] = word;
    };

    /// <summary>Blocked by the data: exit 3, naming what blocked it, or naming nothing.</summary>
    private static Action<JsonObject> WithBlocked(string? blockedBy) => answer =>
    {
        answer["outcome"] = "blocked";
        answer["exit"] = 3;
        answer["blockedBy"] = blockedBy;
    };

    /// <summary>An answer cut or not, naming its whole file or none.</summary>
    private static Action<JsonObject> WithCut(bool truncated, string? full) => answer =>
    {
        answer["truncated"] = truncated;
        answer["full"] = full;
    };

    /// <summary>An answer at <paramref name="exit"/> with one finding of <paramref name="severity"/> and <paramref name="code"/>, or with none when the severity is null; the outcome follows the exit.</summary>
    private static Action<JsonObject> Finds(int exit, string? severity, string? remedy = null, string code = "data-loss-check.rows-present") => answer =>
    {
        answer["exit"] = exit;
        answer["outcome"] = exit switch { 0 => "done", 1 => "bad-arguments", 2 => "unparsed-input", 4 => "unreachable", 6 => "configuration-refused", 7 => "build-failed", 9 => "refused-by-name", var other => (string?)answer["outcome"] ?? "done" };
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
