using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChange.Cli;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// dbchange profile before anything connects (V3_MILESTONES.md WP 3.7): its arguments; --vocabulary, refused for an environment
/// classified real at exit 9 and not in this build for one a lead confirmed synthetic; the row tiers; and the row-tier ledger's text,
/// dbchange/ledgers/row-tiers.md, to which a measurement appends a dated row for each table whose tier changed and in which no row is
/// edited. dbchange runs in this process at a repository root the test writes, whose environments' connections resolve to nothing.
/// </summary>
public sealed class ProfileVerbTests : IDisposable
{
    private readonly ScratchFolder root = ScratchFolder.Temporary("profile-verb");

    public void Dispose() => root.Dispose();

    private Checkout Here => new(root.Path, root.Path, null, Contract.Version);

    /// <summary>
    /// --vocabulary reads literal values, which no query reads from an environment classified real (V3_MILESTONES.md §17 items 2 and
    /// 24): aggregate-query.vocabulary at exit 9, before the connection's reference is read; for an environment a named lead confirmed
    /// synthetic the vocabulary is not in this build, verb.not-built at exit 6, since it returns with the shape tier.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    [InlineData("real", null, 9, "aggregate-query.vocabulary")]
    [InlineData(null, null, 9, "aggregate-query.vocabulary")]
    [InlineData("synthetic", "the dev lead", 6, "verb.not-built")]
    public void Vocabulary_is_exit_9_until_the_environment_is_confirmed_synthetic(string? classification, string? lead, int exit,
        string code)
    {
        EnvironmentsJson.Of("uat", new("env:DBCHANGE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "uat-sql",
            Classification: classification, ConfirmedBy: lead, ConfirmedOn: lead is null ? null : "2026-10-20")).WriteTo(root.Path);

        var (answered, answer) = VerbAnswer.Of(Here, "profile", "--env", "uat", "dbo.Customer.Email", "--vocabulary");

        Assert.Equal((exit, code), (answered, (string?)answer["findings"]![0]!["code"]));
        Assert.Contains("env:uat", (string)answer["message"]!, StringComparison.Ordinal);
    }

    /// <summary>Each malformed request is a bad argument, exit 1, before the environments file is read.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("arguments.column", "--env", "uat")]
    [InlineData("arguments.column", "--env", "uat", "dbo.Customer")]
    [InlineData("arguments.column", "--env", "uat", "Customer.Email; DROP TABLE dbo.[Order]")]
    [InlineData("arguments.column", "--env", "uat", "Sales.dbo.Customer.Email")]
    [InlineData("arguments.column", "--env", "uat", "dbo.Customer.Email", "dbo.Customer.Name")]
    [InlineData("arguments.column", "--env", "uat", "dbo.Customer.Email", "--counts")]
    [InlineData("arguments.unknown-flag", "--env", "uat", "--counts", "--vocabulary")]
    [InlineData("arguments.unknown-flag", "--env", "uat", "dbo.Customer.Email", "--commit")]
    [InlineData("arguments.unknown-flag", "--env", "uat", "--where", "1 = 1")]
    [InlineData("arguments.missing-flag", "dbo.Customer.Email")]
    [InlineData("environments.environment-name", "--env", "UAT", "dbo.Customer.Email")]
    public void A_malformed_request_is_exit_1_or_names_its_flaw_before_anything_connects(string code, params string[] arguments)
    {
        var (exit, answer) = VerbAnswer.Of(Here, ["profile", .. arguments]);

        Assert.Equal(code, (string?)answer["findings"]![0]!["code"]);
        Assert.Equal(code.StartsWith("environments.", StringComparison.Ordinal) ? 6 : 1, exit);
        Assert.DoesNotContain("DROP TABLE", (string)answer["message"]!, StringComparison.Ordinal);
    }

