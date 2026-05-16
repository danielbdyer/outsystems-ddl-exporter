using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Projection.Bridge.Wire;

/// <summary>
/// BCL-typed error record that crosses the Bridge wall. Mirrors the shape of
/// V2's <c>Projection.Core.ValidationError</c> so the F# adapter consuming a
/// <see cref="BridgeResult{T}"/> can lift to <c>Result&lt;'a, ValidationError list&gt;</c>
/// without translation loss.
/// </summary>
/// <param name="Code">
/// Dot-separated error code following V2's <c>category.subject.problem</c>
/// convention (e.g., <c>"bridge.catalog.extractMetadata.unhandled"</c>).
/// </param>
/// <param name="Message">Human-readable description of the failure.</param>
/// <param name="Metadata">
/// Optional supporting metadata (V1 exception type, parameter values, etc.).
/// Use <see cref="ImmutableDictionary{TKey, TValue}.Empty"/> when no metadata
/// is needed.
/// </param>
public sealed record BridgeError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public static BridgeError FromException(string code, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var metadata = ImmutableDictionary<string, string?>.Empty
            .Add("exception.type", exception.GetType().FullName)
            .Add("exception.message", exception.Message);
        return new BridgeError(code, exception.Message, metadata);
    }

    public static BridgeError Of(string code, string message) =>
        new(code, message, ImmutableDictionary<string, string?>.Empty);
}
