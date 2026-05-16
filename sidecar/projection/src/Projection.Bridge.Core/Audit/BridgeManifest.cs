using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Projection.Bridge.Audit;

/// <summary>
/// One entry in the reflection-scanned Bridge manifest. Carries the audit
/// metadata of a single public Bridge method.
/// </summary>
public sealed record BridgeManifestEntry(
    string DeclaringType,
    string MethodName,
    string Chapter,
    string AddedDate,
    string V1Source,
    SunsetDisposition Current,
    SunsetDisposition Target,
    Determinism Determinism,
    Frequency Frequency)
{
    /// <summary>
    /// True if this method has reached its target state on the inheritance
    /// gradient. The cutover+30 sunset gate is the conjunction of this
    /// predicate over every manifest entry.
    /// </summary>
    public bool HasReachedTarget => Current == Target;
}

/// <summary>
/// Reflection-scanned audit witness over every public method in
/// Projection.Bridge.Core and Projection.Bridge.Runtime decorated with
/// <see cref="BridgeMethodAttribute"/>. Built at test-build time and asserted
/// by <c>Projection.Bridge.Tests.BridgeManifestTests</c>.
///
/// <para>
/// The manifest is the auditable witness that the Bridge wall is being
/// operated as a deliberate inheritance surface, not an ad-hoc translation
/// layer. Three structural claims hold by construction:
/// </para>
///
/// <list type="number">
/// <item>Every public Bridge method has the attribute (the analyzer enforces
/// this at compile time; the manifest test confirms at run time).</item>
/// <item>Every entry's <see cref="BridgeMethodAttribute.V1Source"/>,
/// <see cref="BridgeMethodAttribute.Chapter"/>, and
/// <see cref="BridgeMethodAttribute.AddedDate"/> are non-empty (the audit
/// trail is complete).</item>
/// <item>At cutover+30, the conjunction of <c>HasReachedTarget</c> over every
/// entry asserts the workshop is empty.</item>
/// </list>
/// </summary>
public static class BridgeManifest
{
    /// <summary>
    /// Scan the supplied assemblies for every public method decorated with
    /// <see cref="BridgeMethodAttribute"/>, producing the manifest as an
    /// ordered immutable list (sorted by declaring type, then method name,
    /// for determinism).
    /// </summary>
    public static ImmutableArray<BridgeManifestEntry> Scan(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var entries = new List<BridgeManifestEntry>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var attr = method.GetCustomAttribute<BridgeMethodAttribute>(inherit: false);
                    if (attr is null)
                    {
                        continue;
                    }

                    entries.Add(new BridgeManifestEntry(
                        DeclaringType: type.FullName ?? type.Name,
                        MethodName: method.Name,
                        Chapter: attr.Chapter,
                        AddedDate: attr.AddedDate,
                        V1Source: attr.V1Source,
                        Current: attr.Current,
                        Target: attr.Target,
                        Determinism: attr.Determinism,
                        Frequency: attr.Frequency));
                }
            }
        }

        return entries
            .OrderBy(e => e.DeclaringType, StringComparer.Ordinal)
            .ThenBy(e => e.MethodName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    /// Validate the manifest for well-formedness. Returns an immutable list of
    /// human-readable error messages naming the offending entry; an empty list
    /// means the manifest is structurally sound.
    /// </summary>
    public static ImmutableArray<string> Validate(ImmutableArray<BridgeManifestEntry> entries)
    {
        var errors = new List<string>();
        foreach (var entry in entries)
        {
            var prefix = $"{entry.DeclaringType}.{entry.MethodName}";
            if (string.IsNullOrWhiteSpace(entry.Chapter))
            {
                errors.Add($"{prefix}: [BridgeMethod].Chapter is empty.");
            }

            if (string.IsNullOrWhiteSpace(entry.AddedDate))
            {
                errors.Add($"{prefix}: [BridgeMethod].AddedDate is empty.");
            }
            else if (!DateOnly.TryParse(entry.AddedDate, out _))
            {
                errors.Add($"{prefix}: [BridgeMethod].AddedDate is not parseable as YYYY-MM-DD ('{entry.AddedDate}').");
            }

            if (string.IsNullOrWhiteSpace(entry.V1Source))
            {
                errors.Add($"{prefix}: [BridgeMethod].V1Source is empty (use 'OriginAuthoredInV2' for V2-for-V1 capabilities).");
            }

            // Current may not be ahead of Target — the gradient runs Delegated -> Vendored -> RefinedInPlace -> TranslatedToFSharp.
            if ((int)entry.Current > (int)entry.Target)
            {
                errors.Add($"{prefix}: Current state ({entry.Current}) is ahead of Target ({entry.Target}) on the inheritance gradient.");
            }
        }

        return errors.ToImmutableArray();
    }
}
