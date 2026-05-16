using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Projection.Bridge.Audit;
using Projection.Bridge.Wire;

namespace Projection.Bridge.Capabilities.Catalog;

/// <summary>
/// Lifts V1's metadata-extraction verb into V2's vocabulary. Inherits the
/// SQL-extraction + result-set-processor + JSON-snapshot chain from V1's
/// <c>Osm.Pipeline</c> as the first worked inheritance of the Bridge wave.
///
/// <para>
/// Chapter 0.5 slice γ scaffold. The method body is intentionally elided
/// pending slice γ (delegation to V1) and slice ε (vendoring V1 source into
/// <c>Projection.Bridge.Core/Adopted/Catalog/</c>). The signature, the
/// audit attribute, and the wire records below are stable across the
/// gradient transition; only the method body's V1-source dependency moves
/// from <c>Osm.Pipeline.Application.ExtractModelApplicationService</c>
/// (Delegated) to a locally-vendored copy under <c>Adopted/Catalog/</c>
/// (Vendored) to a refined C# implementation (RefinedInPlace).
/// </para>
///
/// <para>
/// The output surface lifts V1's <c>OutsystemsMetadataSnapshot</c> rowset
/// shape, NOT V1's <c>OsmModel</c> aggregate root. F# adapters reconstruct
/// the V2 <c>Catalog</c> directly from rowset Wire records via a renamed
/// <c>RowsetBundle</c> equivalent; the aggregate root is V1's mental-model
/// reconstruction that V2 does not inherit.
/// </para>
///
/// <para>
/// See <c>CHAPTER_0_5_OPEN.md</c> slices γ–ε for the bring-up schedule;
/// <c>DECISIONS 2026-05-16 — Bridge wave: V2 inherits from V1</c> for the
/// codifying decision; <c>ADMIRE.md</c> "OSSYS catalog producer" entry for
/// the inheritance gradient record.
/// </para>
/// </summary>
public static class ExtractMetadata
{
    [BridgeMethod(
        Chapter = "0.5",
        AddedDate = "2026-05-16",
        V1Source = "Osm.Pipeline.Application.ExtractModelApplicationService.RunAsync",
        Current = SunsetDisposition.Delegated,
        Target = SunsetDisposition.RefinedInPlace,
        Determinism = Determinism.Deterministic,
        Frequency = Frequency.OneShot)]
    public static Task<BridgeResult<ExtractMetadataOutput>> ExtractMetadataAsync(
        ExtractMetadataInput input,
        CancellationToken cancellationToken = default)
    {
        // Slice γ (in flight): delegate to V1's ExtractModelApplicationService.
        // Slice ε (scheduled): switch to vendored copy under Adopted/Catalog/.
        // The signature, audit attribute, and Wire records above are stable
        // across both transitions — only this method body moves.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BridgeResult<ExtractMetadataOutput>.Failure(
            BridgeError.Of(
                code: "bridge.catalog.extractMetadata.notImplemented",
                message: "ExtractMetadata is scaffold only at chapter 0.5 slice α; slice γ implementation pending. " +
                         "See CHAPTER_0_5_OPEN.md for the bring-up schedule.")));
    }
}

/// <summary>
/// Bridge's own minimal input record for the metadata-extraction verb.
/// Replaces V1's <c>CliConfigurationContext</c> + <c>ExtractModelOverrides</c>
/// + <c>SqlOptionsOverrides</c> triple — Bridge re-implements V1's
/// configuration-flattening rather than dragging V1's CLI configuration
/// machinery across the wall. This is "lift verbs, not nouns" exactly: the
/// verb is <c>extractMetadata</c>, not <c>runWithConfigurationContext</c>.
/// </summary>
public sealed record ExtractMetadataInput(
    string ConnectionString,
    IReadOnlyList<string>? ModuleFilter,
    bool IncludeSystem,
    bool IncludeInactiveModules,
    bool OnlyActiveAttributes);

/// <summary>
/// Bridge's V2-vocabulary projection of V1's <c>OutsystemsMetadataSnapshot</c>.
/// Flat <c>IReadOnlyList&lt;Row&gt;</c> records the F# adapter reshapes into
/// the existing <c>RowsetBundle</c> via a ~30-line rename function. The
/// reshape preserves V2's existing <c>parseRowsetBundle</c> consumer
/// unchanged — harmonization-via-parameterization (A40) at the adapter.
///
/// <para>
/// Field-by-field expansion lands at slice γ when the V1 result-set
/// processor chain is consulted for the exact column set; the placeholder
/// row records below are skeletons whose final shape lands in slice γ.
/// </para>
/// </summary>
public sealed record ExtractMetadataOutput(
    IReadOnlyList<ExtractedModuleRow> Modules,
    IReadOnlyList<ExtractedKindRow> Kinds,
    IReadOnlyList<ExtractedAttributeRow> Attributes,
    IReadOnlyList<ExtractedReferenceRow> References,
    IReadOnlyList<ExtractedPhysicalTableRow> PhysicalTables,
    IReadOnlyList<ExtractedStaticPopulationRow> StaticPopulations,
    IReadOnlyList<ExtractedAttributeForeignKeyRow> AttributeForeignKeys);

// Slice-γ placeholder row records. Final field sets land when slice γ
// consults V1's IOutsystemsMetadataReader for the exact column shape.
// V2 vocabulary: Module (not Espace), Kind (not Entity), Attribute (not
// Attr). The wall analyzer (slice β) rejects any row record adopting V1
// vocabulary.

public sealed record ExtractedModuleRow(
    System.Guid? ModuleSsKey,
    int ModuleId,
    string ModuleName,
    bool IsSystem,
    bool IsActive,
    string? ModuleKind);

public sealed record ExtractedKindRow(
    System.Guid? KindSsKey,
    System.Guid? PrimaryKeySsKey,
    int KindId,
    int ModuleId,
    string KindName,
    string PhysicalSchema,
    string PhysicalTable,
    string? CatalogName,
    bool IsSystem,
    bool IsExternal,
    string? DataKind,
    bool IsActive,
    string? Description);

public sealed record ExtractedAttributeRow(
    System.Guid? AttributeSsKey,
    int AttributeId,
    int KindId,
    string AttributeName,
    string PhysicalColumn,
    string DataType,
    bool IsMandatory,
    bool IsIdentifier,
    bool IsAutoNumber,
    int? Length,
    int? Precision,
    int? Scale,
    bool IsActive,
    string? Description);

public sealed record ExtractedReferenceRow(
    int AttributeId,
    string TargetKindName,
    string? DeleteRuleCode,
    bool HasDatabaseConstraint);

public sealed record ExtractedPhysicalTableRow(
    int KindId,
    string Schema,
    string Table);

public sealed record ExtractedStaticPopulationRow(
    int KindId,
    System.Guid? RowSsKey,
    IReadOnlyDictionary<string, string?> Values);

public sealed record ExtractedAttributeForeignKeyRow(
    int AttributeId,
    bool HasFk);
