using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>DacFx's messages as it writes them into an exception's text, and the SQL Server error a message quotes.</summary>
public sealed class DacFxMessageTests
{
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Error SQL71501: [dbo].[V] has an unresolved reference to object [dbo].[Missing].", DacFxMessageType.Error, 71501, "[dbo].[V] has an unresolved reference to object [dbo].[Missing].")]
    [InlineData("Warning SQL71502: Procedure: [dbo].[P] has an unresolved reference.", DacFxMessageType.Warning, 71502, "Procedure: [dbo].[P] has an unresolved reference.")]
    [InlineData("Error SQL0: Property BlockOnPossibleDataLoss has an invalid value: maybe.", DacFxMessageType.Error, 0, "Property BlockOnPossibleDataLoss has an invalid value: maybe.")]
    public void A_line_DacFx_writes_parses_to_its_type_prefix_number_and_text(string line, DacFxMessageType type, int number, string text)
    {
        var message = Assert.IsType<DacFxMessage>(DacFxMessage.Parse(line));

        Assert.Equal((type, "SQL", number, text), (message.MessageType, message.Prefix, message.Number, message.Text));
        Assert.Equal(line, message.ToString());
        Assert.Null(message.SqlServerNumber);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Altering Table [dbo].[T]...")]
    [InlineData("Could not deploy package.")]
    [InlineData("IF EXISTS (select top 1 1 from [dbo].[Customer])")]
    [InlineData("")]
    public void A_line_that_is_no_message_parses_to_nothing(string line) => Assert.Null(DacFxMessage.Parse(line));

    /// <summary>SQL72014 quotes the SQL Server error a publish's statement raised, whose number routes the failure as a SqlException's would.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_message_quoting_a_SQL_Server_error_carries_its_number()
    {
        var message = DacFxMessage.Parse("Error SQL72014: Core Microsoft SqlClient Data Provider: Msg 2627, Level 14, State 1, Line 1 Violation of PRIMARY KEY constraint 'PK_Customer'.");

        Assert.Equal(2627, message?.SqlServerNumber);
        Assert.Equal(50000, new DacFxMessage(DacFxMessageType.Error, "SQL", 72014, "Msg 50000, Level 16, State 127, Line 6 Rows were detected.", null).SqlServerNumber);
    }
}
