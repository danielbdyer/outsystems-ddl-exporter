using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers;

public sealed class UserMatchingEngine
{
    private readonly ILogger<UserMatchingEngine> _logger;

    public UserMatchingEngine(ILogger<UserMatchingEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<UserMatchingEngine>.Instance;
    }

    public IReadOnlyList<UserMatchingResult> Execute(UatUsersContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.OrphanUserIds.Count == 0)
        {
            _logger.LogInformation("No orphan user IDs discovered; skipping matching engine.");
            return Array.Empty<UserMatchingResult>();
        }

        var results = new List<UserMatchingResult>(context.OrphanUserIds.Count);
        var lookup = BuildLookup(context);
        var regex = CreateRegex(context);
        var fallbackIndex = 0;

        foreach (var orphan in context.OrphanUserIds)
        {
            if (!context.QaUserInventory.TryGetValue(orphan, out var qaRecord))
            {
                results.Add(UserMatchingResult.Create(
                    orphan,
                    null,
                    context.MatchingStrategy.ToString(),
                    "Source user was not present in the QA inventory."));
                continue;
            }

            if (TryMatch(context, qaRecord, lookup, regex, out var matched, out var explanation))
            {
                var message = explanation ?? "Automatic match produced.";
                results.Add(UserMatchingResult.Create(orphan, matched, context.MatchingStrategy.ToString(), message));
                continue;
            }

            if (TryFallback(context, ref fallbackIndex, out matched, out explanation))
            {
                var message = explanation ?? "Assigned fallback target.";
                results.Add(UserMatchingResult.Create(orphan, matched, context.FallbackAssignment.ToString(), message, true));
                continue;
            }

            results.Add(UserMatchingResult.Create(
                orphan,
                null,
                context.MatchingStrategy.ToString(),
                explanation ?? "No automatic match was produced."));
        }

        var resolvedCount = results.Count(result => result.TargetUserId.HasValue && !result.UsedFallback);
        var fallbackCount = results.Count(result => result.UsedFallback);
        _logger.LogInformation(
            "Matching engine produced {ResolvedCount} matches ({FallbackCount} fallback assignments) for {TotalCount} orphans.",
            resolvedCount,
            fallbackCount,
            context.OrphanUserIds.Count);

