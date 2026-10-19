module Projection.Tests.OssysDomainAttributeParityTests

// V1 parity audit — slice 5.2.α.attribute. Reserves matrix rows 48–53
// (V1's three-layer attribute aggregate vs V2's consolidated Attribute
// record + Kind-scoped ColumnChecks).

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

[<Fact(Skip = "Matrix row 48 — 🟡 DIVERGENCE. V1's attribute aggregate spans 3 layers across 7 files: (1) **Logical** (`AttributeModel` + `AttributeMetadata`; LogicalName, ColumnName, DataType, defaults, IsMandatory, IsIdentifier, IsAutoNumber, Description, ExtendedProperties); (2) **Physical reality** (`AttributeReality`; 5 reflection fields: IsNullableInDatabase, HasNulls, HasDuplicates, HasOrphans, IsPresentButInactive); (3) **On-disk evidence** (`AttributeOnDiskMetadata` + `AttributeOnDiskCheckConstraint` + `AttributeOnDiskDefaultConstraint`; SqlType, MaxLength, Precision, Collation, IsIdentity, IsComputed, DefaultDefinition, CHECK constraint arrays). V2 consolidates into a single `Attribute` record (~21 fields) + `Kind.ColumnChecks` collection (table-scoped). V2's consolidation flows from pillar 9 — the three V1 layers conflate DataIntent (logical + on-disk schema definition) with OperatorIntent (reality reflection is observational, separate concern). See `DECISIONS 2026-05-18 (slice 5.2.α.attribute) — V1 three-layer attribute model consolidates into V2 typed Attribute + table-scoped checks`. Re-open trigger: V2 grows a Profile-layer that carries runtime reflection statistics (parallels matrix row 30 telemetry).")>]
let ``5.2.α row 48: V1 three-layer attribute aggregate vs V2 consolidated Attribute + Kind.ColumnChecks`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 48 + DECISIONS 2026-05-18 (slice 5.2.α.attribute)"

[<Fact>]
let ``5.2.α row 49: V1 AttributeReality reflection fields lift to V2 Profile.AttributeReality (matrix row 49 cashed out)`` () : unit =
    // The axis fired: `Profile.AttributeReality` (Profile.fs:783-812)
    // carries exactly the 5 V1 reflection fields this row named as
    // missing (`IsNullableInDatabase`, `HasNulls`, `HasDuplicates`,
    // `HasOrphans`, `IsPresentButInactive`), keyed by `AttributeKey :
    // SsKey` (A4 identity), living on `Profile` — independent of
    // `Catalog` / `Policy` per A34. Assert the zero-evidence default
    // (`AttributeReality.create`) carries all-false, then that every
    // field is independently settable and merges into `Profile
    // .AttributeRealities`.
    let key = customerIdAttrKey
    let defaultReality = AttributeReality.create key
    Assert.Equal(key, defaultReality.AttributeKey)
    Assert.False(defaultReality.IsNullableInDatabase)
    Assert.False(defaultReality.HasNulls)
    Assert.False(defaultReality.HasDuplicates)
    Assert.False(defaultReality.HasOrphans)
    Assert.False(defaultReality.IsPresentButInactive)

    let fullyObserved =
        { defaultReality with
            IsNullableInDatabase = true
            HasNulls             = true
            HasDuplicates        = true
            HasOrphans           = true
            IsPresentButInactive = true }
    let profile = { Profile.empty with AttributeRealities = [ fullyObserved ] }
    let recovered = profile.AttributeRealities |> List.exactlyOne
    Assert.Equal(key, recovered.AttributeKey)
    Assert.True(recovered.IsNullableInDatabase)
    Assert.True(recovered.HasNulls)
    Assert.True(recovered.HasDuplicates)
    Assert.True(recovered.HasOrphans)
    Assert.True(recovered.IsPresentButInactive)

