using System;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using Estate.Kernel;
using Microsoft.Data.SqlClient;

namespace Estate.Io;

/// <summary>
/// A SQL Server connection string, read and written here alone and only by SqlClient's own grammar (SqlConnectionStringBuilder): a named
/// environment's, which its reference resolves to; the scratch server's, from ESTATE_SQL, ~/.estate/sql.env or LocalDB; and a copy's,
/// the scratch server's with the copy's database. A text SqlClient reads nothing from is connection.malformed, its text withheld,
/// since it can hold a password. For TLS, estate adds no keyword to a named environment's connection: its reference's own Encrypt,
/// TrustServerCertificate and HostNameInCertificate reach the server as written, and SqlClient's defaults stand where it names none.
/// The estate-sql container's connection alone trusts the server's certificate, which SQL Server generated for itself.
/// </summary>
internal static class ConnectionString
{
    /// <summary>A password set in a connection string, however spelled or spaced.</summary>
    internal static readonly Regex Password = new(@"(?:password|pwd)\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The connection string <paramref name="text"/> is, as SqlClient reads it; else connection.malformed, led by <paramref name="subject"/>,
    /// quoting nothing of the text, with <paramref name="remedy"/>, which names where the text is written.
    /// </summary>
    internal static Result<SqlConnectionStringBuilder> Parse(string subject, string text, string remedy)
    {
        try
        {
            return new SqlConnectionStringBuilder(text);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException)
        {
            return new Error("connection.malformed", subject + " is no connection string SqlClient reads; its text is withheld.", remedy);
        }
    }

    /// <summary>Whether a text is a literal connection string: it sets a password, or SqlClient's grammar reads one of its keywords from it (Server, User ID).</summary>
    internal static bool IsConnection(string text)
    {
        try
        {
            return Password.IsMatch(text) || new DbConnectionStringBuilder { ConnectionString = text }.Keys.Cast<string>().Any(new SqlConnectionStringBuilder().ContainsKey);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The connection as estate opens it: the caller's integrated identity when it names no other, and estate as the application unless it names one; nothing else is added.</summary>
    internal static string WithDefaults(SqlConnectionStringBuilder connection)
    {
        if (!connection.ShouldSerialize("Integrated Security") && connection.UserID.Length == 0 && connection.Authentication == SqlAuthenticationMethod.NotSpecified)
        {
            connection.IntegratedSecurity = true;
        }

        if (!connection.ShouldSerialize("Application Name"))
        {
            connection.ApplicationName = "estate";
        }

        return connection.ConnectionString;
    }

    /// <summary>A connection estate made to the database <paramref name="catalog"/> instead of the one it names.</summary>
    internal static string WithCatalog(string connection, string catalog) => new SqlConnectionStringBuilder(connection) { InitialCatalog = catalog }.ConnectionString;

    /// <summary>A connection estate made, opened afresh each time rather than from SqlClient's pool: for CREATE and DROP DATABASE on master, which no pooled session may outlive.</summary>
    internal static string Unpooled(string connection) => new SqlConnectionStringBuilder(connection) { Pooling = false }.ConnectionString;

    /// <summary>The database a connection estate made names.</summary>
    internal static string CatalogOf(string connection) => new SqlConnectionStringBuilder(connection).InitialCatalog;

    /// <summary>The server a connection string names, as this machine spells it (kernel/ServerName.cs).</summary>
    internal static ServerName ServerOf(SqlConnectionStringBuilder connection) => ServerName.Of(connection.DataSource, Environment.MachineName);

    /// <summary>The estate-sql container's connection: SQL Server on the loopback address at the port ~/.estate/sql.env gives, as sa, trusting the certificate SQL Server generated for itself.</summary>
    internal static string Container(string port, string password) =>
        new SqlConnectionStringBuilder { DataSource = "127.0.0.1," + port, UserID = "sa", Password = password, TrustServerCertificate = true }.ConnectionString;
}
