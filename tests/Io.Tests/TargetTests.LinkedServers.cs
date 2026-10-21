using System;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// R15 over the local server's linked servers (DECISIONS.md, 2026-10-21): a seed, a trigger or a declared witness on a copy reaches
/// every server the copy's local server links to, so the local server's linked servers are compared with the environments' hosts as the
/// local server's own host is, by spelling, then by address. DNS is the resolver TargetTests gives, so no lookup leaves the test.
/// </summary>
public sealed partial class TargetTests
{
    /// <summary>
    /// A local server holding a linked server whose data source names the host env:dev runs on, by its spelling in any case, with an
    /// instance, a port, a protocol or the one trailing dot of a fully qualified name, or by an address of the host, is copy.named-host;
    /// so is one whose data source is empty and whose provider string names the host under Server, Data Source or Addr, in quotes or
    /// none, as an OLE DB or an ODBC linked server names its server. The
    /// refusal names the linked server and the environment, and quotes neither the provider string nor the password it carries; a
    /// linked server to an Access file beside it changes nothing.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    [InlineData("dev-sql", null)]
    [InlineData("DEV-SQL\\INST01", null)]
    [InlineData("tcp:dev-sql.corp.example,1433", null)]
    [InlineData("192.0.2.10", null)]
    [InlineData(null, "Server=dev-sql;Database=Catalog;UID=reader;PWD=")]
    [InlineData("", "Driver={ODBC Driver 18 for SQL Server};Server=tcp:dev-sql.corp.example,1433;UID=reader;PWD=")]
    [InlineData(null, "Data Source = 192.0.2.10,1433;Password=")]
    [InlineData(null, "addr=DEV-SQL;pwd=")]
    [InlineData(null, "Server='dev-sql';UID=reader;PWD=")]
    [InlineData("dev-sql.", null)]
    [InlineData("DEV-SQL.\\INST01", null)]
    [InlineData("tcp:dev-sql.corp.example.,1433", null)]
    public void A_local_server_holding_a_linked_server_to_the_host_an_environment_runs_on_is_refused(string? dataSource,
        string? providerString)
    {
        var root = RepositoryAt(EnvironmentsJson.Of("dev", Unresolved("dev-sql")));
        LocalServer.LinkedServer[] linked =
        [
            new("archive", "C:\\data\\catalog.mdb", null), new("prod-link", dataSource, providerString is null ? null : providerString + Planted),
        ];

        var error = Failed(LocalServer.Unlinked(EnvironmentsAt(root), Server("localhost,11434"), linked, Resolver), "copy.named-host");

        Assert.StartsWith("The local server holds the linked server [prod-link], which names ", error.Message, StringComparison.Ordinal);
        Assert.Contains("the host env:dev runs on", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
    }

    /// <summary>
    /// A local server whose linked servers name no host an environment runs on is cleared: one on another host, by its data source or
    /// its provider string; one to an Access file; one named by an ODBC data source name alone, which names no host; and one with
    /// neither a data source nor a provider string.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("prod-sql.corp.example", null)]
    [InlineData(null, "Server=prod-sql.corp.example;PWD=")]
    [InlineData("C:\\data\\catalog.mdb", null)]
    [InlineData("", "DSN=Catalog")]
    [InlineData(null, null)]
    public void A_local_server_whose_linked_servers_name_no_host_an_environment_runs_on_is_cleared(string? dataSource,
        string? providerString)
    {
        var root = RepositoryAt(EnvironmentsJson.Of("dev", Unresolved("dev-sql")));

        Assert.Equal(Server("localhost,11434"), Value(LocalServer.Unlinked(EnvironmentsAt(root), Server("localhost,11434"),
            [new LocalServer.LinkedServer("prod-link", dataSource, providerString)], Resolver)));
    }
}
