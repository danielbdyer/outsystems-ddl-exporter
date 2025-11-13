using System;

namespace Osm.Pipeline.UatUsers;

public sealed record UserMatchingResult(
    UserIdentifier SourceUserId,
    UserIdentifier? TargetUserId,
    string Strategy,
    string Explanation,
    bool UsedFallback)
{
    public static UserMatchingResult Create(
        UserIdentifier sourceUserId,
        UserIdentifier? targetUserId,
        string strategy,
        string explanation,
        bool usedFallback = false)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            throw new ArgumentException("Strategy must be provided.", nameof(strategy));
        }

        explanation ??= string.Empty;
        return new UserMatchingResult(sourceUserId, targetUserId, strategy.Trim(), explanation, usedFallback);
    }
}
