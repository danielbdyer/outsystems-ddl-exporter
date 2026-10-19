using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Cli;
using DbChange.Io;
using DbChange.Tests;
using Xunit;

namespace DbChange.Budgets.Tests.Register;

/// <summary>
/// dbchange's errors are in the register (V3_INSTRUCTION_ARCHITECTURE.md §10 test 4; VALUES.md S2, O4, L1). Every error
/// the kernel, io and the cli construct, reached through <see cref="ErrorPaths"/>, carries a code, a message and a remedy; the remedy is
/// one imperative sentence on one line, starting with a capital letter and ending with a period, led by a verb, with any dbchange verb it
/// runs one of the verb table's, never a paragraph (finding D13); and neither the message nor the remedy uses a retired word or a banned
/// form of ci/register.json. A code the sources construct with no way to it here fails, so a new error arrives with its driver. The
/// kernel's and the cli's remedies hold the one form now; io's take it in the adapters' pass, until which the two form checks and the
/// verb scan are not applied to a code io alone constructs.
/// </summary>
public sealed class Errors
{
    /// <summary>Words that lead a description or a hedge, never a move: an article, a pronoun, an auxiliary, a condition.</summary>
    private static readonly HashSet<string> NoMove = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "that", "these", "those", "it", "its", "i", "we", "you", "your", "our", "my", "they", "their", "there", "here",
        "please", "can", "could", "should", "would", "must", "may", "might", "will", "shall", "is", "are", "was", "were", "be", "been", "do", "does",
        "did", "has", "have", "had", "not", "no", "nothing", "none", "cannot", "unable", "error", "failed", "if", "when", "because", "since", "maybe", "perhaps",
    };

    private static readonly Regex RunsDbChange = new(@"\b[Rr]un dbchange (\S+)", RegexOptions.CultureInvariant);

    public static TheoryData<string> Cases => new(ErrorPaths.All.Select(c => c.Label));

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "S2")]
    [Trait("Value", "O4")]
    [Trait("Value", "L1")]
    [MemberData(nameof(Cases))]
    public void An_error_carries_a_remedy_that_is_one_move_in_the_register(string label)
    {
        var way = ErrorPaths.All.Single(c => c.Label == label);
        using var scratch = ScratchFolder.Temporary("register");

        var error = way.Drive(scratch.Path, "planted-value");

        Assert.Equal(way.Code, error.Code);
        var flaw = Flaw(error.Remedy);
        Assert.True(flaw is null, error.Code + ": the remedy " + flaw + ": " + error.Remedy);
        Assert.Empty(Prose.Findings(error.Message).Concat(Prose.Findings(error.Remedy)).Select(f => error.Code + ": " + f));
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S2")]
    [Trait("Value", "O4")]
    public void Every_error_code_the_kernel_io_and_the_cli_construct_has_a_driver_here()
    {
        var driven = ErrorPaths.All.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var written = ErrorPaths.InTheSources().ToList();

        Assert.Contains("environments.literal-connection", written);
        Assert.Contains("arguments.unknown-check", written);   // the cli's own, in cli/Check.cs
        Assert.Contains("element.", written);   // the start of a composed code: element.property-name, element.relationship-name
        Assert.DoesNotContain(written, code => code.EndsWith('.') ? !driven.Any(d => d.StartsWith(code, StringComparison.Ordinal)) : !driven.Contains(code));
        Assert.DoesNotContain(driven, code => !written.Contains(code) && !written.Any(start => start.EndsWith('.') && code.StartsWith(start, StringComparison.Ordinal)));
    }

    /// <summary>
    /// S27 of the pre-M2 review: each remedy the doctor gives, on a bare machine and on one whose Docker does not answer or answers past its
    /// timeout, is one move in the register, as an error's is; the doctor's remedies are no error's, so the drivers above never read them.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "O4")]
    [InlineData("nothing installed")]
    [InlineData("Docker not answering")]
    [InlineData("Docker past its timeout")]
    public void Every_remedy_the_doctor_gives_is_one_move_in_the_register(string machine)
    {
        using var bare = ScratchFolder.Temporary("doctor-register");
        Runner run = machine switch
        {
            "nothing installed" => (c, _) => new Ran.NotFound(c.Program, "not installed"),
            "Docker not answering" => (c, _) => c.Program == "docker" ? new Ran.Exited(1, "", "Cannot connect to the Docker daemon") : new Ran.NotFound(c.Program, "not installed"),
            _ => (c, _) => c.Program == "docker" ? new Ran.TimedOut(c.Timeout, "", "") : new Ran.NotFound(c.Program, "not installed"),
        };

        var remedies = Doctor.Examine(new Doctor.Machine(new Checkout(bare.Path, bare.Path, null, Contract.Version), bare.Path, null, bare.Under("no-sql.env"), Environment.Version), run)
            .Where(p => p.Remedy is not null).Select(p => (p.Item.Name, Remedy: p.Remedy!)).ToList();

        Assert.NotEmpty(remedies);
        Assert.Empty(remedies.Where(r => Flaw(r.Remedy) is not null).Select(r => r.Name + ": the remedy " + Flaw(r.Remedy) + ": " + r.Remedy));
        Assert.Empty(remedies.SelectMany(r => Prose.Findings(r.Remedy).Select(f => r.Name + ": " + f)));
    }

    /// <summary>The scan reads a code from a construction that names the type and from a target-typed one (finding D6), and reads no code from a string that is none.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("return new Error(\"environments.missing\", \"No environments file.\", \"Commit it.\");", "environments.missing")]
    [InlineData("private static Error Malformed(string at) => new(\"environments.malformed\", at, \"Write it.\");", "environments.malformed")]
    [InlineData("new Error(ErrorCode.Text(category) + \".\", m, r)", null)]
    [InlineData("new(\"dbchange.doctor/1\", outcome, 0, message, [])", null)]
    [InlineData("new(\"done\", [0], \"the message is the tool's version\")", null)]
    public void The_scan_reads_a_code_from_a_named_and_a_target_typed_construction_alike(string source, string? code) =>
        Assert.Equal(code, ErrorPaths.CodesIn(source).SingleOrDefault());

    /// <summary>The remedy check as minimal pairs: each remedy the register keeps, beside one it refuses and why.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Give each part 1 to 128 characters.", null)]
    [InlineData("Run dbchange doctor.", null)]
    [InlineData("Run dbchange --help to see the verbs.", null)]
    [InlineData("Set the variable, or write the file outside git, that env:DBCHANGE_DEV names.", null)]
    [InlineData("dbchange doctor", "starts in lower case")]
    [InlineData("global.json names the SDK to install.", "starts in lower case")]
    [InlineData("ci/publish.sh, or ci/publish.ps1 on Windows, publishes dist/dbchange/; run dbchange from there", "starts in lower case")]
    [InlineData("Run dbchange doctor", "has no period")]
    [InlineData("Run dbchange frobnicate.", "names no verb of dbchange")]
    [InlineData("Then run dbchange again.", "names no verb of dbchange")]
    [InlineData("The file is missing.", "leads with 'The', which moves nothing")]
    [InlineData("You should build again.", "leads with 'You', which moves nothing")]
    [InlineData("If the file is missing, restore it.", "leads with 'If', which moves nothing")]
    [InlineData("Fix the error.\nThen build again.", "spans lines")]
    [InlineData("Fix the error. Build again. Commit the fix. Push it.", "runs to a paragraph")]
    public void A_remedy_is_one_imperative_sentence_that_runs_a_verb_of_dbchange_if_any(string remedy, string? flaw) =>
        Assert.Equal(flaw, Flaw(remedy));

    /// <summary>What keeps a remedy from being one imperative sentence, or null when it is one.</summary>
    private static string? Flaw(string remedy)
    {
        var words = remedy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = words[0].TrimEnd(',', ';', ':', '.');
        var runs = RunsDbChange.Matches(remedy).Select(m => m.Groups[1].Value.TrimEnd(',', ';', ':', '.')).ToList();
        return remedy.Contains('\n', StringComparison.Ordinal) ? "spans lines"
            : Regex.Matches(remedy, @"[.;]\s+[A-Z]", RegexOptions.CultureInvariant).Count > 1 || remedy.Length > 300 ? "runs to a paragraph"
            : !char.IsUpper(remedy[0]) ? "starts in lower case"
            : !remedy.EndsWith('.') ? "has no period"
            : runs.Any(word => word != "--help" && !Contract.Verbs.Any(v => v.Name == word)) ? "names no verb of dbchange"
            : NoMove.Contains(first) ? "leads with '" + first + "', which moves nothing"
            : null;
    }
}
