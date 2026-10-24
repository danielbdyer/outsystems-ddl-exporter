using System;
using System.IO;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class RefusalTests
{
    [Fact]
    [Trait("Category", "fast")]
    public void Prove_refuses_by_name_a_local_server_that_is_an_environments_host_before_it_connects_or_builds()
    {
        using var scratch = new Scratch();
        scratch.Write("dbchange/environments.json", "{ \"environments\": { \"dev\": { \"host\": \"127.0.0.1\", \"connection\": \"env:DBCHANGE_LITE_NONE\", \"profile\": \"profiles/pipeline.publish.xml\" } } }");
        scratch.Write("rows.sql", "PRINT 1;");
        var before = Environment.GetEnvironmentVariable("DBCHANGE_SQL");
        try
        {
            Environment.SetEnvironmentVariable("DBCHANGE_SQL", "Server=127.0.0.1,11433;Integrated Security=true");
            var (exit, answer) = Cli.Run(scratch.Path, "prove", "--env", "dev", "--rows", "rows.sql");
            Assert.Equal(9, exit);
            Assert.Equal("server.named-environment", answer.GetProperty("findings")[0].GetProperty("code").GetString());
            Environment.SetEnvironmentVariable("DBCHANGE_SQL", "Server=db.corp.invalid;Integrated Security=true");
            var (remote, refusal) = Cli.Run(scratch.Path, "prove", "--env", "dev", "--rows", "rows.sql");
            Assert.Equal(9, remote);
            Assert.Equal("server.not-local", refusal.GetProperty("findings")[0].GetProperty("code").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DBCHANGE_SQL", before);
        }
    }
}
