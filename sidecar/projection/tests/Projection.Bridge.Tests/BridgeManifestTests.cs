using System.Linq;
using System.Reflection;
using Projection.Bridge.Audit;
using Xunit;

namespace Projection.Bridge.Tests;

/// <summary>
/// Structural witness that the Bridge manifest is well-formed. These tests
/// are the auditable proof that the V1↔V2 inheritance surface is being
/// operated as a deliberate gradient, not an ad-hoc translation layer.
///
/// <para>
/// At cutover+30, the <c>BridgeManifestSunsetGateTest</c> at the bottom of
/// this file becomes the chapter-close gate: it asserts that every public
/// Bridge method has reached its declared target state on the inheritance
/// gradient. Failure means the workshop is not empty; passage means V1 is
/// done contributing and V2 stands self-contained.
/// </para>
/// </summary>
public sealed class BridgeManifestTests
{
    private static readonly Assembly[] BridgeAssemblies = new[]
    {
        typeof(BridgeMethodAttribute).Assembly,            // Projection.Bridge.Core
        // Projection.Bridge.Runtime is loaded by reference; the public assembly
        // type is added below once the Runtime carries its first [BridgeMethod].
    };

    [Fact]
    public void Manifest_scan_produces_deterministic_ordering()
    {
        var first = BridgeManifest.Scan(BridgeAssemblies);
        var second = BridgeManifest.Scan(BridgeAssemblies);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Manifest_is_well_formed()
    {
        var entries = BridgeManifest.Scan(BridgeAssemblies);
        var errors = BridgeManifest.Validate(entries);
        Assert.Empty(errors);
    }

    /// <summary>
    /// Cutover+30 sunset gate. Asserts every public Bridge method has reached
    /// its declared target state. Until cutover+30, methods may legitimately
    /// have <see cref="BridgeManifestEntry.HasReachedTarget"/> = false (work
    /// scheduled against them). This test ships <c>[Fact(Skip = ...)]</c>
    /// during dual-track and flips to <c>[Fact]</c> at cutover+30 chapter open.
    /// </summary>
    [Fact(Skip = "Active at cutover+30 chapter close — see Chapter 6 (Sunset operationalization).")]
    public void Cutover_plus_30_sunset_gate_every_bridge_method_has_reached_target()
    {
        var entries = BridgeManifest.Scan(BridgeAssemblies);
        var notYet = entries.Where(e => !e.HasReachedTarget).ToArray();
        Assert.True(notYet.Length == 0,
            $"At cutover+30, {notYet.Length} Bridge methods have not reached their target state: " +
            string.Join(", ", notYet.Select(e => $"{e.DeclaringType}.{e.MethodName} ({e.Current} → {e.Target})")));
    }
}
