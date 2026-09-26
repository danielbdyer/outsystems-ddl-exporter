using System;
using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;

namespace DbChange.Kernel.Tests;

/// <summary>
/// A server as .dbchange/copies.json records it and R15 compares it (V3_MILESTONES.md §4 row 16): the data source's host, then its port or
/// instance, in lower case; this machine, however a connection spells it, is localhost. The spelling is the one the registry's rows
/// already carry, so a row written before ServerName existed still resolves; and a host as dbchange/environments.json names one.
/// </summary>
public sealed class ServerNameTests
{
    private const string Machine = "DANNY-PC";

    /// <summary>A data source in each form SqlClient reads: a protocol or none, a host by name or address or this machine, then a port or an instance or neither, in any case and spacing.</summary>
    private static readonly Gen<string> DataSource = Gen.Select(
        Gen.OneOf(Gen.Const(""), Gen.Const("tcp:"), Gen.Const("TCP:"), Gen.Const("np:"), Gen.Const("lpc:"), Gen.Const("admin:")),
        Gen.OneOf(
            Gen.Char["abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-"].Array[1, 20].Select(cs => new string(cs)),
            Gen.OneOf(Gen.Const("localhost"), Gen.Const("127.0.0.1"), Gen.Const("[::1]"), Gen.Const("."), Gen.Const("(local)"), Gen.Const(Machine), Gen.Const("danny-pc"))),
        Gen.OneOf(Gen.Const(""), Gen.Int[1, 65535].Select(port => "," + port), Gen.Int[1, 65535].Select(port => ", " + port),
            Gen.Char["abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_"].Array[1, 16].Select(cs => "\\" + new string(cs))))
        .Select((protocol, host, rest) => protocol + host + rest);

    [Fact]
    [Trait("Category", "fast")]
    public void A_server_name_read_from_its_own_spelling_is_itself()
    {
        DataSource.Sample(source => ServerName.Of(source, Machine) is var name && ServerName.Of(name.ToString(), Machine) == name,
            print: source => source + " is spelled " + ServerName.Of(source, Machine) + ", which reads as " + ServerName.Of(ServerName.Of(source, Machine).ToString(), Machine));
        Assert.Equal(ServerName.Of("(localdb)\\mssqllocaldb", Machine), ServerName.Of(ServerName.Of("(LocalDB)\\MSSQLLocalDB", Machine).ToString(), Machine));
        Assert.Equal(ServerName.Of("dev-sql\\pipe\\sql\\query", Machine), ServerName.Of(ServerName.Of("np:\\\\DEV-SQL\\pipe\\sql\\query", Machine).ToString(), Machine));
    }

    /// <summary>The spellings .dbchange/copies.json rows and R15 used before the kernel held the type (io/Substrate.cs and io/SqlServer.cs at ffaf717c), each pinned.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("127.0.0.1,11433", "localhost,11433", "localhost")]
    [InlineData("tcp:127.0.0.1,1433", "localhost,1433", "localhost")]
    [InlineData("DEV-SQL", "dev-sql", "dev-sql")]
    [InlineData("", "localhost", "localhost")]
    [InlineData(".", "localhost", "localhost")]
    [InlineData("(local)", "localhost", "localhost")]
    [InlineData("[::1],1433", "localhost,1433", "localhost")]
    [InlineData("tcp:DEV-SQL.corp.example, 1433", "dev-sql.corp.example,1433", "dev-sql.corp.example")]
    [InlineData("np:\\\\DEV-SQL\\pipe\\sql\\query", "dev-sql\\pipe\\sql\\query", "dev-sql")]
    [InlineData("(localdb)\\MSSQLLocalDB", "(localdb)\\mssqllocaldb", "(localdb)")]
    [InlineData(Machine + "\\SQLEXPRESS", "localhost\\sqlexpress", "localhost")]
    [InlineData("tcp:danny-pc", "localhost", "localhost")]
    [InlineData("[::ffff:192.0.2.10],1433", "::ffff:192.0.2.10,1433", "::ffff:192.0.2.10")]
    public void A_data_source_is_spelled_as_the_registry_rows_already_carry_it(string dataSource, string spelled, string host)
    {
        var name = ServerName.Of(dataSource, Machine);

        Assert.Equal((spelled, host), (name.ToString(), name.Host.ToString()));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Two_spellings_of_one_server_are_one_server_name_and_two_ports_on_one_host_share_the_host()
    {
        Assert.Equal(ServerName.Of("tcp:127.0.0.1,11433", Machine), ServerName.Of("LOCALHOST, 11433", Machine));
        Assert.NotEqual(ServerName.Of("127.0.0.1,11433", Machine), ServerName.Of("127.0.0.1,1433", Machine));
        Assert.Equal(ServerName.Of("127.0.0.1,11433", Machine).Host, ServerName.Of("127.0.0.1,1433", Machine).Host);
        Assert.Equal(Host.Localhost, ServerName.Of(Machine.ToLowerInvariant(), Machine).Host);
        Assert.Equal(Host.LocalDb, ServerName.Of("(localdb)\\v11.0", Machine).Host);
    }

    /// <summary>A host as dbchange/environments.json names an environment's: the host alone, since a port, an instance, a protocol or white space belongs to a connection's data source.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("DEV-SQL.corp.example", "dev-sql.corp.example")]
    [InlineData(" dev-sql ", "dev-sql")]
    [InlineData("192.0.2.10", "192.0.2.10")]
    [InlineData("[::1]", "localhost")]
    [InlineData("127.0.0.1", "localhost")]
    [InlineData("fe80::1", "fe80::1")]
    [InlineData("[FE80::1]", "fe80::1")]
    [InlineData("(localdb)", "(localdb)")]
    [InlineData("(LocalDB)", "(localdb)")]
    [InlineData("SQL_PROD01", "sql_prod01")]
    public void An_environment_s_host_is_read_in_lower_case_and_this_machine_as_localhost(string text, string host) =>
        Assert.Equal(host, Expect.Value(Host.Of("environments.dev.host in dbchange/environments.json", text)).ToString());

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("dev-sql,1433")]
    [InlineData("dev-sql\\sql2022")]
    [InlineData("tcp:dev-sql")]
    [InlineData("dev sql")]
    [InlineData("")]
    [InlineData("-dev-sql")]
    [InlineData("abc:def")]
    [InlineData("(local)")]
    [InlineData("dev\u0001sql")]
    [InlineData(null)]
    public void An_environment_s_host_with_a_port_an_instance_a_protocol_or_white_space_is_refused(string? text)
    {
        var error = Expect.Failed(Host.Of("environments.dev.host in dbchange/environments.json", text), "environments.host");

        Assert.StartsWith("environments.dev.host in dbchange/environments.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_default_host_or_server_name_is_neither()
    {
        Assert.Throws<InvalidOperationException>(() => default(Host).ToString());
        Assert.Throws<InvalidOperationException>(() => default(ServerName).ToString());
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Server_names_order_as_their_spellings_do_ordinally()
    {
        var names = new[] { "tcp:dev-sql,1433", "(localdb)\\MSSQLLocalDB", "127.0.0.1,11433", "prod-sql" }.Select(source => ServerName.Of(source, Machine));

        Assert.Equal(["(localdb)\\mssqllocaldb", "dev-sql,1433", "localhost,11433", "prod-sql"], SortedArray.Of(names).Select(n => n.ToString()));
    }
}
