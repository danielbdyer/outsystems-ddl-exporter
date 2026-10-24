using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace DbChangeLite;

public static class LocalServer
{
    private static readonly Regex Go = new(@"^\s*GO(\s+\d+)?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static string Describe() => Environment.GetEnvironmentVariable("DBCHANGE_SQL") is { Length: > 0 } ? "the server DBCHANGE_SQL names"
        : File.Exists(EnvFile()) ? "the SQL Server " + EnvFile() + " names" : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "LocalDB" : "no local server: DBCHANGE_SQL is unset and " + EnvFile() + " is absent";

    public static string ConnectionString()
    {
        if (Environment.GetEnvironmentVariable("DBCHANGE_SQL") is { Length: > 0 } given)
        {
            return given;
        }

        if (File.Exists(EnvFile()))
        {
            var values = File.ReadAllLines(EnvFile()).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.Ordinal);
            return "Server=127.0.0.1," + values.GetValueOrDefault("DBCHANGE_SQL_PORT", "11434") + ";User ID=sa;Password=" + values.GetValueOrDefault("MSSQL_SA_PASSWORD", "") + ";Encrypt=True;TrustServerCertificate=True";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;Encrypt=False";
        }

        throw Stop.Of("unreachable", "server.none", "local server", Describe() + ".", "Set DBCHANGE_SQL to a connection string for a SQL Server on this machine, or run ci/sql.sh up.");
    }

    public static string? Try(Run run)
    {
        try
        {
            using var master = Session.Open(ConnectionString(), run.Log, run.Cancel, "local server");
            var (version, edition, _) = master.ServerInfo();
            return master.Server + ": SQL Server " + version + " " + edition;
        }
        catch (Stop)
        {
            return null;
        }
    }

    public static void Refuse(string connectionString, Environments environments)
    {
        var source = Baselines.Connection(connectionString, "local server").DataSource;
        if (!Hosts.IsLocal(source))
        {
            throw Stop.Of("refused-by-name", "server.not-local", source, "prove writes only to a server on this machine, and " + Hosts.Of(source) + " is not one.", "Set DBCHANGE_SQL to a SQL Server on this machine, or use LocalDB.");
        }

        foreach (var environment in environments.All.Where(e => Hosts.Same(source, e.Host)))
        {
            throw Stop.Of("refused-by-name", "server.named-environment", source, Hosts.Of(source) + " is the host of the environment " + environment.Name + ", and prove never writes to a named environment.", "Set DBCHANGE_SQL to a SQL Server that is no environment's host.");
        }
    }

    public static string CopyName(DateTimeOffset at) => "dbchange_lite_" + at.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

    public static void Sweep(Session master, DateTimeOffset now)
    {
        foreach (var row in master.Rows("SELECT name FROM sys.databases WHERE name LIKE 'dbchange_lite_%' ORDER BY name"))
        {
            var stamp = row[0].Split('_').Skip(2).FirstOrDefault() ?? "";
            if (DateTimeOffset.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var made) && now - made > TimeSpan.FromDays(1))
            {
                Drop(master, row[0]);
            }
        }
    }

    public static void Drop(Session master, string name) =>
        master.Execute("IF DB_ID(" + Names.Literal(name) + ") IS NOT NULL BEGIN ALTER DATABASE " + Names.Quote(name) + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + Names.Quote(name) + "; END");

    public static IReadOnlyList<string> Batches(string script) => Go.Split(script.ReplaceLineEndings("\n")).Select(b => b.Trim()).Where(b => b.Length > 0 && !Regex.IsMatch(b, @"^\d+$", RegexOptions.CultureInvariant)).ToList();

    private static string EnvFile() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dbchange", "sql.env");
}
