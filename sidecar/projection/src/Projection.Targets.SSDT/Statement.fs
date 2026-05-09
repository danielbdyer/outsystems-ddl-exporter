namespace Projection.Targets.SSDT

open Projection.Core

/// Π_SSDT's typed statement-stream form. Per session-34 — Π's
/// canonical output is a deterministic `seq<Statement>`; realization
/// layers (`Render.toText`, `Deploy.executeStream`) consume the
/// stream and choose their emission form. Bulk-vs-per-row deploy is
/// realization-layer policy, invisible to Π. The algebra (A18 / T1)
/// holds at the stream level: the same Catalog produces the same
/// statement sequence byte-for-byte, regardless of downstream
/// realization choice.
///
/// AXIOM scaffold (filed for chapter-3 close):
///   A35 — Π's output is a deterministic statement stream.
///   A36 — Bulk-vs-incremental is realization-layer policy.

type TableId = { Schema : string; Table : string }

/// IR-typed column declaration. The realization layer (`Render`)
/// converts `(Type, Length, Precision, Scale)` to its SQL type
/// expression, so emit-time and deploy-time agree by construction.
type ColumnDef =
    {
        Name : string
        Type : PrimitiveType
        Length : int option
        Precision : int option
        Scale : int option
        Nullable : bool
        IsIdentity : bool
        IsPrimaryKey : bool
        /// The originating attribute's display name + SsKey root,
        /// preserved so `Render.toText` can keep the diffable-form
        /// trailing comment that the v1 emitter carried.
        Provenance : string
    }

type ReferenceActionSql = NoActionSql | CascadeSql | SetNullSql

type ForeignKeyDef =
    {
        Name : string
        SourceColumn : string
        Target : TableId
        TargetColumn : string
        OnDelete : ReferenceActionSql
    }

type PrimaryKeyDef =
    {
        Name : string
        Columns : string list
    }

/// One column's value within an `InsertRow`. `Raw` is the V2 IR
/// contract: invariant-culture string, `""` denotes NULL. The
/// realization layer formats per `Type`.
type CellValue =
    {
        Column : string
        Type : PrimitiveType
        Raw : string
    }

type Statement =
    | Blank
    | Comment of text: string
    | CreateTable of TableId * ColumnDef list * PrimaryKeyDef option * ForeignKeyDef list
    | InsertRow of TableId * CellValue list
    | SetIdentityInsert of TableId * enabled: bool
