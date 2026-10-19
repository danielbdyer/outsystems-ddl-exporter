using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Pipeline.Sql;
using Osm.Pipeline.UatUsers;
using Osm.Pipeline.UatUsers.Steps;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class ValidateUserMapStepTests
{
    [Fact]
    public async Task ExecuteAsync_LogsSuccessWhenMappingsValid()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        var source = UserIdentifier.FromString("100");
        var target = UserIdentifier.FromString("200");
        context.SetAllowedUserIds(new[] { target });
        context.SetOrphanUserIds(new[] { source });
        context.SetUserMap(new[] { new UserMappingEntry(source, target, "approved") });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [source] = new(source, "qa-user", "qa@example.com", "QA User", null, null, null, null)
        });

        var logger = new ListLogger<ValidateUserMapStep>();
        var step = new ValidateUserMapStep(logger);

        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains(logger.Infos, message => message.Contains("User map validated successfully", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenTargetMissingFromAllowedList()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        var source = UserIdentifier.FromString("100");
        var target = UserIdentifier.FromString("999");
        context.SetAllowedUserIds(Array.Empty<UserIdentifier>());
        context.SetOrphanUserIds(new[] { source });
        context.SetUserMap(new[] { new UserMappingEntry(source, target, null) });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [source] = new(source, "qa-user", null, null, null, null, null, null)
        });

        var logger = new ListLogger<ValidateUserMapStep>();
        var step = new ValidateUserMapStep(logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Contains("User map validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Errors, message => message.Contains("not present in the allowed UAT user inventory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenSourceMissingFromQaInventory()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        var source = UserIdentifier.FromString("123");
        var target = UserIdentifier.FromString("200");
        context.SetAllowedUserIds(new[] { target });
        context.SetOrphanUserIds(new[] { source });
        context.SetUserMap(new[] { new UserMappingEntry(source, target, null) });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>());

        var logger = new ListLogger<ValidateUserMapStep>();
        var step = new ValidateUserMapStep(logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Contains(logger.Errors, message => message.Contains("QA user inventory contains zero rows", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("User map validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenDuplicateSourceUserIdsDetected()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        var source = UserIdentifier.FromString("777");
        var target = UserIdentifier.FromString("200");
        context.SetAllowedUserIds(new[] { target });
        context.SetOrphanUserIds(new[] { source });
        context.SetUserMap(new[]
        {
            new UserMappingEntry(source, target, null),
            new UserMappingEntry(source, target, "duplicate")
        });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [source] = new(source, "qa-user", null, null, null, null, null, null)
        });

        var logger = new ListLogger<ValidateUserMapStep>();
        var step = new ValidateUserMapStep(logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(context, CancellationToken.None));

        Assert.Contains("User map validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Errors, message => message.Contains("Duplicate SourceUserId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenMappingMissingTargetUserId()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        var source = UserIdentifier.FromString("321");
        var target = UserIdentifier.FromString("200");
        context.SetAllowedUserIds(new[] { target });
        context.SetOrphanUserIds(new[] { source });
        context.SetUserMap(new[]
        {
            new UserMappingEntry(source, null, "pending")
        });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [source] = new(source, "qa-user", null, null, null, null, null, null)
        });

        var logger = new ListLogger<ValidateUserMapStep>();
        var step = new ValidateUserMapStep(logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(context, CancellationToken.None));

        Assert.Contains("User map validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Errors, message => message.Contains("Mappings missing TargetUserId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenOrphanMissingFromMap()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        var mapped = UserIdentifier.FromString("500");
        var missing = UserIdentifier.FromString("600");
        var target = UserIdentifier.FromString("200");
        context.SetAllowedUserIds(new[] { target });
        context.SetOrphanUserIds(new[] { mapped, missing });
        context.SetUserMap(new[]
        {
            new UserMappingEntry(mapped, target, null)
        });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [mapped] = new(mapped, "mapped", null, null, null, null, null, null),
            [missing] = new(missing, "missing", null, null, null, null, null, null)
        });

        var logger = new ListLogger<ValidateUserMapStep>();
        var step = new ValidateUserMapStep(logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(context, CancellationToken.None));

        Assert.Contains("User map validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Errors, message => message.Contains("Missing mappings for", StringComparison.OrdinalIgnoreCase));
    }

    private static UatUsersContext CreateContext(string root)
    {
        var artifacts = new UatUsersArtifacts(root);
        var uatInventoryPath = Path.Combine(root, "uat.csv");
        File.WriteAllLines(uatInventoryPath, new[] { "Id,Username", "1,uat" });
        var qaInventoryPath = Path.Combine(root, "qa.csv");
        File.WriteAllText(qaInventoryPath, "Id,Username\n1,qa\n");
        return new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            new ThrowingConnectionFactory(),
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(root, "map.csv"),
            uatInventoryPath,
            qaInventoryPath,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");
    }

    private sealed class StubSchemaGraph : IUserSchemaGraph
    {
        public Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ForeignKeyDefinition>>(Array.Empty<ForeignKeyDefinition>());
    }

    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Connection factory should not be used in these tests.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "validate-map-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup issues
            }
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Infos { get; } = new();
        public List<string> Errors { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(message);
            }
            else if (logLevel == LogLevel.Information)
            {
                Infos.Add(message);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose()
            {
            }
        }
    }
}
