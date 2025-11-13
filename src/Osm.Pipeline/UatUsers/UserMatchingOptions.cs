using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Pipeline.UatUsers;

public enum UserMatchingStrategy
{
    CaseInsensitiveEmail = 0,
    ExactAttribute = 1,
    Regex = 2
}

public enum UserFallbackAssignmentMode
{
    Ignore = 0,
    SingleTarget = 1,
    RoundRobin = 2
}

public static class UserMatchingConfigurationHelper
{
    public static bool TryParseStrategy(string? value, out UserMatchingStrategy strategy)
    {
        strategy = UserMatchingStrategy.CaseInsensitiveEmail;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Equals("case-insensitive-email", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("caseinsensitiveemail", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("email", StringComparison.OrdinalIgnoreCase))
        {
            strategy = UserMatchingStrategy.CaseInsensitiveEmail;
            return true;
        }

        if (normalized.Equals("exact", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("exact-attribute", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("exactattribute", StringComparison.OrdinalIgnoreCase))
        {
            strategy = UserMatchingStrategy.ExactAttribute;
            return true;
        }

        if (normalized.Equals("regex", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("regular-expression", StringComparison.OrdinalIgnoreCase))
        {
            strategy = UserMatchingStrategy.Regex;
            return true;
        }

        if (Enum.TryParse<UserMatchingStrategy>(normalized, ignoreCase: true, out var parsedStrategy))
        {
            strategy = parsedStrategy;
            return true;
        }

        return false;
    }

    public static UserMatchingStrategy ParseStrategy(string? value, UserMatchingStrategy defaultValue)
    {
        if (TryParseStrategy(value, out var strategy))
        {
            return strategy;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        throw new ArgumentException($"Unknown matching strategy '{value}'.", nameof(value));
    }

    public static bool TryParseFallbackMode(string? value, out UserFallbackAssignmentMode mode)
    {
        mode = UserFallbackAssignmentMode.Ignore;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Equals("ignore", StringComparison.OrdinalIgnoreCase))
        {
            mode = UserFallbackAssignmentMode.Ignore;
            return true;
        }

        if (normalized.Equals("single", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("single-target", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("singleprimarykey", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("singleprimary", StringComparison.OrdinalIgnoreCase))
        {
            mode = UserFallbackAssignmentMode.SingleTarget;
            return true;
        }

        if (normalized.Equals("round-robin", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("roundrobin", StringComparison.OrdinalIgnoreCase))
        {
            mode = UserFallbackAssignmentMode.RoundRobin;
            return true;
        }

        if (Enum.TryParse<UserFallbackAssignmentMode>(normalized, ignoreCase: true, out var parsedMode))
        {
            mode = parsedMode;
            return true;
        }

        return false;
    }

    public static UserFallbackAssignmentMode ParseFallbackMode(string? value, UserFallbackAssignmentMode defaultValue)
    {
        if (TryParseFallbackMode(value, out var mode))
        {
            return mode;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        throw new ArgumentException($"Unknown fallback mode '{value}'.", nameof(value));
    }

    public static string ResolveAttribute(UserMatchingStrategy strategy, string? attribute)
    {
        var trimmed = string.IsNullOrWhiteSpace(attribute) ? null : attribute.Trim();
        return strategy switch
        {
            UserMatchingStrategy.CaseInsensitiveEmail => "Email",
            UserMatchingStrategy.Regex => trimmed ?? "Username",
            UserMatchingStrategy.ExactAttribute => trimmed ?? "External_Id",
            _ => trimmed ?? "Email"
        };
    }

    public static ImmutableArray<UserIdentifier> NormalizeFallbackTargets(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return ImmutableArray<UserIdentifier>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<UserIdentifier>();
        var seen = new HashSet<UserIdentifier>();
        foreach (var candidate in values)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (!UserIdentifier.TryParse(candidate.Trim(), out var identifier))
            {
                throw new FormatException($"Invalid fallback target '{candidate}'.");
            }

            if (seen.Add(identifier))
            {
                builder.Add(identifier);
            }
        }

        return builder.ToImmutable();
    }
}
