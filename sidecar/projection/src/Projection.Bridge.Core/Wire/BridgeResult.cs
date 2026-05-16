using System.Collections.Generic;
using System.Collections.Immutable;

namespace Projection.Bridge.Wire;

/// <summary>
/// BCL-typed result envelope that crosses the Bridge wall. Mirrors V2's
/// <c>Projection.Core.Result&lt;'a&gt;</c> shape so the F# adapter consuming a
/// Bridge call can pattern-match on success/failure without try/catch at
/// every site. Bridge methods never throw across the wall; the wall converts
/// every V1 exception into a <see cref="BridgeError"/> entry on this result.
/// </summary>
public sealed record BridgeResult<T>(
    bool IsSuccess,
    T? Value,
    IReadOnlyList<BridgeError> Errors)
{
    public static BridgeResult<T> Success(T value) =>
        new(IsSuccess: true, Value: value, Errors: ImmutableArray<BridgeError>.Empty);

    public static BridgeResult<T> Failure(IReadOnlyList<BridgeError> errors) =>
        new(IsSuccess: false, Value: default, Errors: errors);

    public static BridgeResult<T> Failure(BridgeError error) =>
        Failure(ImmutableArray.Create(error));
}
