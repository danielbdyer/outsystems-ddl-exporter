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
            ["doctor", "read", "diff", "classify", "predict", "profile", "twin", "prove", "record", "gate", "check", "knowledge", "--version"],
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

    /// <summary>io names what it refused; the refusal table alone says which exit that is, by the code's area.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_refusal_io_constructs_takes_an_exit_the_table_holds_by_its_codes_area()
    {
        var codes = Repository.Files.Where(f => f.StartsWith("io/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => RefusalCode.Matches(Repository.Read(f)).Select(m => m.Groups[1].Value))
            .ToList();

        Assert.Contains("build.failed", codes);
        Assert.DoesNotContain(codes, c => !Contract.RefusalExits.ContainsKey(c.Split('.')[0]));
        Assert.Empty(Contract.RefusalExits.Values.Except(Contract.Exits.Select(e => e.Code)));
        Assert.Equal([6, 7], ((string[])["sdk.missing", "build.failed"]).Select(c => Contract.Exit(new Kernel.Refusal(c, "Refused.", "Do the other thing."))));
    }

    private static readonly Regex RefusalCode = new(@"new\s+Refusal\(\s*""([a-z0-9.-]+)""", RegexOptions.CultureInvariant);

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
    public void Each_verb_built_at_M1_has_its_own_schema_and_a_refusal_of_it_validates_against_it()
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
    [InlineData(3, Blocked.Guard, "guard")]
    [InlineData(3, Blocked.Violation, "violation")]
    [InlineData(3, null, null)]
    [InlineData(0, null, null)]
    [InlineData(0, Blocked.Guard, "guard")]
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

    /// <summary>WP 1.7's doctor on a bare machine: DEGRADED, exit 6, one blocking finding with its remedy per missing item, and no milestone deferred to.</summary>
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
            Assert.Equal(["doctor.sdk", "doctor.tool", "doctor.build", "doctor.substrate", "doctor.lfs"], findings.Select(f => f.Item1));
            Assert.Equal(checks.Where(c => c.Remedy is not null).Select(c => c.Remedy), findings.Select(f => f.Item3));
            Assert.All(findings, f => Assert.Equal("block", f.Item2));
            Assert.Equal(checks.Select(c => c.Item), json["checks"]!.AsArray().Select(c => (string)c!["item"]!));
        }
        finally
        {
            Directory.Delete(bare, recursive: true);
        }
    }

    /// <summary>WP 1.7's doctor with every item present: READY and exit 0, naming the SDK and runtime, the tool and its DacFx against the ledger, the build route, the substrate and LFS.</summary>
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
                + " | substrate=docker 29.5.3 | image=present | lfs=git-lfs/3.4.0", answer.Verdict.Message);
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
    private static readonly Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Refused)> EnvelopePairs = Pairs();

    private static Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Refused)> Pairs()
    {
        var pairs = new Dictionary<string, (Action<JsonObject> Admitted, Action<JsonObject> Refused)>(StringComparer.Ordinal)
        {
            ["exit is a code of the frozen table"] = (a => a["exit"] = 7, a => a["exit"] = 8),
            ["exit 3 names its kind"] = (BlockedBy("guard"), BlockedBy(null)),
            ["exit 3's kind is guard or violation"] = (BlockedBy("violation"), BlockedBy("other")),
            ["no other exit has a kind"] = (_ => { }, a => a["verdict"]!["kind"] = "guard"),
            ["a verdict names its kind, as null when the data did not block"] = (_ => { }, a => a["verdict"]!.AsObject().Remove("kind")),
            ["a blocking finding carries a remedy"] = (Finds(1, "warn", remedy: null), Finds(1, "block", remedy: null)),
            ["a receipt names its data facts, as null when it lacks them"] = (WithReceipt(r => r["dataFacts"] = null), WithReceipt(r => r.Remove("dataFacts"))),
            ["a receipt's at is a date-time"] = (WithReceipt(_ => { }), WithReceipt(r => r["at"] = "yesterday")),
            ["a receipt names the input it lacks, as null when it lacks none"] = (WithReceipt(r => r["lacking"] = null), WithReceipt(r => r["lacking"] = "seed")),
            ["the engine names the SQL Server image by its digest"] = (WithReceipt(_ => { }), WithReceipt(r => r["engine"]!["sqlserver"] = "16.0.4295.3")),
        };

        // Instruction architecture §9.1: a remedy is required for severity block and for exits 2, 4, 6 and 9.
        foreach (var exit in (int[])[2, 4, 6, 9])
        {
            var code = exit.ToString(CultureInfo.InvariantCulture);
            pairs["exit " + code + " names a finding"] = (Finds(exit, "block", "estate doctor"), Finds(exit, severity: null));
            pairs["exit " + code + " gives every finding a remedy"] = (Finds(exit, "warn", "estate doctor"), Finds(exit, "warn", remedy: null));
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

    /// <summary>An answer at <paramref name="exit"/> with one finding of <paramref name="severity"/>, or with none when it is null.</summary>
    private static Action<JsonObject> Finds(int exit, string? severity, string? remedy = null) => answer =>
    {
        answer["exit"] = exit;
        answer["findings"] = severity is null ? new JsonArray() : new JsonArray(new JsonObject
        {
            ["code"] = "guard.row-presence", ["severity"] = severity, ["subject"] = "dbo.Customer.Email",
            ["message"] = "The publish guard refused: the table has rows.", ["remedy"] = remedy,
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

    /// <summary>Runs the built estate, as a process, and returns its exit code and standard output.</summary>
    private static (int Exit, string Output) Estate(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "estate.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
