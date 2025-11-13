using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Configuration;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.TestSupport;

namespace Osm.Etl.Integration.Tests;

[Collection("SqlServerCollection")]
public sealed class StaticSeedScriptExecutionTests
{
    private readonly SqlServerFixture _fixture;

    public StaticSeedScriptExecutionTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [DockerFact]
    public async Task StaticSeedScript_WithCaseSensitiveColumnNames_ExecutesSuccessfully()
    {
        var masterBuilder = new SqlConnectionStringBuilder(_fixture.DatabaseConnectionString)
        {
            InitialCatalog = "master"
        };

        var masterConnectionString = masterBuilder.ConnectionString;
        var databaseName = $"StaticSeeds_{Guid.NewGuid():N}";

        var databaseCreated = false;

        await using (var masterConnection = new SqlConnection(masterConnectionString))
        {
            await masterConnection.OpenAsync();
            await using var createCommand = masterConnection.CreateCommand();
            createCommand.CommandText = $"CREATE DATABASE [{databaseName}] COLLATE Latin1_General_CS_AS;";
            await createCommand.ExecuteNonQueryAsync();
            databaseCreated = true;
        }

        var databaseBuilder = new SqlConnectionStringBuilder(_fixture.DatabaseConnectionString)
        {
            InitialCatalog = databaseName
        };

        var databaseConnectionString = databaseBuilder.ConnectionString;

        if (!databaseCreated)
        {
            throw new InvalidOperationException($"Failed to create case-sensitive database '{databaseName}'.");
        }

        try
        {
            await CreateCaseSensitiveTableAsync(databaseConnectionString);

            var definition = new StaticEntitySeedTableDefinition(
                Module: "CaseSensitive",
                LogicalName: "Status",
                Schema: "dbo",
                PhysicalName: "CS_STATUS",
                EffectiveName: "CS_STATUS",
                Columns: ImmutableArray.Create(
                    new StaticEntitySeedColumn("Id", "ID", "Id", "Identifier", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                    new StaticEntitySeedColumn("Name", "NAME", "Name", "Text", 50, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false),
                    new StaticEntitySeedColumn("IsActive", "ISACTIVE", "IsActive", "Boolean", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

            var rows = ImmutableArray.Create(
                StaticEntityRow.Create(new object?[] { 1, "Active", true }));

            var tableData = StaticEntityTableData.Create(definition, rows);
            var generator = CreateGenerator();
            var script = generator.Generate(new[] { tableData }, StaticSeedSynchronizationMode.NonDestructive);

            await ExecuteScriptAsync(databaseConnectionString, script);

            await using var verificationConnection = new SqlConnection(databaseConnectionString);
            await verificationConnection.OpenAsync();

            await using var command = verificationConnection.CreateCommand();
            command.CommandText = "SELECT [Name], [IsActive] FROM dbo.CS_STATUS WHERE [Id] = 1;";
            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync(), "Expected a row to be seeded.");
            Assert.Equal("Active", reader.GetString(0));
            Assert.True(reader.GetBoolean(1));
        }
        finally
        {
            if (databaseCreated)
            {
                await using var dropConnection = new SqlConnection(masterConnectionString);
                await dropConnection.OpenAsync();

                await using var dropCommand = dropConnection.CreateCommand();
                dropCommand.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];";
                await dropCommand.ExecuteNonQueryAsync();
            }
        }
    }

    private static StaticEntitySeedScriptGenerator CreateGenerator()
    {
        var literalFormatter = new SqlLiteralFormatter();
        var sqlBuilder = new StaticSeedSqlBuilder(literalFormatter);
        var templateService = new StaticEntitySeedTemplateService();
        return new StaticEntitySeedScriptGenerator(templateService, sqlBuilder);
    }

    private static async Task CreateCaseSensitiveTableAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE dbo.CS_STATUS
(
    [Id] INT NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(50) NOT NULL,
    [IsActive] BIT NOT NULL
);
""";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteScriptAsync(string connectionString, string script)
    {
        foreach (var batch in SplitSqlBatches(script))
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandType = System.Data.CommandType.Text;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<string> SplitSqlBatches(string script)
    {
        using var reader = new StringReader(script);
        var builder = new System.Text.StringBuilder();
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
