using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Osm.Pipeline.RemapUsers;

public static class RemapUsersRunHasher
{
    public static string ComputeHash(RemapUsersRunParameters parameters)
    {
        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        var builder = new StringBuilder();
        builder.AppendLine(parameters.SourceEnvironment);
        builder.AppendLine(parameters.SnapshotPath);
        builder.AppendLine(string.Join(',', parameters.MatchingRules));
        builder.AppendLine(parameters.Policy.ToString().ToLowerInvariant());
        builder.AppendLine(parameters.IncludePii ? "1" : "0");
        builder.AppendLine(parameters.RebuildMap ? "1" : "0");
        builder.AppendLine(parameters.UserTable.ToLowerInvariant());
        builder.AppendLine(parameters.BatchSize.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(parameters.CommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(parameters.Parallelism.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(parameters.FallbackUserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        var canonical = builder.ToString();
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
