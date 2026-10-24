using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

public sealed class SqlClientOutsystemsMetadataReader : IOutsystemsMetadataReader, IMetadataSnapshotDiagnostics
{
    private readonly IAdvancedSqlScriptProvider _scriptProvider;
    private readonly SqlExecutionOptions _options;
    private readonly ILogger<SqlClientOutsystemsMetadataReader> _logger;
    private readonly MetadataContractOverrides _contractOverrides;
    private readonly MetadataSnapshotRunner _runner;

    public SqlClientOutsystemsMetadataReader(
        IDbConnectionFactory connectionFactory,
        IAdvancedSqlScriptProvider scriptProvider,
        SqlExecutionOptions? options = null,
        ILogger<SqlClientOutsystemsMetadataReader>? logger = null,
        IDbCommandExecutor? commandExecutor = null,
        MetadataContractOverrides? contractOverrides = null,
        ILoggerFactory? loggerFactory = null,
        ITaskProgressAccessor? progressAccessor = null)
    {
        if (connectionFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        _scriptProvider = scriptProvider ?? throw new ArgumentNullException(nameof(scriptProvider));
        _options = options ?? SqlExecutionOptions.Default;
        _logger = logger ?? NullLogger<SqlClientOutsystemsMetadataReader>.Instance;
        _contractOverrides = contractOverrides ?? MetadataContractOverrides.Strict;

        var processors = MetadataResultSetProcessorFactory.Default.Create(_contractOverrides, loggerFactory);
        var executor = commandExecutor ?? DbCommandExecutor.Instance;
        var runnerLogger = loggerFactory?.CreateLogger<MetadataSnapshotRunner>()
            ?? NullLogger<MetadataSnapshotRunner>.Instance;

        _runner = new MetadataSnapshotRunner(
            connectionFactory,
            executor,
            processors,
            _options,
            runnerLogger,
            progressAccessor);

        LogContractOverrides();
    }

    public async Task<Result<OutsystemsMetadataSnapshot>> ReadAsync(
        AdvancedSqlRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var script = _scriptProvider.GetScript();
        if (string.IsNullOrWhiteSpace(script))
        {
            _logger.LogError("OutSystems metadata script provider returned an empty script.");
            return Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.metadata.script.missing",
                "Metadata extraction script was empty."));
        }

        return await _runner.ExecuteAsync(script, request, cancellationToken).ConfigureAwait(false);
    }

    MetadataRowSnapshot? IMetadataSnapshotDiagnostics.LastFailureRowSnapshot
        => ((IMetadataSnapshotDiagnostics)_runner).LastFailureRowSnapshot;

    private void LogContractOverrides()
    {
        if (!_contractOverrides.HasOverrides)
        {
            return;
        }

        foreach (var pair in _contractOverrides.OptionalColumns)
        {
            var columnList = string.Join(", ", pair.Value);
            _logger.LogInformation(
                "Metadata contract override active for result set {ResultSet}. Optional columns: {Columns}.",
                pair.Key,
                columnList);
        }
    }

}
