using System.Collections.Immutable;

namespace Osm.Pipeline.UatUsers;

public sealed record UatUsersApplicationResult(
    bool Executed,
    UatUsersContext? Context,
    ImmutableArray<string> Warnings)
{
    public static UatUsersApplicationResult Disabled { get; } = new(
        Executed: false,
        Context: null,
        Warnings: ImmutableArray<string>.Empty);
}
