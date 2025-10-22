using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Osm.Etl.Integration.Tests;

public sealed class SqlServerFixture : IAsyncLifetime, IAsyncDisposable
{
    private const string DatabaseName = "OutsystemsIntegration";
    private readonly bool _dockerAvailable;
    private readonly string _skipReason;
    private readonly MsSqlTestcontainer? _container;
    private int _disposed;
    private string? _databaseConnectionString;

    public SqlServerFixture()
    {
        _dockerAvailable = DockerTestHelper.TryEnsureDocker(out var skipReason);
        _skipReason = skipReason;

        if (!_dockerAvailable)
        {
            return;
        }

        var configuration = new MsSqlTestcontainerConfiguration
        {
            Password = "yourStrong(!)Password"
        };

        _container = new TestcontainersBuilder<MsSqlTestcontainer>()
            .WithDatabase(configuration)
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU15-ubuntu-22.04")
            .WithCleanUp(true)
            .Build();
    }

    public string DatabaseConnectionString
    {
        get
        {
            if (!_dockerAvailable)
            {
                throw new InvalidOperationException(_skipReason);
            }

            return _databaseConnectionString ?? throw new InvalidOperationException("SQL Server container has not been initialized.");
        }
    }

    public async Task InitializeAsync()
    {
        if (!_dockerAvailable || _container is null)
        {
            return;
        }

        await _container.StartAsync().ConfigureAwait(false);

        var masterBuilder = new SqlConnectionStringBuilder(_container.ConnectionString)
        {
            TrustServerCertificate = true
        };

        var masterConnection = masterBuilder.ConnectionString;
        var seedScriptPath = Path.GetFullPath("tests/Fixtures/sql/model.edge-case.seed.sql");
        var seedScript = await File.ReadAllTextAsync(seedScriptPath).ConfigureAwait(false);
        await ExecuteScriptAsync(masterConnection, seedScript).ConfigureAwait(false);

        masterBuilder.InitialCatalog = DatabaseName;
        _databaseConnectionString = masterBuilder.ConnectionString;

        var advancedSqlPath = Path.GetFullPath("src/AdvancedSql/outsystems_model_export.sql");
        var advancedScript = await File.ReadAllTextAsync(advancedSqlPath).ConfigureAwait(false);
        await ExecuteScriptAsync(
            _databaseConnectionString,
            advancedScript,
            static command =>
            {
                var moduleParam = command.CreateParameter();
                moduleParam.ParameterName = "@ModuleNamesCsv";
                moduleParam.SqlDbType = SqlDbType.NVarChar;
                moduleParam.Value = string.Empty;
                command.Parameters.Add(moduleParam);

                var includeParam = command.CreateParameter();
                includeParam.ParameterName = "@IncludeSystem";
                includeParam.SqlDbType = SqlDbType.Bit;
                includeParam.Value = 0;
                command.Parameters.Add(includeParam);

                var activeParam = command.CreateParameter();
                activeParam.ParameterName = "@OnlyActiveAttributes";
                activeParam.SqlDbType = SqlDbType.Bit;
                activeParam.Value = 0;
                command.Parameters.Add(activeParam);
            }).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await DisposeContainerAsync().ConfigureAwait(false);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeContainerAsync().ConfigureAwait(false);
    }

    private ValueTask DisposeContainerAsync()
    {
        if (!_dockerAvailable || _container is null)
        {
            return ValueTask.CompletedTask;
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return _container.DisposeAsync();
    }

    private static async Task ExecuteScriptAsync(string connectionString, string script, Action<SqlCommand>? configure = null)
    {
        foreach (var batch in SplitSqlBatches(script))
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandType = CommandType.Text;
            configure?.Invoke(command);
            try
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            finally
            {
                command.Parameters.Clear();
            }
        }
    }

    private static IEnumerable<string> SplitSqlBatches(string script)
    {
        using var reader = new StringReader(script);
        var builder = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }
                continue;
            }

            builder.AppendLine(line);
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }
}