[<Fact(Skip = "Matrix row 50 — 🔵 V2-EXTENSION. V1's `AttributeOnDiskCheckConstraint` is an attribute-nested array — each attribute carries `CheckConstraints : ImmutableArray<AttributeOnDiskCheckConstraint>` with (Name, Definition, IsNotTrusted). V2 promotes CHECK constraints to **table-scoped** `Kind.ColumnChecks : ColumnCheck list` (chapter A.0' slice ε IR lift; L3-S5 sub-axiom). V2's placement aligns with SQL Server semantic — a CHECK constraint may span multiple columns; attribute-scoping was V1's mismodeling. V2 carries (SsKey + Name option + Definition + IsNotTrusted), structurally stronger via typed identity. Emitter consumer for CHECK constraints in DDL is a separate axis (matrix row 12 NOT-MAPPED — V2 IR carries but no emitter consumes yet).")>]
let ``5.2.α row 50: V1 attribute-nested CHECK arrays vs V2 table-scoped Kind.ColumnChecks (V2 corrects placement)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 50"

[<Fact(Skip = "Matrix row 51 — 🔵 V2-EXTENSION. V1's `AttributeReference` is an attribute-embedded optional object — each attribute carries an inline 6-field record (IsReference, TargetEntityId, TargetEntity, TargetPhysicalName, DeleteRuleCode, HasDatabaseConstraint) when the attribute is a FK. V2 lifts references out of `Attribute` into `Kind.References : Reference list` (chapter 4.2 + chapter 4.6 design). V2's `Reference` carries (SourceAttribute: SsKey, TargetKind: SsKey, OnDelete: ReferenceAction closed DU, HasDbConstraint: bool, RefEntityId: int option per chapter 5.0 slice δ cross-key-shape fix). The lift enables: (a) symmetric-closure pass (chapter 3.5); (b) topological ordering (chapter 3.7); (c) bidirectional SsKey navigation. V2's `ReferenceAction` DU (NoAction | Cascade | SetNull | Restrict) replaces V1's string `DeleteRuleCode` — typed exhaustiveness over open enum.")>]
let ``5.2.α row 51: V1 attribute-embedded AttributeReference vs V2 Kind.References lifted-out collection (V2 stronger)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 51"

[<Fact(Skip = "Matrix row 52 — 🔵 V2-EXTENSION. V1 carries `DataType : string` (free-form), `DefaultValue : string?` (raw default expression), `Length / Precision / Scale : int?` (typed dimensions but unfaceted). V2 carries `Type : PrimitiveType` (closed DU per A13 type-correspondence policy: Identifier, Integer, LongInteger, Text, Email, PhoneNumber, Boolean, DateTime, Date, Time, Decimal, Currency, BinaryData) + `DefaultValue : SqlLiteral option` (typed value with structural validation) + `Length / Precision / Scale : int option`. V2's typed primitives flow from pillar 1 (data-structure-oriented over string-parsing); decimal-scale invariants enforced at type level, not check time. V2's `PrimitiveType` is also where `parsePrimitiveType` (matrix row 10) lands additional V1 type variants as evidence demands.")>]
let ``5.2.α row 52: V1 DataType+DefaultValue strings vs V2 PrimitiveType+SqlLiteral typed (V2 stronger)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 52"

[<Fact(Skip = "Matrix row 53 — 🟠 NOT-MAPPED. V1's `AttributeOnDiskDefaultConstraint` carries the DEFAULT-constraint **envelope** — (Name : string, Definition : string, IsNotTrusted : bool). V2's `Attribute.DefaultValue : SqlLiteral option` carries only the Definition (as a typed value); the constraint metadata (Name + IsNotTrusted) is dropped at the adapter boundary. Trigger: extended-properties consumer needs constraint identity (e.g., manifest emitter naming constraints for operator-visible drift reports) OR DDL emitter needs to round-trip the V1 constraint name (preserving `DF_TableName_ColumnName` conventions across emit). Cash-out shape: extend V2's `Attribute.Default : DefaultConstraint option` (vs. current `DefaultValue : SqlLiteral option`) where `DefaultConstraint = { Name : Name option; Value : SqlLiteral; IsNotTrusted : bool }`. Migration impact: existing call sites consuming `DefaultValue` map to `Default |> Option.map (fun d -> d.Value)`.")>]
let ``5.2.α row 53: V1 default-constraint envelope (Name + IsNotTrusted) vs V2 SqlLiteral-only DefaultValue`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 53"

[<Fact>]
let ``5.2.α.attribute: domain-attribute parity file present`` () =
    Assert.True(true)
