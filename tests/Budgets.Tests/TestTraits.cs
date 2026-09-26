using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DbChange.Budgets.Tests;

/// <summary>
/// Every test method under tests/ with the traits above it, read from the sources as ci/laws.sh and ci/laws.ps1 read them (comment
/// lines skipped; the namespace, the class declared at the start of a line, each [Trait(…)] above a public method), so what LAWS.md
/// lists, what holds each VALUES.md row and which milestone exits have a test are one reading.
/// </summary>
internal static class TestTraits
{
    /// <summary>One test method: its project (Kernel.Tests, Io.Tests, Budgets.Tests), its class's full name, its name and its traits, in order.</summary>
    public sealed record Test(string Project, string Class, string Method, IReadOnlyList<(string Name, string Value)> Traits)
    {
        public string FullName => Class + "." + Method;

        /// <summary>The values of the trait named, in the order declared.</summary>
        public IEnumerable<string> Values(string trait) => Traits.Where(t => t.Name == trait).Select(t => t.Value);

        /// <summary>The method's name as LAWS.md prints it: _s_ as a possessive, and each other underscore a space.</summary>
        public string English => Method.Replace("_s_", "'s_", StringComparison.Ordinal).Replace('_', ' ');

        /// <summary>The method's name as words: lower case, letters and digits only, one space between, so a name and its English form compare.</summary>
        public string Words => TestTraits.Words(Method);
    }

    private static readonly Regex Comment = new(@"^[ \t]*//", RegexOptions.CultureInvariant);
    private static readonly Regex Namespace = new(@"^namespace ([\w.]+);", RegexOptions.CultureInvariant);
    private static readonly Regex Class = new(@"^(?:(?:public|internal|private|sealed|static|abstract|partial|file)[ \t]+)*class[ \t]+(\w+)", RegexOptions.CultureInvariant);
    private static readonly Regex Trait = new(@"\[Trait\(""(\w+)"", ""([^""]+)""\)\]", RegexOptions.CultureInvariant);
    private static readonly Regex Marked = new(@"^\s*\[(?:Fact|Theory)\b", RegexOptions.CultureInvariant);
    private static readonly Regex Method = new(@"^[ \t]*public (?:async )?[\w<>]+ (\w+)\(", RegexOptions.CultureInvariant);

    /// <summary>Every test method: a public method under tests/ with a [Fact] or [Theory] above it.</summary>
    public static IReadOnlyList<Test> All { get; } = Read();

    /// <summary>A name as its words: lower case, letters and digits only, one space between.</summary>
    public static string Words(string name) => string.Join(' ', Regex.Split(name.ToLowerInvariant(), @"[^\p{Ll}\p{Nd}]+", RegexOptions.CultureInvariant).Where(w => w.Length > 0));

    private static List<Test> Read()
    {
        var tests = new List<Test>();
        foreach (var file in Repository.Files.Where(f => f.StartsWith("tests/", StringComparison.Ordinal) && !f.StartsWith("tests/Golden/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var (project, space, type, marked) = (file.Split('/')[1], "", "", false);
            var traits = new List<(string, string)>();
            foreach (var line in Repository.Lines(file).Where(l => !Comment.IsMatch(l)))
            {
                if (Namespace.Match(line) is { Success: true } ns)
                {
                    space = ns.Groups[1].Value;
                }

                if (Class.Match(line) is { Success: true } declared)
                {
                    type = declared.Groups[1].Value;
                }

                marked |= Marked.IsMatch(line);
                traits.AddRange(Trait.Matches(line).Select(m => (m.Groups[1].Value, m.Groups[2].Value)));
                if (Method.Match(line) is { Success: true } method)
                {
                    if (marked)
                    {
                        tests.Add(new Test(project, space + "." + type, method.Groups[1].Value, [.. traits]));
                    }

                    (marked, traits) = (false, []);
                }
            }
        }

        return tests;
    }
}
