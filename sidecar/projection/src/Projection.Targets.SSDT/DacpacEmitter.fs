namespace Projection.Targets.SSDT

open System
open System.IO
open Microsoft.SqlServer.Dac
open Microsoft.SqlServer.Dac.Model
open Projection.Core

/// Π_DACPAC — chapter 3.x slice α substantive deliverable. The
/// **dev-tooling** sibling-Π emitter complementing the production
/// `SsdtDdlEmitter.emitSlices` directory bundle. Produces a
/// `.dacpac` byte stream the dev team can one-click stand up via
/// `sqlpackage.exe`, Visual Studio "Publish DAC Package," or
/// `DacServices.Deploy` to a local SQL Server instance.
///
/// Per `DECISIONS 2026-05-11 — Chapter 3.x DacpacEmitter open`:
///   - **Dev-tooling scope, not production deploy path.** Production
///     stays SSDT-style file deploy via `SsdtDdlEmitter.emitSlices`.
///   - **Pure F# wrapper inside `Projection.Targets.SSDT`** (no C#
///     subproject); DacFx's V2-relevant surface is small and the
///     `IDisposable`-aware calls F# handles natively via `use`. The
///     `DECISIONS 2026-05-09 — Adapter language choice` conditional
///     clause "DacFx — *if* its API turns out to be unfriendly from
///     F#" fell on the F#-friendly side empirically.
///   - **T1 amendment for binary emitters: content-equality via
///     DacFx round-trip**, not byte-equality. DacFx embeds wall-
///     clock timestamps in `Origin.xml` so two emit calls on the
///     same Catalog produce non-byte-identical streams; the
///     algebraic claim holds at the DacFx model level.
///
/// **Pillar 7 (gold-standard library precedence) holds end-to-end**:
/// statement generation flows through `SsdtDdlEmitter.statements`
/// (already typed-AST via ScriptDom); script ingestion flows through
/// `ScriptDomGenerate.toText`; `.dacpac` serialization flows through
/// `Microsoft.SqlServer.Dac.DacPackageExtensions.BuildPackage`. No
/// `StringBuilder` of bytes; no `System.IO.Packaging` zip surgery.
///
/// **Slice scope (chapter 3.x §α):** single-Kind / multi-attribute
/// catalog → `byte[]`; FKs and modality marks land in slices β / ε.
/// Slice α exercises `CreateTable` only via `SsdtDdlEmitter
/// .statements` (which already filters to typed DDL for SSDT).
[<RequireQualifiedAccess>]
module DacpacEmitter =

    /// Pass version. Bump when the DACPAC emission shape changes in
    /// a way that matters for cross-version comparators.
    [<Literal>]
    let version : int = 1

    /// Package manifest identity defaults. Per pre-scope §6.8: avoid
    /// embedding wall-clock time anywhere in metadata fields the
    /// emitter controls. Slice α uses constants; per-Catalog
    /// derivation (`Name = catalog-derived`, `Version = snapshot
    /// hash`) lands when a consumer demands it.
    [<Literal>]
    let private DefaultPackageName : string = "ProjectionCatalog"

    [<Literal>]
    let private DefaultPackageVersion : string = "1.0.0.0"

    [<Literal>]
    let private DefaultPackageDescription : string =
        "Projection dev-tooling DACPAC (chapter 3.x; not production deploy)"

    /// Filter the Π statement stream to DDL only — DacFx's schema
    /// model accepts `CREATE TABLE` / `CREATE INDEX` etc., not row
    /// inserts or `SET IDENTITY_INSERT`. `SsdtDdlEmitter.statements`
    /// already produces DDL only today, but the filter is the
    /// structural guarantee for future stream-extending slices.
    let private isSchemaStatement (s: Statement) : bool =
        match s with
        | CreateTable _ | CreateIndex _ | SetExtendedProperty _
        | AlterTableNoCheckConstraint _ | AlterIndexDisable _ -> true
        | InsertRow _ | SetIdentityInsert _ | Comment _ | Blank -> false

    /// Render one DDL Statement into its standalone T-SQL form via
    /// the ScriptDom typed-AST renderer. Per-statement ingestion is
    /// DacFx's expected `AddObjects` shape (the alternative —
    /// multi-statement scripts — requires `GO` batch separators
    /// that DacFx interprets at the Public Model API surface; per-
    /// statement avoids the separator-grammar coupling).
    let private statementToScript (statement: Statement) : string option =
        match ScriptDomBuild.buildStatement statement with
        | Some fragment -> Some (ScriptDomGenerate.generateOne fragment)
        | None -> None

    /// Construct a `TSqlModel` and ingest each DDL statement
    /// separately. Pinned target version `Sql160` mirrors the V1
    /// trunk's ScriptDom Sql160 pin; surface a version-pin DECISIONS
    /// amendment if the cutover team confirms a different production
    /// SQL Server version (pre-scope §6.7).
    ///
    /// Per-statement `AddObjects` is DacFx-canonical: each call
    /// accepts a single statement (or `GO`-separated batch). Feeding
    /// the filtered Π stream one statement at a time keeps the
    /// emitter free of batch-separator grammar.
    let private buildModel (statements: Statement seq) : TSqlModel =
        let model = new TSqlModel(SqlServerVersion.Sql160, TSqlModelOptions())
        for statement in statements do
            match statementToScript statement with
            | Some script when not (String.IsNullOrWhiteSpace script) ->
                model.AddObjects script
            | _ -> ()
        model

    /// Serialize the model to `.dacpac` bytes via DacFx's
    /// `DacPackageExtensions.BuildPackage`. `MemoryStream` keeps
    /// the bytes in-process — no file system touch in Core or
    /// Targets (`F#-pure-core / no-I/O` posture, with DacFx's
    /// internal I/O confined to the in-memory stream).
    let private buildPackageBytes (model: TSqlModel) : byte[] =
        use stream = new MemoryStream()
        let metadata =
            PackageMetadata(
                Name = DefaultPackageName,
                Description = DefaultPackageDescription,
                Version = DefaultPackageVersion)
        DacPackageExtensions.BuildPackage(stream, model, metadata)
        stream.ToArray()

    let private dacFxFailure (code: string) (message: string) : Result<'a> =
        Result.failureOf (ValidationError.create code message)

    /// Emit a Catalog as `.dacpac` bytes. Failure modes (DacFx
    /// validation errors, malformed script ingestion) surface as
    /// `ValidationError` carrying DacFx's exception message.
    ///
    /// **T1 for binary emitters (content-equality).** Two
    /// invocations on the same Catalog produce DacFx models with
    /// identical `GetObjects(Table.TypeClass)` enumerations. The
    /// byte streams DIFFER (Origin.xml embeds wall-clock); the
    /// algebraic claim holds at the model level. Round-trip via
    /// `DacPackage.Load(stream)` + `new TSqlModel(...)` validates
    /// content identity (see `DacpacEmitterTests` slice α).
    let emit (catalog: Catalog) : Result<byte[]> =
        use _ = Bench.scope "emit.dacpac.emit"
        try
            let statements =
                SsdtDdlEmitter.statements catalog
                |> Seq.filter isSchemaStatement
            use model = buildModel statements
            let bytes = buildPackageBytes model
            Result.success bytes
        with
        | ex ->
            dacFxFailure
                "emitter.dacpac.failed"
                (String.Concat("DacFx emission failed: ", ex.Message))

    // -----------------------------------------------------------------------
    // Slice A.4.7'-prelude.dacpac-registry — `registeredMetadata`
    // entry for the DacpacEmitter sibling-Π realization. Closes the
    // last sibling-Π registry gap (every other Π emitter shipped
    // `registeredMetadata` during slice 5.13.sibling-emitter-registry-*
    // / 5.13.data-emission-registry; DacpacEmitter was the lone
    // holdout).
    //
    // **Classification.** All Sites carry `DataIntent`. The emitter
    // signature is `Catalog → Result<byte[]>` (per A18 amended:
    // Catalog only; no Profile, no Policy). DacFx model construction +
    // serialization are V2-controlled boundary translations into a
    // Microsoft typed library; no operator policy enters at any site.
    //
    // **T1 amendment.** Per the file header: binary emitters carry
    // a content-equality form of T1 (DacFx round-trip equality on
    // model objects), not byte-equality, because DacFx embeds wall-
    // clock timestamps in `Origin.xml`. The Sites prose names this
    // explicitly so consumers don't expect byte-determinism.
    // -----------------------------------------------------------------------

    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "dacpacEmitter" Schema
            [ TransformSite.dataIntent "schemaStatementFilter"
                "Filter the Π statement stream (`SsdtDdlEmitter.statements`) to DDL-only via closed-DU predicate `isSchemaStatement` — admits CreateTable / CreateIndex / SetExtendedProperty / AlterTableNoCheckConstraint / AlterIndexDisable; rejects InsertRow / SetIdentityInsert / Comment / Blank (DacFx's Public Model API accepts schema objects, not row data). Pure DataIntent — the filter is structural; no Policy enters."
              TransformSite.dataIntent "statementIngestion"
                "Per-statement `TSqlModel.AddObjects` via `ScriptDomBuild.buildStatement` → `ScriptDomGenerate.generateOne` rendered script. Per-statement (not multi-statement-batch) avoids DacFx's `GO`-separator grammar coupling; each statement enters the DacFx model as a single typed object. Statement-text is byte-deterministic via Sql160ScriptGenerator's pinned options."
              TransformSite.dataIntent "packageMetadata"
                "DacFx `PackageMetadata` (Name = `ProjectionCatalog`, Description = dev-tooling brief, Version = `1.0.0.0`). Per pre-scope §6.8: no wall-clock embedding in emitter-controlled fields. Constants today; per-Catalog derivation (Name = catalog-derived, Version = snapshot hash) lands when a consumer demands it. DataIntent — V2-controlled defaults, no operator opinion."
              TransformSite.dataIntent "packageBuild"
                "`DacPackageExtensions.BuildPackage(stream, model, metadata)` serializes the TSqlModel to `.dacpac` bytes via `MemoryStream` (no file system I/O in Core/Targets; DacFx's internal zip plumbing confined to the in-memory stream). The output is a Microsoft-canonical `.dacpac` consumable by `sqlpackage.exe`, Visual Studio Publish DAC Package, or `DacServices.Deploy`."
              TransformSite.dataIntent "emit"
                "Π port realization — `Catalog → Result<byte[]>`. Sibling-Π to `SsdtDdlEmitter.emitSlices` (directory bundle for production deploy) + `JsonEmitter.emit` (JSON manifest for downstream consumers). T1 binary amendment: content-equality via DacFx round-trip (two emit calls produce non-byte-identical streams due to Origin.xml wall-clock embedding; the algebraic claim holds at the model level per DacpacEmitterTests' content-determinism test)." ]
