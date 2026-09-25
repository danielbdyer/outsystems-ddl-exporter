using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Estate.Cli;
using Estate.Io;
using Xunit;

namespace Estate.Budgets.Tests.Register;

/// <summary>
/// The engine's errors are in the register (V3_INSTRUCTION_ARCHITECTURE.md §10 test 4; VALUES.md S2, O4, L1). Every error
/// the kernel, io and the cli construct, reached through <see cref="RefusalPaths"/>, carries a code, a message and a remedy; the remedy is
/// one move on one line, led by a verb, an estate verb or a file path, never a paragraph; and neither the message nor the remedy
/// uses a retired word or a banned form of ci/register.json. A code the sources construct with no way to it here fails, so a new
/// error arrives with its driver.
/// </summary>
public sealed class Refusals
{
    /// <summary>Words that lead a description or a hedge, never a move: an article, a pronoun, an auxiliary, a condition.</summary>
    private static readonly HashSet<string> NoMove = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "that", "these", "those", "it", "its", "i", "we", "you", "your", "our", "my", "they", "their", "there", "here",
        "please", "can", "could", "should", "would", "must", "may", "might", "will", "shall", "is", "are", "was", "were", "be", "been", "do", "does",
        "did", "has", "have", "had", "not", "no", "nothing", "none", "cannot", "unable", "error", "failed", "if", "when", "because", "since", "maybe", "perhaps",
    };

    private static readonly Regex FilePath = new(@"\A[\w.-]*[/\\][\w./\\-]*\z|\A[\w-]+(\.[\w-]+)*\.[a-z]{1,8}\z", RegexOptions.CultureInvariant);

    public Refusals() => Telemetry.OptOut();   // before DacFx loads, as estate's Main does

    public static TheoryData<string> Cases => new(RefusalPaths.All.Select(c => c.Label));

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Cases))]
    public void An_error_carries_its_code_a_message_and_a_remedy_that_is_one_move_in_the_register(string label)
    {
        var way = RefusalPaths.All.Single(c => c.Label == label);
        var scratch = Directory.CreateTempSubdirectory("estate-register-").FullName;
        try
        {
            var error = way.Drive(scratch, "planted-value");

            Assert.Equal(way.Code, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.Message) || string.IsNullOrWhiteSpace(error.Remedy), error.Code + " lacks a message or a remedy");
            Assert.Null(Flaw(error.Remedy));
            Assert.Empty(Prose.Findings(error.Message).Concat(Prose.Findings(error.Remedy)).Select(f => error.Code + ": " + f));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Every_error_code_the_kernel_io_and_the_cli_construct_has_a_way_to_it_here()
    {
        var driven = RefusalPaths.All.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var written = RefusalPaths.InTheSources().ToList();

        Assert.Contains("posture.literal-connection", written);
        Assert.Contains("arguments.unknown-check", written);   // the cli's own, in cli/Check.cs
        Assert.Contains("element.", written);   // the start of a composed code: element.property-name, element.relationship-name
        Assert.DoesNotContain(written, code => code.EndsWith('.') ? !driven.Any(d => d.StartsWith(code, StringComparison.Ordinal)) : !driven.Contains(code));
        Assert.DoesNotContain(driven, code => !written.Contains(code) && !written.Any(start => start.EndsWith('.') && code.StartsWith(start, StringComparison.Ordinal)));
    }

    /// <summary>The remedy check as minimal pairs: each remedy the register keeps, beside one it refuses and why.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Give each part 1 to 128 characters.", null)]
    [InlineData("estate doctor", null)]
    [InlineData("estate --help lists what this build runs", null)]
    [InlineData("ci/publish.sh, or ci/publish.ps1 on Windows, publishes dist/estate/; run estate from there", null)]
    [InlineData("global.json names the SDK to install.", null)]
    [InlineData("The file is missing.", "leads with 'The', which moves nothing")]
    [InlineData("You should build again.", "leads with 'You', which moves nothing")]
    [InlineData("If the file is missing, restore it.", "leads with 'If', which moves nothing")]
    [InlineData("estate frobnicate", "names no verb of estate")]
    [InlineData("Fix the error.\nThen build again.", "spans lines")]
    [InlineData("Fix the error. Build again. Commit the fix. Push it.", "runs to a paragraph")]
    public void A_remedy_is_one_move_led_by_a_verb_an_estate_verb_or_a_file_path(string remedy, string? flaw) =>
        Assert.Equal(flaw, Flaw(remedy));

    /// <summary>What keeps a remedy from being one move, or null when it is one.</summary>
    private static string? Flaw(string remedy)
    {
        var words = remedy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = words[0].TrimEnd(',', ';', ':', '.');
        return remedy.Contains('\n', StringComparison.Ordinal) ? "spans lines"
            : Regex.Matches(remedy, @"[.;]\s+[A-Z]", RegexOptions.CultureInvariant).Count > 1 || remedy.Length > 300 ? "runs to a paragraph"
            : first == "estate" ? (words.Length > 1 && (words[1] == "--help" || Contract.Verbs.Any(v => v.Name == words[1].TrimEnd(',', ';', ':', '.'))) ? null : "names no verb of estate")
            : FilePath.IsMatch(first) ? null
            : NoMove.Contains(first) || !char.IsLetter(first[0]) ? "leads with '" + first + "', which moves nothing"
            : null;
    }
}
