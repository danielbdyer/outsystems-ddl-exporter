using Microsoft.Data.SqlClient;

namespace Osm.Pipeline.Sql;

public sealed record SqlConnectionOptions(
    SqlAuthenticationMethod? AuthenticationMethod,
    bool? TrustServerCertificate,
    string? ApplicationName,
    string? AccessToken)
{
    public static SqlConnectionOptions Default { get; } = new(null, null, null, null);
}
