using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class BuildUserMapStep : RemapUsersPipelineStep
{
    public BuildUserMapStep()
        : base("build-user-map")
    {
    }

    protected override async Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?>(context.BuildCommonParameters())
        {
            ["UserTableSchema"] = context.UserTableSchema,
            ["UserTableName"] = context.UserTableName
        };

        await context.SqlRunner.ExecuteAsync(
            "DELETE FROM ctl.UserMap WHERE SourceEnv = @SourceEnv;",
            parameters,
            context.CommandTimeout,
            cancellationToken).ConfigureAwait(false);

        foreach (var rule in context.MatchingRules)
        {
            parameters["MatchReason"] = GetMatchReason(rule);

            var command = rule == RemapUsersMatchRule.Fallback
                ? BuildFallbackCommand(context)
                : BuildInsertCommand(context, rule);

            if (rule == RemapUsersMatchRule.Fallback && !context.FallbackUserId.HasValue)
            {
                throw new InvalidOperationException("Fallback user id must be provided when fallback match rule is enabled.");
            }

            await context.SqlRunner.ExecuteAsync(
                command,
                parameters,
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        var coverage = await context.SqlRunner.QueryAsync(
            "SELECT MatchReason, COUNT_BIG(*) FROM ctl.UserMap WHERE SourceEnv = @SourceEnv GROUP BY MatchReason ORDER BY MatchReason;",
            parameters,
            static record => new UserMapCoverageRow(
                record.GetString(0),
                record.GetInt64(1)),
            context.CommandTimeout,
            cancellationToken).ConfigureAwait(false);

        var unresolvedIdentifiers = await context.SqlRunner.QueryAsync(
            $@"
SELECT TOP 25
    COALESCE(NULLIF(LTRIM(RTRIM(s.Email)), ''), NULLIF(LTRIM(RTRIM(s.UserName)), ''), CONCAT('user#', CAST(s.Id AS nvarchar(32)))) AS Identifier
FROM stg.[{context.UserTableName}] s
LEFT JOIN ctl.UserMap m
  ON m.SourceEnv = @SourceEnv
 AND m.SourceUserId = s.Id
WHERE m.SourceUserId IS NULL
ORDER BY s.Id;",
            parameters,
            static record => record.IsDBNull(0) ? string.Empty : record.GetString(0),
            context.CommandTimeout,
            cancellationToken).ConfigureAwait(false);

        var unresolvedCount = await context.SqlRunner.ExecuteScalarAsync<long?>(
            $@"
SELECT COUNT_BIG(1)
FROM stg.[{context.UserTableName}] s
LEFT JOIN ctl.UserMap m
  ON m.SourceEnv = @SourceEnv
 AND m.SourceUserId = s.Id
WHERE m.SourceUserId IS NULL;",
            parameters,
            context.CommandTimeout,
            cancellationToken).ConfigureAwait(false) ?? 0L;

        context.State.SetUserMapReport(new UserMapReport(
            coverage,
            unresolvedCount,
            unresolvedIdentifiers));

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["coverage.rules"] = coverage.Count.ToString(CultureInfo.InvariantCulture),
            ["unresolved"] = unresolvedCount.ToString(CultureInfo.InvariantCulture)
        };

        context.Telemetry.Info(Name, "Constructed user map using configured rules.", metadata);
    }

    private static string BuildInsertCommand(RemapUsersContext context, RemapUsersMatchRule rule)
    {
        var builder = new StringBuilder();
        builder.AppendLine("INSERT INTO ctl.UserMap (SourceEnv, SourceUserId, SourceEmail, SourceUserName, SourceEmpNo, TargetUserId, MatchReason)");
        builder.AppendLine("SELECT @SourceEnv, s.Id, s.Email, s.UserName, s.EmployeeNo, u.Id, @MatchReason");
        builder.AppendLine($"FROM stg.[{context.UserTableName}] s");
        builder.AppendLine($"JOIN [{context.UserTableSchema}].[{context.UserTableName}] u ON {BuildJoinPredicate(rule)}");
        builder.AppendLine("LEFT JOIN ctl.UserMap existing ON existing.SourceEnv = @SourceEnv AND existing.SourceUserId = s.Id");
        builder.AppendLine("WHERE existing.SourceUserId IS NULL;");

        return builder.ToString();
    }

    private static string BuildFallbackCommand(RemapUsersContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("INSERT INTO ctl.UserMap (SourceEnv, SourceUserId, SourceEmail, SourceUserName, SourceEmpNo, TargetUserId, MatchReason)");
        builder.AppendLine("SELECT @SourceEnv, s.Id, s.Email, s.UserName, s.EmployeeNo, @FallbackUserId, @MatchReason");
        builder.AppendLine($"FROM stg.[{context.UserTableName}] s");
        builder.AppendLine("LEFT JOIN ctl.UserMap existing ON existing.SourceEnv = @SourceEnv AND existing.SourceUserId = s.Id");
        builder.AppendLine("WHERE existing.SourceUserId IS NULL;");
        return builder.ToString();
    }

    private static string BuildJoinPredicate(RemapUsersMatchRule rule)
    {
        return rule switch
        {
            RemapUsersMatchRule.Email => "u.Email = s.Email",
            RemapUsersMatchRule.NormalizeEmail => "LOWER(LTRIM(RTRIM(u.Email))) = LOWER(LTRIM(RTRIM(s.Email)))",
            RemapUsersMatchRule.UserName => "u.UserName = s.UserName",
            RemapUsersMatchRule.EmployeeNumber => "u.EmployeeNo = s.EmployeeNo",
            RemapUsersMatchRule.Fallback => "1 = 0", // handled separately
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unsupported match rule."),
        };
    }

    private static string GetMatchReason(RemapUsersMatchRule rule)
    {
        return rule switch
        {
            RemapUsersMatchRule.Email => "email_exact",
            RemapUsersMatchRule.NormalizeEmail => "email_norm",
            RemapUsersMatchRule.UserName => "username",
            RemapUsersMatchRule.EmployeeNumber => "employee_no",
            RemapUsersMatchRule.Fallback => "fallback",
            _ => "unknown"
        };
    }
}
