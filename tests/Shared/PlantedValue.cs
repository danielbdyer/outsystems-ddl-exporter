using System;
using System.Text.RegularExpressions;
using DbChange.Kernel;
using Xunit;

namespace DbChange.Tests;

/// <summary>
/// A value planted in an input that no output may carry (VALUES.md X2, and the planted-value scans of M2 exit 4, M3 exit 4, M5 exit 6
/// and M6 exit 5): the one password every test plants, or a value unique to one plant, which a server or a file may store. A search
/// reads the whole of what came back, an error's code, message, remedy and text together, and also fails on a password set in any
/// connection string, however spelled or spaced.
/// </summary>
internal sealed record PlantedValue(string Text)
{
    /// <summary>A password set in a connection string or a script, however spelled or spaced, to any value but the printer's &lt;left out&gt; (io/SchemaText): what no output may carry.</summary>
    public static readonly Regex PasswordSetting = new(@"(?:password|pwd)\s*=\s*(?!<left out>)\S", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The password the tests plant, as a constant for an InlineData: a token no word of a message spells.</summary>
    public const string PasswordText = "Pa55!planted#7f3a";

    /// <summary>The password the tests plant.</summary>
    public static PlantedValue Password { get; } = new(PasswordText);

    /// <summary>A value drawn once per call, planted-&lt;12 hex digits&gt;, for a value a server stores, so two plants never share one.</summary>
    public static PlantedValue Unique() => new("planted-" + Guid.NewGuid().ToString("N")[..12]);

    /// <summary>The error carries the value nowhere: not in its code, its message, its remedy or its text; and it sets no password.</summary>
    public void AbsentFrom(Error error) => AbsentFrom(string.Join('\n', error.Code, error.Message, error.Remedy, error));

    /// <summary>The output carries the value nowhere, in any letter case, and sets no password.</summary>
    public void AbsentFrom(string output)
    {
        Assert.DoesNotContain(Text, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(PasswordSetting, output);
    }

    public override string ToString() => Text;
}
