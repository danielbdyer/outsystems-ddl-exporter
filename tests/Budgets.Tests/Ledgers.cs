using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// VALUES.md O12 for the ledgers: each ledger the corpus commits under tests/Golden/dbchange/ledgers/, and the row-tier ledger as
/// dbchange profile --counts writes it, is a table whose first column is Date, and every row of it leads with its date written
/// yyyy-MM-dd, a day the calendar holds. That the row-tier ledger's date is the UTC day of the clock its measurement reads is
/// <c>Io.Tests: "A measurement is dated by the UTC day of its clock"</c>.
/// </summary>
public sealed class Ledgers
{
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "O12")]
    public void Every_ledger_row_leads_with_its_date_written_yyyy_MM_dd()
    {
        var written = Expect.Value(Io.Profile.Appended(null, Expect.Value(EnvironmentName.Of("--env", "uat")), new DateOnly(2026, 10, 21),
            [(Expect.Value(Name.Of("dbo", "Customer")), "1–1k"), (Expect.Value(Name.Of("dbo", "Order")), "0")])).Text;
        var ledgers = Repository.Files
            .Where(f => f.StartsWith("tests/Golden/dbchange/ledgers/", StringComparison.Ordinal) && f.EndsWith(".md", StringComparison.Ordinal))
            .Select(f => (Ledger: f, Text: Repository.Read(f)))
            .Append((Ledger: "the row-tier ledger profile --counts writes", Text: written)).ToList();

        Assert.Contains(ledgers, l => l.Ledger == "tests/Golden/dbchange/ledgers/toolchain.md");
        Assert.All(ledgers, ledger =>
        {
            var (header, rows) = Table(ledger.Text);
            Assert.True(header.FirstOrDefault() == "Date", ledger.Ledger + " leads its table with " + header.FirstOrDefault() + ", not Date");
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.True(
                DateOnly.TryParseExact(row[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                ledger.Ledger + ": the row " + string.Join(" | ", row) + " leads with no date written yyyy-MM-dd"));
        });
    }

    /// <summary>A Markdown text's first table: its header's cells, and each row after the separator as its cells.</summary>
    private static (IReadOnlyList<string> Header, IReadOnlyList<string[]> Rows) Table(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith('|'));
        string[] Cells(string line) => [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim())];
        return at < 0 ? ([], [])
            : (Cells(lines[at]), [.. lines.Skip(at + 2).TakeWhile(l => l.StartsWith('|')).Select(Cells)]);
    }
}
