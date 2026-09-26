using System;
using Osm.Domain.Configuration;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.ModelIngestion;

public sealed record ModelIngestionOptions(
    ModuleValidationOverrides ValidationOverrides,
    string? MissingSchemaFallback,
    ModelIngestionSqlMetadataOptions? SqlMetadata = null)
{
    public static ModelIngestionOptions Empty { get; }
        = new(ModuleValidationOverrides.Empty, null);
}

public sealed record ModelIngestionSqlMetadataOptions(
    string connectionString,
    SqlConnectionOptions connectionOptions,
    int? commandTimeoutSeconds = null)
{
    public string ConnectionString { get; } = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("Connection string must be provided for SQL metadata enrichment.", nameof(connectionString))
        : connectionString.Trim();

    public SqlConnectionOptions ConnectionOptions { get; } = connectionOptions
        ?? throw new ArgumentNullException(nameof(connectionOptions));

    public int? CommandTimeoutSeconds { get; } = commandTimeoutSeconds;
}
