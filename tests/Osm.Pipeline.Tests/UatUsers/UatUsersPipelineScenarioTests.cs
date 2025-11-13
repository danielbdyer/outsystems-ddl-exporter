using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Pipeline.Sql;
using Osm.Pipeline.UatUsers;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class UatUsersPipelineScenarioTests
{
    [Fact]
    public async Task PipelineProducesArtifactsAndPersistsSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var artifacts = new UatUsersArtifacts(temp.Root);

        var uatInventoryPath = temp.WriteFile(
            "inputs/uat_users.csv",
            string.Join(Environment.NewLine,
            [
                "Id,Username",
                "100,uat-admin",
                "200,uat-owner"
            ]));
        var qaInventoryPath = temp.WriteFile(
            "inputs/qa_users.csv",
            string.Join(Environment.NewLine,
            [
                "Id,Username,EMail,Name,External_Id,Is_Active,Creation_Date,Last_Login",
                "999,qa-admin,qa@example.com,QA Admin,,1,2024-01-01T00:00:00Z,2024-02-01T00:00:00Z",
                "111,qa-owner,owner@example.com,QA Owner,,1,2024-01-02T00:00:00Z,2024-02-02T00:00:00Z"
            ]));

        var mapPath = temp.WriteFile(
            "inputs/custom_map.csv",
            string.Join(Environment.NewLine,
            [
                "SourceUserId,TargetUserId,Rationale",
                "999,200,Legacy administrator",
                "111,100,Legacy owner"
            ]));

        var snapshotPath = temp.Combine("snapshots", "fk.json");

        var createdBy = new UserFkColumn("dbo", "Tasks", "CreatedBy", "FK_Tasks_CreatedBy");
        var updatedBy = new UserFkColumn("dbo", "Tasks", "UpdatedBy", "FK_Tasks_UpdatedBy");

        var schemaGraph = new StubSchemaGraph(new[]
        {
            BuildDefinition(createdBy),
            BuildDefinition(updatedBy)
        });

        var context = new UatUsersContext(
            schemaGraph,
            artifacts,
            new ThrowingConnectionFactory(),
            userSchema: "dbo",
            userTable: "Users",
            userIdColumn: "Id",
            includeColumns: null,
            userMapPath: mapPath,
            uatInventoryPath,
            qaUserInventoryPath: qaInventoryPath,
            snapshotPath: snapshotPath,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "sample-db/v1");

        var provider = new RecordingValueProvider(new Dictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>
        {
            [createdBy] = new Dictionary<UserIdentifier, long>
            {
                [UserIdentifier.FromString("999")] = 2,
                [UserIdentifier.FromString("100")] = 5
            },
            [updatedBy] = new Dictionary<UserIdentifier, long>
            {
                [UserIdentifier.FromString("111")] = 3,
                [UserIdentifier.FromString("200")] = 7
            }
        });

        var pipeline = new UatUsersPipeline(
            NullLoggerFactory.Instance,
            provider,
            new FileUserForeignKeySnapshotStore());

        await pipeline.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(2, provider.RequestedColumnCount);
        Assert.Equal(2, context.UserFkCatalog.Count);
        Assert.Equal(2, context.OrphanUserIds.Count);
        Assert.Contains(context.OrphanUserIds, id => id.NumericValue == 999);
        Assert.Contains(context.OrphanUserIds, id => id.NumericValue == 111);

        Assert.True(File.Exists(snapshotPath));
        var snapshotJson = File.ReadAllText(snapshotPath);
        using var snapshot = JsonDocument.Parse(snapshotJson);
        var allowed = snapshot.RootElement.GetProperty("AllowedUserIds")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Contains("100", allowed);
        Assert.Contains("200", allowed);

        var orphans = snapshot.RootElement.GetProperty("OrphanUserIds")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Contains("999", orphans);
        Assert.Contains("111", orphans);

        var previewPath = temp.Combine("uat-users", "01_preview.csv");
        Assert.True(File.Exists(previewPath));
        var previewLines = File.ReadAllLines(previewPath);
        Assert.Equal(2, previewLines.Length);
        Assert.Contains(
            "dbo.Tasks,CreatedBy,999,200,2",
            previewLines[1],
            StringComparison.OrdinalIgnoreCase);

        var scriptPath = temp.Combine("uat-users", "02_apply_user_remap.sql");
        var script = File.ReadAllText(scriptPath);
        Assert.Contains("CREATE TABLE #UserRemap (SourceUserId INT PRIMARY KEY, TargetUserId INT NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("999", script, StringComparison.Ordinal);
        Assert.Contains("200", script, StringComparison.Ordinal);
        Assert.Contains("-- Pending mappings without TargetUserId", script, StringComparison.Ordinal);

        var mapLines = File.ReadAllLines(mapPath);
        Assert.Equal(3, mapLines.Length);
        Assert.Contains(mapLines, line => line.Contains("111", StringComparison.Ordinal));
        Assert.Contains(mapLines, line => line.Contains("999,200", StringComparison.Ordinal));
        Assert.Equal(mapLines, File.ReadAllLines(temp.Combine("uat-users", "00_user_map.csv")));

        var templatePath = temp.Combine("uat-users", "00_user_map.template.csv");
        Assert.True(File.Exists(templatePath));
        var templateRows = File.ReadAllLines(templatePath);
        Assert.Equal(3, templateRows.Length);
        Assert.Contains(templateRows, row => row.Contains("999", StringComparison.Ordinal));
        Assert.Contains(templateRows, row => row.StartsWith("111,", StringComparison.Ordinal));
        Assert.Contains(templateRows, row => row.Contains(",,", StringComparison.Ordinal));

        var catalogPath = temp.Combine("uat-users", "03_catalog.txt");
        Assert.True(File.Exists(catalogPath));
        var catalogLines = File.ReadAllLines(catalogPath);
        Assert.Contains("dbo.Tasks.CreatedBy  -- FK_Tasks_CreatedBy", catalogLines);
        Assert.Contains("dbo.Tasks.UpdatedBy  -- FK_Tasks_UpdatedBy", catalogLines);

        var secondArtifacts = new UatUsersArtifacts(temp.Root);
        var secondContext = new UatUsersContext(
            schemaGraph,
            secondArtifacts,
            new ThrowingConnectionFactory(),
            userSchema: "dbo",
            userTable: "Users",
            userIdColumn: "Id",
            includeColumns: null,
            userMapPath: mapPath,
            uatInventoryPath,
            qaUserInventoryPath: qaInventoryPath,
            snapshotPath: snapshotPath,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "sample-db/v1");

        var secondPipeline = new UatUsersPipeline(
            NullLoggerFactory.Instance,
            new ThrowingValueProvider(),
            new FileUserForeignKeySnapshotStore());

        await secondPipeline.ExecuteAsync(secondContext, CancellationToken.None);

        Assert.Equal(0, secondContext.OrphanUserIds.Count(id => id.NumericValue == 200));
        Assert.Equal(2, secondContext.OrphanUserIds.Count);
        Assert.True(secondContext.ForeignKeyValueCounts.TryGetValue(createdBy, out var firstColumn));
        Assert.Equal(2L, firstColumn[UserIdentifier.FromString("999")]);
        Assert.True(secondContext.ForeignKeyValueCounts.TryGetValue(updatedBy, out var secondColumn));
        Assert.Equal(3L, secondColumn[UserIdentifier.FromString("111")]);
    }

    private static ForeignKeyDefinition BuildDefinition(UserFkColumn column)
    {
        return new ForeignKeyDefinition(
            column.ForeignKeyName,
            new ForeignKeyTable(column.SchemaName, column.TableName),
            new ForeignKeyTable("dbo", "Users"),
            ImmutableArray.Create(new ForeignKeyColumn(column.ColumnName, "Id")));
    }

    private sealed class RecordingValueProvider : IUserForeignKeyValueProvider
    {
        private readonly IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> _counts;

        public RecordingValueProvider(IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> counts)
        {
            _counts = counts;
        }

        public int RequestedColumnCount { get; private set; }

        public Task<IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>> CollectAsync(
            IReadOnlyList<UserFkColumn> catalog,
            IDbConnectionFactory connectionFactory,
            CancellationToken cancellationToken)
        {
            RequestedColumnCount += catalog.Count;
            return Task.FromResult(_counts);
        }
    }

    private sealed class ThrowingValueProvider : IUserForeignKeyValueProvider
    {
        public Task<IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>> CollectAsync(
            IReadOnlyList<UserFkColumn> catalog,
            IDbConnectionFactory connectionFactory,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Snapshot should have been reused; live collection was not expected.");
        }
    }

    private sealed class StubSchemaGraph : IUserSchemaGraph
    {
        private readonly IReadOnlyList<ForeignKeyDefinition> _definitions;

        public StubSchemaGraph(IReadOnlyList<ForeignKeyDefinition> definitions)
        {
            _definitions = definitions;
        }

        public Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_definitions);
        }
    }

    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("SQL access should not be required in this scenario test.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "uat-users-scenario", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Combine(params string[] components)
        {
            var path = Path.Combine(new[] { Root }.Concat(components).ToArray());
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return path;
        }

        public string WriteFile(string relativePath, string contents)
        {
            var path = Combine(relativePath);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Ignore best-effort cleanup failures.
            }
        }
    }
}
