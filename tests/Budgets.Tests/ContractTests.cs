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
using DbChange.Cli;
using DbChange.Io;
using DbChange.Kernel;
using DbChange.Tests;
using Json.Schema;
using Xunit;
using Outcome = DbChange.Cli.Outcome;

namespace DbChange.Budgets.Tests;

/// <summary>The CLI contract: the verb table, the envelope and the frozen exit table, checked against cli/schemas/.</summary>
public sealed partial class ContractTests
{
    public static TheoryData<string> Answers => new(Contract.Verbs.Select(v => v.Name).Concat(["no-such-verb", ""]));

    /// <summary>
    /// The help as the built dbchange prints it validates against its schema and says which verbs this build has: the four of M1,
    /// synthetic-copy and --version.
    /// </summary>
    [Fact]
    [Trait("Category", "build")]
    public void Help_json_validates_against_its_committed_schema_and_says_which_verbs_this_build_has()
    {
        var (exit, output) = Run("--help", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain('\r', output);
        var help = JsonNode.Parse(output)!;
        VerbAnswer.Valid("dbchange.help.1.schema.json", help);
        Assert.Equal(["doctor", "read", "diff", "synthetic-copy", "check", "--version"],
            help["verbs"]!.AsArray().Where(v => (bool)v!["built"]!).Select(v => (string)v!["name"]!));
        help["exits"]![0]!["code"] = 10;
        Assert.False(VerbAnswer.Evaluate("dbchange.help.1.schema.json", help).IsValid,
            "the schema admits an exit code the table does not have");
    }

    /// <summary>
    /// The help names each outcome a verb answers with, at its exit, since the bundle and cli/VERBS.md are generated from it (AGENTS.md):
    /// each kind of answer synthetic-copy up gives, built and current at exit 0, differs at exit 5 and needs-input at exit 8, takes an
    /// outcome the help lists for synthetic-copy at that exit, and validates against the verb's schema.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(Io.SyntheticCopy.Kind.Built, "built", 0)]
    [InlineData(Io.SyntheticCopy.Kind.Current, "current", 0)]
    [InlineData(Io.SyntheticCopy.Kind.Differs, "differs", 5)]
    [InlineData(Io.SyntheticCopy.Kind.NeedsInput, "needs-input", 8)]
    public void Each_answer_synthetic_copy_up_gives_takes_an_outcome_its_help_lists_at_its_exit(Io.SyntheticCopy.Kind kind, string word,
        int exit)
    {
        Finding[] findings = kind switch
        {
            Io.SyntheticCopy.Kind.Differs => [Finding.Warning("synthetic-copy.identity", "Table [dbo].[Order]",
                "The plan from the base to the copy would alter Table [dbo].[Order].", "Run dbchange check drift --target synthetic-copy.")],
            Io.SyntheticCopy.Kind.NeedsInput => [Finding.Error("witness.acceptance", "NotNull on [dbo].[Order] ([Total])",
                "The copy answers NotNull on [dbo].[Order] ([Total]) populated and clean.", "Plant a row that breaks it.")],
            _ => [],
        };
        var answer = Verbs.Copied(new Io.SyntheticCopy.Answer(kind, CopyName.Synthetic(Fingerprint.Of("a base"), null),
            Expect.Value(GitRef.Of("--at", "main")), new string('a', 40), null, false, [], [], [], findings, []),
            new Stamp(Expect.Value(DacFx.Version)));
        var listed = Render.Help()["verbs"]!.AsArray().Single(v => (string?)v!["name"] == "synthetic-copy")!["outcomes"]!.AsArray();

        Assert.Equal((word, exit), (answer.Outcome.Word, answer.Exit));
        Assert.Contains(listed, o => (string?)o!["outcome"] == word && o["exits"]!.AsArray().Any(e => (int)e! == exit));
        VerbAnswer.Valid("dbchange.synthetic-copy.1.schema.json", Render.Json(answer));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_committed_schemas_are_what_the_contract_generates()
    {
        var directory = Path.Combine(Repository.Root, "cli", "schemas");
        var generated = Render.Schemas().ToDictionary(s => Render.SchemaFile(s.Id), s => Io.Json.Text(s.Schema));
        if (Environment.GetEnvironmentVariable("DBCHANGE_BLESS") == "1")
        {
            foreach (var (file, text) in generated)
            {
                Write.Text(Path.Combine(directory, file), text);
            }
        }

        Assert.Equal(generated.Keys.Order(StringComparer.Ordinal),
            Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var (file, text) in generated)
        {
            Assert.True(text == File.ReadAllText(Path.Combine(directory, file)),
                $"cli/schemas/{file} is stale: regenerate with DBCHANGE_BLESS=1 dotnet test --filter Category=fast");
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
    /// kernel's closed ErrorCategory with no discard arm, so a member without an arm fails the build. The codes are Register.ErrorPaths',
    /// which Register.Errors holds to every code the three packages construct, composed ones included: a member no path constructs
    /// fails here, as does a path whose code names a word no member writes as, and every arm names a code of the frozen exit table.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S2")]
    [Trait("Value", "O4")]
    public void Every_error_category_has_an_exit_of_the_frozen_table_and_is_constructed_by_some_path()
    {
        var members = Enum.GetValues<ErrorCategory>();
        var constructed = Register.ErrorPaths.All.Select(c => c.Code.Split('.')[0]).Distinct().Order(StringComparer.Ordinal).ToList();

        Assert.Equal(members.Select(ErrorCode.Text).Order(StringComparer.Ordinal), constructed);
        Assert.All(members, m => Assert.Contains(Contract.ExitByCategory(m), Contract.Exits.Select(e => e.Code)));
    }

    /// <summary>
    /// Finding X-1: the committed schemas carry the kernel's one code pattern as a finding's code, so an error whose category is
    /// hyphenated, as local-server is, answers with an envelope that validates against the envelope's schema and its verb's; and over
    /// generated codes and their near misses (a capital, a space, a line break, a doubled or stray dot or hyphen), the kernel's pattern
    /// admits a code exactly when the envelope's schema admits it as a finding's. A code the pattern admits is still refused by the kernel
    /// when its category names no member.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_schemas_admit_exactly_the_codes_the_kernel_s_pattern_admits_a_hyphenated_category_included()
    {
        var answer = Render.Json(
            Contract.Failed(Contract.Verbs.Single(v => v.Name == "read"),
            new Kernel.Error("local-server.missing", "No SQL Server answers for copies.", "Run ci/sql.sh up.")));
        VerbAnswer.Valid("dbchange.read.1.schema.json", answer);
        VerbAnswer.Valid("dbchange.envelope.1.schema.json", answer);

        var pattern = new Regex(ErrorCode.Pattern, RegexOptions.CultureInvariant);
        var word = Gen.Char["a0z9"].Array[1, 3].Select(cs => new string(cs)).Array[1, 2].Select(pieces => string.Join('-', pieces));
        var code = word.Array[2, 3].Select(words => string.Join('.', words));
        var nearMiss = Gen.Select(code, Gen.Int[0, 12], Gen.Char[".-A \n_"])
            .Select((text, at, mark) => text.Insert(at % (text.Length + 1), mark.ToString()));
        Gen.OneOf(code, nearMiss)
            .Sample(text => pattern.IsMatch(text) == VerbAnswer.Evaluate("dbchange.envelope.1.schema.json",
            Answer(Finds(0, "note", code: text))).IsValid);
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
        using var scratch = ScratchFolder.Temporary("blank-name");
        var (dacpac, bare) = (scratch.Under("blank.dacpac"), scratch.Under("bare.dacpac"));
        Package(dacpac, "CREATE TABLE dbo.Customer (Id INT NOT NULL, [ ] INT NULL, [a\tb] INT NULL);");
        Package(bare, "CREATE TABLE dbo.Customer (Id INT NOT NULL);");

        var (exit, answer) = Answered(["read", "--from", "dacpac:" + dacpac, "--json"],
            new Checkout(scratch.Path, scratch.Path, null, Cli.Contract.Version));
        using var diff = new MemoryStream();
        var diffExit = Cli.Program.Run(["diff", "--from", "dacpac:" + bare, "--to", "dacpac:" + dacpac], diff,
            new Checkout(scratch.Path, scratch.Path, null, Cli.Contract.Version));

        Assert.Equal(0, exit);
        VerbAnswer.Valid("dbchange.read.1.schema.json", answer);
        var keys = Elements(answer, scratch.Path).Select(e => (string)e!["key"]!).ToList();
        Assert.Contains("Column [dbo].[Customer].[ ]", keys);
        Assert.Contains("Column [dbo].[Customer].[a\tb]", keys);
        Assert.Contains("\"Column [dbo].[Customer].[a\\tb]\"", Io.Json.Text(answer), StringComparison.Ordinal);
        Assert.Equal(0, diffExit);
        var lines = Encoding.UTF8.GetString(diff.ToArray()).Split('\n');
        Assert.Contains("created Column [dbo].[Customer].[ ]", lines);
        Assert.Contains("created Column [dbo].[Customer].[a\\u0009b]", lines);
    }

    /// <summary>
    /// S26 of the pre-M2 review, VALUES.md D2 at its mechanism: a command runs under the invariant culture whatever the caller's thread
    /// culture is, and the caller's is as it was once the command ends; Main also sets the invariant culture for every thread a command
    /// starts.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "D2")]
    public void A_command_runs_under_the_invariant_culture_and_leaves_the_caller_s_as_it_was()
    {
        var (during, was) = ((CultureInfo?)null, CultureInfo.CurrentCulture);
        var doctor = Contract.Verbs.Single(v => v.Name == "doctor");
        var probe = doctor with
        {
            Body = (_, _, _) =>
            {
                during = CultureInfo.CurrentCulture;
                return Contract.Answer(doctor.Output, doctor.Outcome("ready"), 0, "ready", []);
            },
        };
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            using var output = new MemoryStream();
            Cli.Program.Run(["doctor", "--json"], output, () => new Checkout(Repository.Root, Repository.Root, null, Contract.Version),
                [probe]);

            Assert.Equal(CultureInfo.InvariantCulture, during);
            Assert.Equal("tr-TR", CultureInfo.CurrentCulture.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    /// <summary>
    /// VALUES.md D2 at the answer: read of a package and diff of two, run in this process under tr-TR, de-DE and the invariant culture,
    /// write the same bytes. The package holds a decimal default, a date, and a column named ilk, whose upper case differs under Turkish
    /// rules.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "D2")]
    public void An_answer_is_the_same_bytes_under_tr_TR_de_DE_and_the_invariant_culture()
    {
        using var scratch = ScratchFolder.Temporary("cultures");
        var (before, after) = (scratch.Under("before.dacpac"), scratch.Under("after.dacpac"));
        Package(before, "CREATE TABLE dbo.Invoice (Id INT NOT NULL, Price DECIMAL(10, 2) NOT NULL DEFAULT 1.5, Issued DATE NULL, [ilk] INT "
            + "NULL);");
        Package(after, "CREATE TABLE dbo.Invoice (Id INT NOT NULL, Price DECIMAL(12, 3) NOT NULL DEFAULT 1.5, Issued DATE NULL, [ILK] INT "
            + "NULL, Total DECIMAL(10, 2) NULL);");
        var answers = new List<(string Culture, byte[] Read, byte[] Diff)>();
        foreach (var culture in (CultureInfo[])[CultureInfo.InvariantCulture, new("tr-TR"), new("de-DE")])
        {
            var (was, wasUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, culture);
            try
            {
                using var read = new MemoryStream();
                using var diff = new MemoryStream();
                Assert.Equal(0, Cli.Program.Run(["read", "--from", "dacpac:" + before, "--json"], read,
                    new Checkout(scratch.Path, scratch.Path, null, Cli.Contract.Version)));
                Assert.Equal(0, Cli.Program.Run(["diff", "--from", "dacpac:" + before, "--to", "dacpac:" + after, "--json"], diff,
                    new Checkout(scratch.Path, scratch.Path, null, Cli.Contract.Version)));
                answers.Add((culture.Name, Timeless(read.ToArray()), Timeless(diff.ToArray())));
            }
            finally
            {
                (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (was, wasUi);
            }
        }

        Assert.All(answers, a =>
            Assert.True(a.Read.SequenceEqual(answers[0].Read) && a.Diff.SequenceEqual(answers[0].Diff), "the answer under " + a.Culture
            + " differs from the invariant culture's"));
        var diffed = Encoding.UTF8.GetString(answers[0].Diff);
        Assert.Contains("[ilk]", diffed, StringComparison.Ordinal);
        Assert.Contains("[Total]", diffed, StringComparison.Ordinal);
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
            [Finding.Warning("drift.column", "Column [dbo].[Customer].[a\u001Bb‮c]", "line one\nline two \uD800 end",
                "Run dbchange diff\u0009now.")]);

        using var output = new MemoryStream();
        Write.Text(output, Render.Markdown(answer));
        var markdown = Encoding.UTF8.GetString(output.ToArray());

        var line = Assert.Single(markdown.Split('\n'), l => l.StartsWith("- warning", StringComparison.Ordinal));
        Assert.Contains("[a\\u001Bb\\u202Ec]", line, StringComparison.Ordinal);
        Assert.Contains("line one\\u000Aline two \\uD800 end", line, StringComparison.Ordinal);
        Assert.Contains("Remedy: Run dbchange diff\\u0009now.", line, StringComparison.Ordinal);
        Assert.StartsWith("Length 300 → 256 for café.\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(markdown, c => c is '\u001B' or '‮' or '\uD800' or '\t');
    }

    /// <summary>
    /// JSON escapes a bidirectional control as ‮, which System.Text.Json's own encoder writes raw, and a parser restores the character.
    /// </summary>
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
        using var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160,
            new Microsoft.SqlServer.Dac.Model.TSqlModelOptions());
        foreach (var script in scripts)
        {
            model.AddObjects(script);
        }

        Microsoft.SqlServer.Dac.DacPackageExtensions.BuildPackage(dacpac, model, new Microsoft.SqlServer.Dac.PackageMetadata());
    }

    /// <summary>
    /// The elements a read's answer holds: in the answer itself, or in the run's answer.json when the answer was cut to its first entries.
    /// </summary>
    private static JsonArray Elements(JsonNode answer, string root) =>
        ((string?)answer["full"] is { } full ? JsonNode.Parse(File.ReadAllText(Path.Combine(root, full)))!
            : answer)["read"]!["elements"]!.AsArray();

    /// <summary>
    /// An exception no verb expected answers with an envelope: exit 6, one finding internal.unexpected naming the exception's type, and its
    /// message kept when the command read no named environment. A checkout with no working directory makes read throw
    /// ArgumentNullException.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S2")]
    public void An_unexpected_exception_answers_exit_6_with_a_finding_naming_its_type_and_its_message()
    {
        var (exit, answer) = Answered(["read", "--from", "dacpac:none.dacpac", "--json"],
            new Checkout(Repository.Root, null!, null, Cli.Contract.Version));

        Assert.Equal(6, exit);
        VerbAnswer.Valid("dbchange.read.1.schema.json", answer);
        var finding = Assert.Single(answer["findings"]!.AsArray())!;
        Assert.Equal(("internal.unexpected", "error"), ((string)finding["code"]!, (string)finding["severity"]!));
        Assert.Contains("ArgumentNullException: Value cannot be null.", (string)finding["message"]!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The withholding rule is keyed to what the run read, not to how the arguments are spelled: check drift naming env:dev or a copy, in a
    /// checkout with no root, throws ArgumentNullException before it reads any environment's connection, so the message is dbchange's own
    /// and is kept.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env:dev")]
    [InlineData("copy:dbchange_host_1_0a1b2c3d")]
    public void An_unexpected_exception_keeps_its_message_when_the_command_names_a_database_but_read_no_environment(string target)
    {
        var (exit, answer) = Answered(["check", "drift", "--target", target, "--at", "main", "--json"],
            new Checkout(null!, Repository.Root, null, Cli.Contract.Version));

        Assert.Equal(6, exit);
        VerbAnswer.Valid("dbchange.check.1.schema.json", answer);
        var message = (string)Assert.Single(answer["findings"]!.AsArray())!["message"]!;
        Assert.Contains("ArgumentNullException: Value cannot be null.", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// VALUES.md X2 at the top-level catch, for M2's predict and M6's check environments, which read environments without an env: argument:
    /// a verb that resolves env:dev (through EnvironmentDatabase.Of) or a copy (through R15's read of dev's connection in io/LocalServer)
    /// and then throws has its exception's message withheld, its type alone printed; one that resolves a git ref reads no environment and
    /// keeps the message.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    [InlineData("env:dev", true)]
    [InlineData("copy:dbchange_host_1_0a1b2c3d", true)]
    [InlineData("ref:main", false)]
    public void An_unexpected_exception_withholds_its_message_when_the_run_read_a_named_environment_with_no_env_argument(string target,
        bool withheld)
    {
        var planted = PlantedValue.Unique();
        using var root = ScratchFolder.Temporary("withheld");
        root.File("dev.connection", "Server=tcp:192.0.2.10,1433;Initial Catalog=Estate;User ID=estate;Password="
            + PlantedValue.PasswordText);
        EnvironmentsJson.Dev("file:dev.connection", "192.0.2.10").WriteTo(root.Path);
        root.File(".dbchange/copies.json",
            "{ \"copies\": [ { \"name\": \"dbchange_host_1_0a1b2c3d\", \"server\": \"localhost,11433\", \"host\": \"host\", \"pid\": 1, "
            + "\"created\": \"2026-09-25T00:00:00Z\" } ] }");
        var check = Contract.Verbs.Single(v => v.Name == "check") with
        {
            Body = (here, _, _) =>
            {
                SqlServer.Target(target, "--target").Bind(parsed => SqlServer.Resolve(parsed, here.Root));
                throw new InvalidOperationException("Server=tcp:192.0.2.10,1433;Password=" + planted);
            },
        };

        using var output = new MemoryStream();
        var exit = Cli.Program.Run(["check", "environments", "--json"], output, () =>
            new Checkout(root.Path, root.Path, null, Cli.Contract.Version), [check]);
        var answer = JsonNode.Parse(output.ToArray())!;

        Assert.Equal(6, exit);
        VerbAnswer.Valid("dbchange.check.1.schema.json", answer);
        var message = (string)Assert.Single(answer["findings"]!.AsArray())!["message"]!;
        Assert.Contains("InvalidOperationException", message, StringComparison.Ordinal);
        Assert.Equal(withheld, !answer.ToJsonString().Contains(planted.Text, StringComparison.Ordinal));
        Assert.Equal(withheld, message.Contains("withheld", StringComparison.Ordinal));
        if (withheld)
        {
            planted.AbsentFrom(answer.ToJsonString());
        }
    }

    /// <summary>
    /// The catch holds the whole command, not the verb's body alone: the checkout failing as Directory.GetCurrentDirectory does once the
    /// working directory is removed (a swept .dbchange/worktrees/&lt;commit&gt;/) answers internal.unexpected at exit 6 with read's schema.
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
        VerbAnswer.Valid("dbchange.read.1.schema.json", answer);
        var finding = Assert.Single(answer["findings"]!.AsArray())!;
        Assert.Equal("internal.unexpected", (string)finding["code"]!);
        Assert.Contains("FileNotFoundException: The working directory was removed.", (string)finding["message"]!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Writing the answer is inside the catch too: standard output refusing the first write gets the internal.unexpected answer naming the
    /// IOException; refusing every write gets nothing, and dbchange still exits 6 rather than the verb's exit or an unhandled exception.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Standard_output_refusing_the_answer_exits_6(int refusedWrites)
    {
        using var output = new RefusingStream(refusedWrites);
        var exit = Cli.Program.Run(["no-such-verb", "--json"], output, () =>
            new Checkout(Repository.Root, Repository.Root, null, Cli.Contract.Version), Contract.Verbs);

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

    /// <summary>
    /// A stream that throws IOException on its first <paramref name="refused"/> writes, as standard output does once its reader has gone,
    /// and keeps what it accepts after.
    /// </summary>
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

    /// <summary>dbchange run in this process for the checkout given: its exit and its --json answer.</summary>
    private static (int Exit, JsonNode Answer) Answered(string[] arguments, Checkout here)
    {
        using var output = new MemoryStream();
        var exit = Cli.Program.Run(arguments, output, here);
        return (exit, JsonNode.Parse(output.ToArray())!);
    }

    [Theory]
    [Trait("Category", "build")]
    [MemberData(nameof(Answers))]
    public void Every_verb_answers_with_an_envelope_that_validates(string verb)
    {
        var (exit, output) = Run(verb, "--json");

        var envelope = JsonNode.Parse(output)!;
        VerbAnswer.Valid("dbchange.envelope.1.schema.json", envelope);
        Assert.Equal(exit, (int)envelope["exit"]!);
        if (Contract.Verbs.SingleOrDefault(v => v.Name == verb) is { Content: not null } built)
        {
            VerbAnswer.Valid(Render.SchemaFile(built.Output), envelope);   // its own schema too, whatever the exit
        }
    }

    /// <summary>
    /// WP 1.7: each verb this build has that adds to the envelope writes its own schema, the envelope and what the verb adds, committed
    /// under cli/schemas/.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_built_verb_has_its_own_schema_and_its_error_answer_validates_against_it()
    {
        var built = Contract.Verbs.Where(v => v.Built && v.Content is not null).ToList();

        Assert.NotEmpty(built);
        foreach (var verb in built)
        {
            Assert.True(File.Exists(Path.Combine(Repository.Root, "cli", "schemas", Render.SchemaFile(verb.Output))), verb.Output
                + " has no schema under cli/schemas/");
            using var output = new MemoryStream();
            var exit = Cli.Program.Run([verb.Name, "--no-such-flag", "--json"], output,
                new Checkout(Repository.Root, Repository.Root, null, Cli.Contract.Version));
            var answer = JsonNode.Parse(output.ToArray())!;
            Assert.Equal(1, exit);
            VerbAnswer.Valid(Render.SchemaFile(verb.Output), answer);
            Assert.All(verb.Content!, added => Assert.Null(answer[added.Key]));
            answer[verb.Content!.First().Key] = new JsonObject();
            Assert.False(VerbAnswer.Evaluate(Render.SchemaFile(verb.Output), answer).IsValid, verb.Output
                + " admits what the verb adds in a shape it never writes");
        }
    }

    /// <summary>
    /// A verb the contract names and this build has no body for, and a check this build lacks, answer verb.not-built at exit 6 naming the
    /// verb and no milestone.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "S2")]
    [InlineData("predict")]
    [InlineData("check outsystems")]
    [InlineData("check cdc")]
    public void A_verb_this_build_does_not_have_answers_exit_6_with_verb_not_built_naming_the_verb(string verb)
    {
        var (exit, answer) = Answered([.. verb.Split(' '), "--json"],
            new Checkout(Repository.Root, Repository.Root, null, Cli.Contract.Version));

        Assert.Equal(6, exit);
        VerbAnswer.Valid("dbchange.envelope.1.schema.json", answer);
        var finding = Assert.Single(answer["findings"]!.AsArray())!;
        Assert.Equal(("verb.not-built", "dbchange " + verb), ((string?)finding["code"], (string?)finding["subject"]));
        Assert.Equal("dbchange " + verb + " is not in this build.", (string?)answer["message"]);
        Assert.DoesNotMatch(@"\bM\d\b", answer.ToJsonString());
    }

    /// <summary>
    /// WP 1.7's doctor on a bare machine (M0 exit 3): DEGRADED, exit 6, one finding of severity error with its remedy per missing item, and
    /// no milestone claimed.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "A2")]
    [Trait("Exit", "M0.3")]
    public void Doctor_on_a_bare_machine_prints_DEGRADED_with_a_remedy_per_missing_item()
    {
        using var bare = ScratchFolder.Temporary("bare");
        var doctor = Doctor.Run(
            new Doctor.Machine(new Checkout(bare.Path, bare.Path, null, Contract.Version), bare.Path, null, bare.Under("no-sql.env"),
                Environment.Version, (_, _, _) => false),
            (c, _) => new Ran.NotFound(c.Program, "not installed"));

        var json = Render.Json(Verbs.Doctor(doctor));

        var checks = Expect.Value(doctor.Result).Prerequisites;
        VerbAnswer.Valid("dbchange.doctor.1.schema.json", json);
        Assert.Equal((6, "degraded"), ((int)json["exit"]!, (string?)json["outcome"]));
        var line = (string)json["message"]!;
        Assert.StartsWith("dbchange doctor DEGRADED | sdk=", line, StringComparison.Ordinal);
        Assert.Contains(" | dacfx=" + DacFx.Version.Match(v => v.ToString(), e => e.Message) + " (UNPINNED) | ", line,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bM\d\b", line);
        var findings = json["findings"]!.AsArray().Select(f => ((string)f!["code"]!, (string)f["severity"]!, (string?)f["remedy"]))
            .ToList();
        Assert.Equal(["doctor.sdk", "doctor.tool", "doctor.build", "doctor.git", "doctor.local-server", "doctor.lfs"],
            findings.Select(f => f.Item1));
        Assert.Equal(checks.Where(c => c.Remedy is not null).Select(c => c.Remedy), findings.Select(f => f.Item3));
        Assert.All(findings, f => Assert.Equal("error", f.Item2));
        Assert.Equal(checks.Select(c => c.Item.Name), json["prerequisites"]!.AsArray().Select(c => (string)c!["item"]!));
    }

    /// <summary>
    /// WP 1.7's doctor with every item present: READY and exit 0, naming the SDK and runtime, the tool and its DacFx against the ledger,
    /// the build route, the local server and LFS.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Doctor_with_every_item_present_prints_READY_and_exits_0()
    {
        using var machine = ScratchFolder.Temporary("ready");
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll",
            "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            machine.File(file, "");
        }

        File.Copy(Path.Combine(AppContext.BaseDirectory, Ssdt.BuildTargets.Task), machine.Under(Ssdt.BuildTargets.Task));

        machine.File("global.json", """{ "sdk": { "version": "10.0.401" } }""");
        machine.File("sql.env", "MSSQL_SA_PASSWORD=x\nDBCHANGE_SQL_PORT=11434\n");
        Runner answers = (command, _) => (command.Program + " " + command.Arguments[0]) switch
        {
            "dotnet --list-sdks" => new Ran.Exited(0, "10.0.402 [x]\n", ""),
            "git --version" => new Ran.Exited(0, "git version 2.31.1.windows.1\n", ""),
            "git lfs" => new Ran.Exited(0, "git-lfs/3.4.0 (GitHub; windows amd64; go 1.21.1)\n", ""),
            _ => new Ran.NotFound(command.Program, "not installed"),
        };

        var answer = Verbs.Doctor(
            Doctor.Run(new Doctor.Machine(new Checkout(machine.Path, machine.Path, null, Contract.Version), machine.Path, null,
                machine.Under("sql.env"), Environment.Version, (host, port, _) => (host, port) == ("127.0.0.1", 11434)), answers));

        var json = Render.Json(answer);
        VerbAnswer.Valid("dbchange.doctor.1.schema.json", json);
        Assert.Equal((0, "ready"), (answer.Exit, answer.Outcome.Word));
        Assert.Equal("dbchange doctor READY | sdk=10.0.402 | runtime=" + Environment.Version + " | tool=published | dacfx="
            + DacFx.Version.Match(v => v.ToString(), e => e.Message) + " (UNPINNED) | build=dotnet with the tool folder's targets"
            + " | git=2.31.1 | local-server=the SQL Server ~/.dbchange/sql.env names (localhost,11434) | lfs=git-lfs/3.4.0",
            answer.Message);
        Assert.Empty(answer.Findings);
        Assert.Equal((DacFx.Version.Match(v => v.ToString(), e => e.Message), "UNPINNED", null),
            ((string?)json["dacfx"], (string?)json["pin"], json["server"]));
    }

    /// <summary>
    /// The first call in Main is Telemetry.OptOut, and no static initializer runs ahead of it; what OptOut sets is TelemetryTests'.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X5")]
    public void Main_opts_out_of_telemetry_before_anything_else()
    {
        var main = typeof(Cli.Program).GetMethod(nameof(Cli.Program.Main))!;
        var il = main.GetMethodBody()!.GetILAsByteArray()!;
        var first = Array.FindIndex(il, b => b != 0x00);    // past the nops a debug build emits

        Assert.Equal(0x28, il[first]);                      // call
        Assert.Equal(typeof(Telemetry).GetMethod(nameof(Telemetry.OptOut)),
            main.Module.ResolveMethod(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(first + 1))));
        Assert.Null(typeof(Cli.Program).TypeInitializer);   // no static initializer runs ahead of Main
    }

    /// <summary>An answer with its time taken set aside, the one field that differs from run to run.</summary>
    private static byte[] Timeless(byte[] answer) =>
        Encoding.UTF8.GetBytes(
            Regex.Replace(Encoding.UTF8.GetString(answer), "\"elapsedMs\": [0-9]+", "\"elapsedMs\": 0", RegexOptions.CultureInvariant));

    /// <summary>
    /// Runs the built dbchange, as a process, and returns its exit code and standard output alone, which its JSON answer is.
    /// </summary>
    private static (int Exit, string Output) Run(params string[] arguments)
    {
        var ran = new Command(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [Path.Combine(AppContext.BaseDirectory, "dbchange.dll"), .. arguments], Programs.Default).Finish();
        return (ran.Code, ran.Output);
    }
}
