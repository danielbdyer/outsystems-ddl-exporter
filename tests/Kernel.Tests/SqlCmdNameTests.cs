using System.Collections.Generic;
using Xunit;

namespace DbChange.Kernel.Tests;

/// <summary>A SQLCMD variable's name as sqlcmd matches it: in any case, so a package's declaration and a profile's value meet whatever their case.</summary>
public sealed class SqlCmdNameTests
{
    [Fact]
    [Trait("Category", "fast")]
    public void Two_SQLCMD_names_differing_in_case_are_one_name_and_each_prints_as_written()
    {
        var (tag, upper) = (Name("Tag"), Name("TAG"));

        Assert.Equal(tag, upper);
        Assert.Equal(0, tag.CompareTo(upper));
        Assert.Single(new HashSet<SqlCmdName> { tag, upper });
        Assert.NotEqual(tag, Name("Tags"));
        Assert.Equal(("Tag", "TAG"), (tag.ToString(), upper.ToString()));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("1Tag")]
    [InlineData("Tag Name")]
    [InlineData("$(Tag)")]
    [InlineData("")]
    public void A_name_sqlcmd_does_not_read_is_refused(string name) =>
        Assert.Equal("sqlcmd.name", Assert.IsType<Result<SqlCmdName>.Failed>(SqlCmdName.Of("the profile", name)).Error.Code);

    private static SqlCmdName Name(string text) => Assert.IsType<Result<SqlCmdName>.Ok>(SqlCmdName.Of("the profile", text)).Value;
}
