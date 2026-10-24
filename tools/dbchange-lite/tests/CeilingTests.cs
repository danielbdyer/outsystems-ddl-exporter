using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class CeilingTests
{
    public const int Ceiling = 3400;
    public const int EachFile = 200;

    private static IReadOnlyList<string> Sources => Directory.EnumerateFiles(Repo.Src, "*.cs", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToList();

    [Fact]
    [Trait("Category", "fast")]
    public void The_code_holds_its_ceiling_and_each_file_its_own()
    {
        var lines = Sources.ToDictionary(f => Path.GetFileName(f), f => File.ReadAllLines(f).Length);
        Assert.True(lines.Values.Sum() <= Ceiling, "src/ holds " + lines.Values.Sum() + " lines, over the ceiling of " + Ceiling + ": " + string.Join(", ", lines.OrderByDescending(l => l.Value).Take(5).Select(l => l.Key + " " + l.Value)));
        Assert.Empty(lines.Where(l => l.Value > EachFile).Select(l => l.Key + " " + l.Value));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void No_source_file_holds_a_comment()
    {
        var offending = new List<string>();
        foreach (var file in Sources)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (OutsideStrings(lines[i]).Contains("//", StringComparison.Ordinal) || OutsideStrings(lines[i]).Contains("/*", StringComparison.Ordinal))
                {
                    offending.Add(Path.GetFileName(file) + ":" + (i + 1));
                }
            }
        }

        Assert.Empty(offending);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Every_command_line_the_knowledge_and_the_design_document_show_names_a_verb_and_flags_help_lists()
    {
        var files = Directory.EnumerateFiles(Path.Combine(Repo.Lite, "knowledge"), "*.md").ToList();
        var design = Path.Combine(Repo.Root, "DBCHANGE_LITE.md");
        if (File.Exists(design))
        {
            files.Add(design);
        }

        Assert.NotEmpty(files);
        var commands = files.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:`|^\s*)dbchange-lite (--?[a-z-]+)([^`\n]*)", RegexOptions.CultureInvariant | RegexOptions.Multiline)
            .Select(m => (File: Path.GetFileName(f), Verb: m.Groups[1].Value, Tail: m.Groups[2].Value))).ToList();
        Assert.NotEmpty(commands);
        foreach (var (file, verbName, tail) in commands.Where(c => c.Verb != "--help" && c.Verb != "--version"))
        {
            var verb = Contract.Verbs.FirstOrDefault(v => v.Name == verbName);
            Assert.True(verb is not null, file + " shows dbchange-lite " + verbName + ", which is no verb");
            foreach (var flag in Regex.Matches(tail, @"--[a-z-]+", RegexOptions.CultureInvariant).Select(m => m.Value).Where(f => f != "--json"))
            {
                Assert.True(verb!.Flags.Any(f => f.Name == flag) || verb.Switches.Contains(flag), file + " shows " + verbName + " " + flag + ", which " + verbName + " has not");
            }
        }
    }

    private static string OutsideStrings(string line)
    {
        var kept = new System.Text.StringBuilder();
        var inString = false;
        var verbatim = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inString)
            {
                if (c == '\\' && !verbatim)
                {
                    i++;
                }
                else if (c == '"')
                {
                    if (verbatim && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        i++;
                    }
                    else
                    {
                        inString = false;
                    }
                }
            }
            else if (c == '"')
            {
                inString = true;
                verbatim = i > 0 && line[i - 1] == '@';
            }
            else if (c == '\'' && i + 2 < line.Length && line[i + 2] == '\'')
            {
                i += 2;
            }
            else if (c == '\'' && i + 3 < line.Length && line[i + 1] == '\\' && line[i + 3] == '\'')
            {
                i += 3;
            }
            else
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }
}
