using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChangeLite;

public abstract record Measured
{
    public sealed record Answered(long Value) : Measured;

    public sealed record Refused(int Number) : Measured;

    public sealed record TimedOut(int Seconds) : Measured;

    public sealed record Failed(int Number, string Message) : Measured;
}

public sealed class Session : IDisposable
{
    public const int CommandSeconds = 30;
    public const int LockMilliseconds = 5000;

    private readonly SqlConnection connection;
    private readonly QueryLog log;

    private Session(SqlConnection connection, QueryLog log)
    {
        this.connection = connection;
        this.log = log;
        Server = connection.DataSource;
        Database = connection.Database;
    }

    public string Server { get; }

    public string Database { get; }

    public static Session Open(string connectionString, QueryLog log, CancellationToken cancel, string subject)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { ApplicationName = "dbchange-lite" };
        var connection = new SqlConnection(builder.ConnectionString);
        try
        {
            connection.OpenAsync(cancel).GetAwaiter().GetResult();
        }
        catch (SqlException e)
        {
            connection.Dispose();
            throw Stop.Of("unreachable", "environment.unreachable", subject, "SQL Server did not answer for " + subject + ": Msg " + e.Number.ToString(CultureInfo.InvariantCulture) + ", " + e.Message.ReplaceLineEndings(" "),
                "Check the connection the environments file references, this login's access and the network, then run the verb again.");
        }
        catch (InvalidOperationException e)
        {
            connection.Dispose();
            throw Stop.Of("configuration-refused", "environment.connection", subject, "The connection for " + subject + " is not usable: " + e.Message, "Correct the connection the environments file references.");
        }

        var session = new Session(connection, log);
        session.Execute("SET LOCK_TIMEOUT " + LockMilliseconds.ToString(CultureInfo.InvariantCulture));
        return session;
    }

    public Measured Scalar(string query)
    {
        OneSelect(query);
        var clock = Stopwatch.StartNew();
        try
        {
            using var command = new SqlCommand(query, connection) { CommandTimeout = CommandSeconds };
            var value = command.ExecuteScalar();
            var answer = value is null or DBNull ? (long?)null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            log.Add(Server, Database, query, clock.ElapsedMilliseconds, "answered");
            return answer is { } v ? new Measured.Answered(v) : new Measured.Failed(0, "no row");
        }
        catch (SqlException e)
        {
            log.Add(Server, Database, query, clock.ElapsedMilliseconds, "Msg " + e.Number.ToString(CultureInfo.InvariantCulture));
            return e.Number switch
            {
                229 or 230 or 297 => new Measured.Refused(e.Number),
                -2 or 1222 => new Measured.TimedOut(CommandSeconds),
                _ => new Measured.Failed(e.Number, e.Message.ReplaceLineEndings(" ")),
            };
        }
    }

    public IReadOnlyList<IReadOnlyList<string>> Rows(string query)
    {
        OneSelect(query);
        var clock = Stopwatch.StartNew();
        var rows = new List<IReadOnlyList<string>>();
        try
        {
            using var command = new SqlCommand(query, connection) { CommandTimeout = CommandSeconds };
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "").ToList());
            }
        }
        catch (SqlException e)
        {
            log.Add(Server, Database, query, clock.ElapsedMilliseconds, "Msg " + e.Number.ToString(CultureInfo.InvariantCulture));
            throw Stop.Of("unreachable", "environment.catalog-read", Server, Server + " did not answer a catalog read of " + Database + ": Msg " + e.Number.ToString(CultureInfo.InvariantCulture) + ", " + e.Message.ReplaceLineEndings(" "),
                e.Number is 1222 or -2 ? "Run the verb again when no schema change holds a lock on " + Database + "." : "Check this login's VIEW DEFINITION on " + Database + ", then run the verb again.");
        }

        log.Add(Server, Database, query, clock.ElapsedMilliseconds, "answered " + rows.Count.ToString(CultureInfo.InvariantCulture) + " rows");
        return rows;
    }

    public void Execute(string batch)
    {
        var clock = Stopwatch.StartNew();
        using var command = new SqlCommand(batch, connection) { CommandTimeout = 600 };
        command.ExecuteNonQuery();
        log.Add(Server, Database, batch, clock.ElapsedMilliseconds, "executed");
    }

    public string Collation() => Rows("SELECT collation_name FROM sys.databases WHERE name = DB_NAME()").FirstOrDefault()?[0] ?? "";

    public string SchemaFingerprint() => Fingerprint.OfString(string.Join('\n',
        Rows("SELECT object_id, type, CONVERT(varchar(33), modify_date, 126) FROM sys.objects WHERE is_ms_shipped = 0 ORDER BY object_id").Select(r => string.Join('\t', r))));

    public (string Version, string Edition, int CompatibilityLevel) ServerInfo()
    {
        var row = Rows("SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')), CONVERT(nvarchar(128), SERVERPROPERTY('Edition')), compatibility_level FROM sys.databases WHERE name = DB_NAME()")[0];
        return (row[0], row[1], int.Parse(row[2], CultureInfo.InvariantCulture));
    }

    public IReadOnlyList<string> CdcTracked() =>
        Rows("SELECT s.name, t.name FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id WHERE t.is_tracked_by_cdc = 1 ORDER BY s.name, t.name")
            .Select(r => Names.Quote(r[0], r[1])).ToList();

    public static void OneSelect(string query)
    {
        var script = DeployScripts.Parse(query);
        if (script.Batches.Count != 1 || script.Batches[0].Statements is not [SelectStatement { Into: null }])
        {
            throw new InvalidOperationException("dbchange-lite composed a statement that is not one SELECT: " + query);
        }
    }

    public void Dispose() => connection.Dispose();
}

public static class Measure
{
    public static (bool? Fires, string ReadBy, Measured Result) RowPresence(Session session, string schema, string table, string exactQuery)
    {
        var exact = session.Scalar(exactQuery);
        if (exact is not Measured.Refused)
        {
            return (exact is Measured.Answered a ? a.Value > 0 : null, "exists", exact);
        }

        var partitions = session.Scalar(PartitionsQuery(schema, table));
        return partitions switch
        {
            Measured.Answered { Value: var rows } => (rows > 0, "sys.partitions", partitions),
            Measured.Failed { Number: 0 } => (null, "sys.partitions", new Measured.Failed(0, "sys.partitions shows this login no partition of " + Names.Quote(schema, table))),
            _ => (null, "sys.partitions", partitions),
        };
    }

    public static string PartitionsQuery(string schema, string table) =>
        "SELECT SUM(p.rows) FROM sys.partitions AS p JOIN sys.objects AS o ON o.object_id = p.object_id JOIN sys.schemas AS s ON s.schema_id = o.schema_id WHERE s.name = "
        + Names.Literal(schema) + " AND o.name = " + Names.Literal(table) + " AND p.index_id IN (0, 1)";
}
