namespace Projection.Tests

// The capability vector's contract (the data-sink chapter, S2;
// CHAPTER_SINK_OPEN.md): rowset 26 informs the SINK — witnessed snapshots
// persist it so ossys-schema drift reads as data (the journal's
// ShapeChanged transition) — and never the Catalog. `toBundle` ignoring
// the vector is what keeps the contract bump behavior-preserving for
// every existing consumer of the bundle path.

open Xunit
open Projection.Adapters.OssysSql

module MetadataSnapshotCapabilityTests =

    [<Fact>]
    let ``toBundle is capability-invariant: the vector informs the sink, never the bundle`` () =
        let snapshot = OssysSnapshotBuilders.fullyPopulated 7
        let withoutVector = { snapshot with Capabilities = [] }
        Assert.Equal<Projection.Adapters.Osm.OssysRowsetTypes.RowsetBundle>(
            MetadataSnapshotRunner.toBundle withoutVector,
            MetadataSnapshotRunner.toBundle snapshot)

    [<Fact>]
    let ``capability vector round-trips the builder unchanged (field-count guard at the record grain)`` () =
        // The survival-rule-7 shape: a reconstruction site that omits a
        // field silently inherits it. Copy-reconstruct with every field
        // named — the compiler forces this test to grow when the record
        // does, and the equality catches a dropped polarity.
        let row = OssysSnapshotBuilders.capabilityRow
        let reconstructed : MetadataSnapshotRunner.OssysCapabilityRow =
            { HasDataType           = row.HasDataType
              HasType               = row.HasType
              HasPrecision          = row.HasPrecision
              HasScale              = row.HasScale
              HasDecimals           = row.HasDecimals
              HasOriginalName       = row.HasOriginalName
              HasExternalColumnType = row.HasExternalColumnType
              HasPhysicalColumnName = row.HasPhysicalColumnName
              HasDatabaseName       = row.HasDatabaseName
              HasIsIdentifier       = row.HasIsIdentifier
              HasRefEntityId        = row.HasRefEntityId
              HasIsAutoNumber       = row.HasIsAutoNumber
              HasDefaultValue       = row.HasDefaultValue
              HasDeleteRule         = row.HasDeleteRule
              HasOriginalType       = row.HasOriginalType
              HasAttrSsKey          = row.HasAttrSsKey
              HasLength             = row.HasLength
              HasOrderNum           = row.HasOrderNum
              HasEntityDescription  = row.HasEntityDescription }
        Assert.Equal(row, reconstructed)
