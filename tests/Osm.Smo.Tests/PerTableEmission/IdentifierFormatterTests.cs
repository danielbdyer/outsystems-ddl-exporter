using Osm.Smo;
using Osm.Smo.PerTableEmission;
using Xunit;

namespace Osm.Smo.Tests.PerTableEmission;

public class IdentifierFormatterTests
{
    private readonly IdentifierFormatter _formatter = new();

    [Fact]
    public void QuoteIdentifier_uses_square_brackets_by_default()
    {
        var format = SmoFormatOptions.Default;

        var quoted = _formatter.QuoteIdentifier("Column]Name", format);

        Assert.Equal("[Column]]Name]", quoted);
    }

    [Fact]
    public void QuoteIdentifier_uses_double_quotes_when_configured()
    {
        var format = SmoFormatOptions.Default.WithIdentifierQuoteStrategy(IdentifierQuoteStrategy.DoubleQuote);

        var quoted = _formatter.QuoteIdentifier("Value\"Name", format);

        Assert.Equal("\"Value\"\"Name\"", quoted);
    }

    [Fact]
    public void CreateIdentifier_applies_requested_quote_type()
    {
        var format = SmoFormatOptions.Default.WithIdentifierQuoteStrategy(IdentifierQuoteStrategy.None);

        var identifier = _formatter.CreateIdentifier("Unquoted", format);

        Assert.Equal("Unquoted", identifier.Value);
        Assert.Equal(Microsoft.SqlServer.TransactSql.ScriptDom.QuoteType.NotQuoted, identifier.QuoteType);
    }
}
