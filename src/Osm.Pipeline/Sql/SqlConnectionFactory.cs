using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Osm.Pipeline.Sql;

public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly SqlConnectionOptions _options;

    public SqlConnectionFactory(string connectionString, SqlConnectionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        _connectionString = connectionString.Trim();
        _options = options ?? SqlConnectionOptions.Default;
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        if (_options.AuthenticationMethod.HasValue)
        {
            builder.Authentication = _options.AuthenticationMethod.Value;
        }

        if (_options.TrustServerCertificate.HasValue)
        {
            builder.TrustServerCertificate = _options.TrustServerCertificate.Value;
        }

        if (!string.IsNullOrWhiteSpace(_options.ApplicationName))
        {
            builder.ApplicationName = _options.ApplicationName;
        }

        var connection = new SqlConnection(builder.ConnectionString);
        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            connection.AccessToken = _options.AccessToken;
        }

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