    /// <summary>An environment the environments file does not name is target.unnamed at exit 1, naming those it does.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_environment_the_environments_file_does_not_name_is_exit_1()
    {
        EnvironmentsJson.Dev("env:DBCHANGE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()).WriteTo(root.Path);

        var (exit, answer) = VerbAnswer.Of(Here, "profile", "--env", "prod", "--counts");

        Assert.Equal((1, "target.unnamed"), (exit, (string?)answer["findings"]![0]!["code"]));
    }

    /// <summary>A row tier is the order of magnitude of a table's rows, v2's tiers: 0, 1–1k, 1k–100k, 100k–1M and >1M.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(0, "0")]
    [InlineData(1, "1–1k")]
    [InlineData(1000, "1–1k")]
    [InlineData(1001, "1k–100k")]
    [InlineData(100_000, "1k–100k")]
    [InlineData(100_001, "100k–1M")]
    [InlineData(1_000_000, "100k–1M")]
    [InlineData(1_000_001, ">1M")]
    public void A_row_tier_is_the_order_of_magnitude_of_a_table_s_rows(long rows, string tier) => Assert.Equal(tier, Profile.TierOf(rows));

    /// <summary>
    /// The ledger: written with its table when absent; a measurement appends a row for each table whose tier differs from the last row of
    /// its environment and table, or that has none, after the table's last row and before any text after it, so a table whose tier holds,
    /// or one another environment measured, gets no row; and no row is edited. Every row leads with the date, written yyyy-MM-dd.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "O12")]
    public void A_measurement_appends_a_dated_row_for_each_table_whose_tier_changed_and_edits_no_row()
    {
        var (uat, qa) = (Value(EnvironmentName.Of("--env", "uat")), Value(EnvironmentName.Of("--env", "qa")));
        var (customer, order) = (Value(Name.Of("dbo", "Customer")), Value(Name.Of("dbo", "Order|Line")));

        var (first, firstRows) = Value(Profile.Appended(null, uat, new DateOnly(2026, 10, 21), [(customer, "1–1k"), (order, "0")]));
        var annotated = first + "\nMeasured from Dev's copy, and UAT's own, on the dates each row names.\n";
        var (second, secondRows) = Value(Profile.Appended(annotated, uat, new DateOnly(2026, 10, 22),
            [(customer, "1–1k"), (order, "1–1k")]));
        var (third, thirdRows) = Value(Profile.Appended(second, qa, new DateOnly(2026, 10, 23), [(customer, "1–1k")]));

        Assert.Equal([customer, order], firstRows);
        Assert.Equal([order], secondRows);
        Assert.Equal([customer], thirdRows);
        var rows = third.Split('\n').Where(l => l.StartsWith("| 2026-", StringComparison.Ordinal)).ToList();
        Assert.Equal(
        [
            "| 2026-10-21 | uat | [dbo].[Customer] | 1–1k |", "| 2026-10-21 | uat | [dbo].[Order\\|Line] | 0 |",
            "| 2026-10-22 | uat | [dbo].[Order\\|Line] | 1–1k |", "| 2026-10-23 | qa | [dbo].[Customer] | 1–1k |",
        ], rows);
        Assert.EndsWith("| 2026-10-23 | qa | [dbo].[Customer] | 1–1k |\n\nMeasured from Dev's copy, and UAT's own, on the dates each row names.\n",
            third, StringComparison.Ordinal);
        Assert.StartsWith(first.Split("| 2026-")[0], third, StringComparison.Ordinal);
    }

    /// <summary>A ledger with no table of the four columns, or a row of another width, an undated row or a tier of no form, is
    /// ledger.malformed naming its line, and nothing is appended.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("# Row tiers\n\nNo table here.\n", "holds no table")]
    [InlineData("| Date | Environment | Table | Tier |\n|---|---|---|---|\n| 2026-10-21 | uat | [dbo].[T] |\n", "line 3")]
    [InlineData("| Date | Environment | Table | Tier |\n|---|---|---|---|\n| 21/10/2026 | uat | [dbo].[T] | 0 |\n", "line 3")]
    [InlineData("| Date | Environment | Table | Tier |\n|---|---|---|---|\n| 2026-10-21 | uat | [dbo].[T] | a few |\n", "line 3")]
    public void A_ledger_of_another_form_is_refused_naming_its_line(string ledger, string where)
    {
        var refused = Failed(Profile.Appended(ledger, Value(EnvironmentName.Of("--env", "uat")), new DateOnly(2026, 10, 21),
            [(Value(Name.Of("dbo", "T")), "0")]), "ledger.malformed");

        Assert.Contains(where, refused.Message, StringComparison.Ordinal);
        Assert.StartsWith("dbchange/ledgers/row-tiers.md ", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A measurement's date is the UTC day of the clock it is taken by: late on the 20th in Chicago is the 21st (VALUES.md
    /// O12).</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "O12")]
    public void A_measurement_is_dated_by_the_UTC_day_of_its_clock() =>
        Assert.Equal(new DateOnly(2026, 10, 21), Profile.DateOf(new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.FromHours(-5))));
}