        return results;
    }

    private static Dictionary<string, List<UserIdentifier>> BuildLookup(UatUsersContext context)
    {
        var attribute = context.MatchingAttribute ?? "Email";
        var comparer = context.MatchingStrategy == UserMatchingStrategy.CaseInsensitiveEmail
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var lookup = new Dictionary<string, List<UserIdentifier>>(comparer);
        foreach (var pair in context.UatUserInventory)
        {
            var value = pair.Value.GetAttributeValue(attribute);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim();
            if (!lookup.TryGetValue(normalized, out var list))
            {
                list = new List<UserIdentifier>();
                lookup[normalized] = list;
            }

            list.Add(pair.Key);
        }

        return lookup;
    }

    private static Regex? CreateRegex(UatUsersContext context)
    {
        if (context.MatchingStrategy != UserMatchingStrategy.Regex)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(context.MatchingRegexPattern))
        {
            return null;
        }

        return new Regex(
            context.MatchingRegexPattern!,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));
    }

    private static bool TryMatch(
        UatUsersContext context,
        UserInventoryRecord qaRecord,
        IReadOnlyDictionary<string, List<UserIdentifier>> lookup,
        Regex? regex,
        out UserIdentifier? matched,
        out string? explanation)
    {
        matched = null;
        explanation = null;

        switch (context.MatchingStrategy)
        {
            case UserMatchingStrategy.CaseInsensitiveEmail:
                return TryExactMatch(context, qaRecord, lookup, attributeLabel: "email", out matched, out explanation);
            case UserMatchingStrategy.ExactAttribute:
                return TryExactMatch(context, qaRecord, lookup, context.MatchingAttribute, out matched, out explanation);
            case UserMatchingStrategy.Regex:
                return TryRegexMatch(context, qaRecord, lookup, regex, out matched, out explanation);
            default:
                explanation = "No matching strategy was selected.";
                return false;
        }
    }

    private static bool TryExactMatch(
        UatUsersContext context,
        UserInventoryRecord qaRecord,
        IReadOnlyDictionary<string, List<UserIdentifier>> lookup,
        string? attributeLabel,
        out UserIdentifier? matched,
        out string? explanation)
    {
        matched = null;
        explanation = null;

        if (string.IsNullOrWhiteSpace(attributeLabel))
        {
            explanation = "No attribute was configured for exact matching.";
            return false;
        }

        var attributeValue = qaRecord.GetAttributeValue(attributeLabel);
        if (string.IsNullOrWhiteSpace(attributeValue))
        {
            explanation = $"QA inventory row does not include '{attributeLabel}'.";
            return false;
        }

        var normalized = attributeValue.Trim();
        if (!lookup.TryGetValue(normalized, out var candidates) || candidates.Count == 0)
        {
            explanation = $"No UAT user has '{attributeLabel}' equal to '{normalized}'.";
            return false;
        }

        if (candidates.Count > 1)
        {
            explanation = $"Multiple UAT users share '{attributeLabel}' value '{normalized}'.";
            return false;
        }

        matched = candidates[0];
        explanation = $"Matched {attributeLabel} '{normalized}'.";
        return true;
    }

    private static bool TryRegexMatch(
        UatUsersContext context,
        UserInventoryRecord qaRecord,
        IReadOnlyDictionary<string, List<UserIdentifier>> lookup,
        Regex? regex,
        out UserIdentifier? matched,
        out string? explanation)
    {
        matched = null;
        explanation = null;

        if (regex is null)
        {
            explanation = "Regex pattern was not provided.";
            return false;
        }

        var attribute = context.MatchingAttribute ?? "Username";
        var value = qaRecord.GetAttributeValue(attribute);
        if (string.IsNullOrWhiteSpace(value))
        {
            explanation = $"QA inventory row does not include '{attribute}'.";
            return false;
        }

        var match = regex.Match(value);
        if (!match.Success)
        {
            explanation = $"Regex pattern did not match '{value}'.";
            return false;
        }

        var extracted = ExtractRegexValue(match);
        if (string.IsNullOrWhiteSpace(extracted))
        {
            explanation = "Regex did not capture a usable value.";
            return false;
        }

        var key = extracted.Trim();
        if (!lookup.TryGetValue(key, out var candidates) || candidates.Count == 0)
        {
            explanation = $"No UAT user matched regex capture '{key}'.";
            return false;
        }

        if (candidates.Count > 1)
        {
            explanation = $"Multiple UAT users share regex capture '{key}'.";
            return false;
        }

        matched = candidates[0];
        explanation = $"Regex captured '{key}'.";
        return true;
    }

    private static string? ExtractRegexValue(Match match)
    {
        if (match.Groups["target"] is { Success: true } target)
        {
            return target.Value;
        }

        if (match.Groups.Count > 1)
        {
            foreach (Group group in match.Groups)
            {
                if (group.Name == "0" || !group.Success)
                {
                    continue;
                }

                return group.Value;
            }
        }

        return match.Value;
    }

    private static bool TryFallback(
        UatUsersContext context,
        ref int fallbackIndex,
        out UserIdentifier? matched,
        out string? explanation)
    {
        matched = null;
        explanation = null;

        if (context.FallbackAssignment == UserFallbackAssignmentMode.Ignore)
        {
            explanation = "Fallback mode is set to ignore.";
            return false;
        }

        if (context.FallbackTargets.Length == 0)
        {
            explanation = "Fallback mode requires at least one target user ID.";
            return false;
        }

        var next = context.FallbackAssignment switch
        {
            UserFallbackAssignmentMode.SingleTarget => context.FallbackTargets[0],
            UserFallbackAssignmentMode.RoundRobin => context.FallbackTargets[fallbackIndex++ % context.FallbackTargets.Length],
            _ => context.FallbackTargets[0]
        };

        if (!context.IsAllowedUser(next))
        {
            explanation = $"Fallback target '{next}' is not part of the allowed UAT roster.";
            return false;
        }

        matched = next;
        explanation = context.FallbackAssignment == UserFallbackAssignmentMode.RoundRobin
            ? $"Assigned round-robin fallback '{next}'."
            : $"Assigned single fallback '{next}'.";
        return true;
    }
}
