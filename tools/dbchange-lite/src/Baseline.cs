using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace DbChangeLite;

public sealed record Baseline(NamedEnvironment Environment, Session Session, string Database, string Collation, string ExtractPath, Model Model, string SchemaFingerprint, IReadOnlyList<string> CdcTracked) : IDisposable
{
    public void Dispose() => Session.Dispose();
}

public static class Baselines
{
    public static string DatabaseOf(string connectionString, string subject)
    {
        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        return database.Length > 0 ? database
            : throw Stop.Of("configuration-refused", "environment.no-database", subject, "The connection for " + subject + " names no database.", "Add Initial Catalog=<database> to the connection the environments file references.");
    }

    public static Baseline Read(NamedEnvironment environment, Run run)
    {
        var connection = EnvironmentsFile.Resolve(run.Root, environment.Connection);
        var database = DatabaseOf(connection, environment.Name);
        var session = Session.Open(connection, run.Log, run.Cancel, environment.Name);
        try
        {
            var collation = session.Collation();
            var fingerprint = session.SchemaFingerprint();
            var tracked = session.CdcTracked();
            var extract = Cache.Extract(run, environment.Name, fingerprint, () => DacFx.Extract(connection, database, run.Cancel));
            return new Baseline(environment, session, database, collation, extract, Model.Load(extract), fingerprint, tracked);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public static IReadOnlyDictionary<string, string> SqlCmd(NamedEnvironment environment, PublishProfile profile, Run run)
    {
        var values = new Dictionary<string, string>(profile.SqlCmd, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in environment.SqlCmd)
        {
            values[name] = EnvironmentsFile.IsReference(value) ? EnvironmentsFile.Resolve(run.Root, value) : value;
        }

        return values;
    }
}

public static class CdcLedger
{
    public const string Path = "dbchange/ledgers/cdc-tracked.md";
    private static readonly Regex Table = new(@"\[[^\]]+\]\.\[[^\]]+\]", RegexOptions.CultureInvariant);

    public static IReadOnlySet<string> Read(string root)
    {
        var path = System.IO.Path.Combine(root, Path);
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            tracked.UnionWith(File.ReadLines(path).Where(l => l.TrimStart().StartsWith('|')).SelectMany(l => Table.Matches(l).Select(m => m.Value)));
        }

        return tracked;
    }
}
