using System;
using System.Data;
using System.IO;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// io/ServerProbe with no SQL Server: which commands the observer on SqlClient's WriteCommandBefore event refuses, and what a refusal
/// becomes when DacFx does not catch it. DriftTests' trace of the read-only principal holds the published dbchange to sending none.
/// </summary>
public sealed class ServerProbeTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("dbchange-probe-").FullName;

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    /// <summary>
    /// DacFx 170.5.96's call (ReliableConnectionHelper.GetServerPropertiesFromConnection) and the other ways a command calls xp_msver
    /// alone, a batch's first statement without EXEC among them, are refused; xp_instance_regread, sp_executesql, a SELECT that quotes the name, a procedure whose name merely contains it and
    /// a batch that holds a second statement pass unchanged.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(CommandType.StoredProcedure, "xp_msver", true)]
    [InlineData(CommandType.StoredProcedure, "XP_MSVER", true)]
    [InlineData(CommandType.StoredProcedure, "master.dbo.xp_msver", true)]
    [InlineData(CommandType.StoredProcedure, "[sys].[xp_msver]", true)]
    [InlineData(CommandType.Text,
        "exec xp_msver @optname=N'Platform',@optname=N'WindowsVersion',@optname=N'ProcessorCount',@optname=N'PhysicalMemory',"
        + "@optname=N'FileDescription'", true)]
    [InlineData(CommandType.Text, "EXECUTE master.sys.xp_msver N'ProcessorCount';", true)]
    [InlineData(CommandType.Text, "\n  EXEC [xp_msver]\n", true)]
    [InlineData(CommandType.Text, "xp_msver N'ProcessorCount'", true)]
    [InlineData(CommandType.StoredProcedure, "xp_instance_regread", false)]
    [InlineData(CommandType.StoredProcedure, "master.dbo.xp_instance_regread", false)]
    [InlineData(CommandType.Text,
        "EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',N'Software\\Microsoft\\MSSQLServer\\MSSQLServer',N'DefaultData'",
        false)]
    [InlineData(CommandType.StoredProcedure, "sp_executesql", false)]
    [InlineData(CommandType.Text, "exec sp_executesql N'SELECT 1 WHERE @p = 1', N'@p int', @p = 1", false)]
    [InlineData(CommandType.Text, "SELECT SERVERPROPERTY('EngineEdition');", false)]
    [InlineData(CommandType.Text, "SELECT N'xp_msver' AS name;", false)]
    [InlineData(CommandType.StoredProcedure, "dbo.usp_xp_msver_report", false)]
    [InlineData(CommandType.StoredProcedure, "xp_msver_wrapper", false)]
    [InlineData(CommandType.Text, "EXEC dbo.xp_msver_wrapper;", false)]
    [InlineData(CommandType.Text, "SELECT 1; EXEC xp_msver;", false)]
    [InlineData(CommandType.Text, "EXEC xp_msver N'ProcessorCount' (", false)]
    [InlineData(CommandType.TableDirect, "xp_msver", false)]
    [InlineData(CommandType.Text, "", false)]
    public void The_observer_refuses_exactly_an_EXEC_of_xp_msver_and_passes_every_other_command(
        CommandType type, string text, bool refused) =>
        Assert.Equal(refused, ServerProbe.Refuses(type, text));

    /// <summary>
    /// DacFx 170.5.96 catches the refusal and plans without the host's figures; a release that lets it escape fails the read as
    /// dacfx.failed, the message naming xp_msver and the reason, never as internal.unexpected and never silently.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_refusal_DacFx_does_not_catch_is_dacfx_failed_naming_xp_msver()
    {
        var copy = new SqlServer.Copy(CopyName.Make("host", 1, 0x0a1b2c3d), "Server=localhost,11433;User ID=sa", scratch);

        var error = Expect.Failed(DacFx.Guard<int>(() => throw ServerProbe.Refusal(), failure => DacFx.Failed(copy, failure)),
            "dacfx.failed");

        Assert.Contains("refused DacFx's EXEC of xp_msver before SqlClient sent it", error.Message, StringComparison.Ordinal);
        Assert.Contains("sends no EXEC", error.Message, StringComparison.Ordinal);
    }
}
