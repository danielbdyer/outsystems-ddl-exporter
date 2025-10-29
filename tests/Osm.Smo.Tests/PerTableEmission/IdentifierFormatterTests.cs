using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class IdentifierFormatterTests
{
    private readonly IdentifierFormatter _formatter = new();

    [Fact]
    public void QuoteIdentifier_uses_square_brackets_and_escapes_closing_bracket()
    {
        var quoted = _formatter.QuoteIdentifier("Name]Suffix", SmoFormatOptions.Default);

        Assert.Equal("[Name]]Suffix]", quoted);
    }

    [Fact]
    public void QuoteIdentifier_uses_double_quotes_when_strategy_is_double_quote()
    {
        var options = SmoFormatOptions.Default.WithIdentifierQuoteStrategy(IdentifierQuoteStrategy.DoubleQuote);

        var quoted = _formatter.QuoteIdentifier("O'Reilly", options);

        Assert.Equal("\"O'Reilly\"", quoted);
    }

    [Fact]
    public void ResolveConstraintName_swaps_original_table_name_with_effective_name()
    {
        var resolved = _formatter.ResolveConstraintName(
            originalName: "FK_OSUSR_ORIGINAL_CUSTOMER",
            originalTableName: "OSUSR_ORIGINAL_CUSTOMER",
            logicalTableName: "Customer",
            effectiveTableName: "OSUSR_RENAMED_CUSTOMER");

        Assert.Equal("FK_OSUSR_RENAMED_OSUSR_RENAMED_CUSTOMER", resolved);
    }
}
