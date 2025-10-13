using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Osm.Pipeline.RemapUsers;

/// <summary>
/// Defines the supported matching rules for mapping source snapshot users to
/// their corresponding UAT <c>ossys_User</c> identities.
/// </summary>
public enum RemapUsersMatchRule
{
    Email,
    NormalizeEmail,
    UserName,
    EmployeeNumber,
    Fallback
}

public static class RemapUsersMatchRuleExtensions
{
    private static readonly IReadOnlyDictionary<string, RemapUsersMatchRule> Lookup =
        new Dictionary<string, RemapUsersMatchRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = RemapUsersMatchRule.Email,
            ["email_exact"] = RemapUsersMatchRule.Email,
            ["normalize-email"] = RemapUsersMatchRule.NormalizeEmail,
            ["normalize_email"] = RemapUsersMatchRule.NormalizeEmail,
            ["username"] = RemapUsersMatchRule.UserName,
            ["user-name"] = RemapUsersMatchRule.UserName,
            ["empno"] = RemapUsersMatchRule.EmployeeNumber,
            ["employee-no"] = RemapUsersMatchRule.EmployeeNumber,
            ["employee-number"] = RemapUsersMatchRule.EmployeeNumber,
            ["fallback"] = RemapUsersMatchRule.Fallback,
        };

    public static RemapUsersMatchRule Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Match rule value must be provided.", nameof(value));
        }

        if (!Lookup.TryGetValue(value.Trim(), out var rule))
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "Unsupported remap-users match rule '{0}'.", value),
                nameof(value));
        }

        return rule;
    }

    public static IReadOnlyList<RemapUsersMatchRule> ParseMany(IEnumerable<string> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        return values.Select(Parse).ToArray();
    }
}
