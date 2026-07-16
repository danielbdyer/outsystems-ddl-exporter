namespace Projection.Targets.SSDT

open System.IO
open System.Xml

/// Π_Sqlproj — the SDK-style `Microsoft.Build.Sql` `.sqlproj` that turns V2's
/// emitted SSDT bundle into a cross-platform `dotnet build`-able project
/// producing a `.dacpac` (the operator's Azure DevOps / `sqlpackage` deploy
/// surface). SDK-style (not classic SSDT) so it builds on Linux CI with
/// `dotnet build`, no Visual Studio / Windows-only MSBuild targets.
///
/// The Microsoft.Build.Sql SDK's default glob includes `**/*.sql` as schema
/// `Build` items, so the per-table `Modules/**/*.sql` are picked up
/// automatically — this emitter does NOT enumerate them. It REMOVES the
/// post-deploy script + the `Data/*.sql` lanes from that default Build glob and
/// re-adds them as `PostDeploy` / `None` (the lanes are `:r`-included by the
/// post-deploy, not compiled as schema objects).
///
/// Typed XML via `System.Xml.XmlWriter` (no hand-rolled XML string-builder, per
/// the typed-AST-first emission discipline — `XmlWriter` is the BCL obligation
/// for XML, like `Sql160ScriptGenerator` is for T-SQL).
[<RequireQualifiedAccess>]
module SqlprojEmitter =

    /// The Microsoft.Build.Sql SDK version the emitted project pins, so a
    /// `dotnet build` of the bundle restores a known SDK. Pinned to the latest GA
    /// (verified buildable 2026-06-24 — inline `Sdk="…/2.2.0"` + a `nuget.config`
    /// resolves the SDK and produces a `.dacpac`); bump under a real build-failure
    /// trigger (the gated `.sqlproj`-build test is the canary).
    [<Literal>]
    let sdkVersion : string = "2.2.0"

    /// The Sql160 database schema provider (matches the `TSqlModel`/ScriptDom
    /// Sql160 pin the rest of the SSDT target uses).
    [<Literal>]
    let private dsp : string = "Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider"

    /// The emitted project's default name (the `.dacpac` base name). Mirrors
    /// `DacpacEmitter`'s `ProjectionCatalog` so the dev-tooling artifacts agree.
    [<Literal>]
    let projectName : string = "ProjectionCatalog"

    [<Literal>]
    let fileName : string = "ProjectionCatalog.sqlproj"

    /// The bundle-relative `.refactorlog` the project carries when the run
    /// is store-threaded (G3, DECISIONS 2026-07-16). Named `<project>
    /// .refactorlog` per SSDT convention; mirror `Compose.ArtifactPath
    /// .refactorLog` — the `.sqlproj`-build test pins the pairing.
    [<Literal>]
    let refactorLogFileName : string = "ProjectionCatalog.refactorlog"

    /// Emit the `.sqlproj` XML. `dataLaneRelPaths` are the bundle-relative
    /// `Data/*.sql` paths (re-classified `None`, `:r`-included by the
    /// post-deploy); `hasPostDeploy` adds the `Script.PostDeployment.sql`
    /// `PostDeploy` item; `hasRefactorLog` adds the `<RefactorLog>` item
    /// for the bundle's accumulated `ProjectionCatalog.refactorlog` (G3 —
    /// present exactly when the run was store-threaded, so DacFx converts
    /// renames into `sp_rename` instead of DROP+CREATE on incremental
    /// publish). The schema `.sql` under `Modules/**` ride the SDK's
    /// default `Build` glob and are intentionally not enumerated.
    let emit (dataLaneRelPaths: string list) (hasPostDeploy: bool) (hasRefactorLog: bool) : string =
        let settings =
            XmlWriterSettings(
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true,
                NewLineChars = "\n")
        use sw = new StringWriter()
        (
            use w = XmlWriter.Create(sw, settings)
            let elem (name: string) (attr: string) (value: string) =
                w.WriteStartElement name
                w.WriteAttributeString(attr, value)
                w.WriteEndElement()

            w.WriteStartElement "Project"
            w.WriteAttributeString("Sdk", System.String.Concat("Microsoft.Build.Sql/", sdkVersion))  // LINT-ALLOW: terminal XML attribute value (SDK ref) at the XmlWriter boundary

            w.WriteStartElement "PropertyGroup"
            w.WriteElementString("Name", projectName)
            w.WriteElementString("DSP", dsp)
            w.WriteElementString("ModelCollation", "1033, CI")
            w.WriteEndElement() // PropertyGroup

            // Re-classify the non-schema .sql out of the SDK's default Build glob:
            // the post-deploy is `PostDeploy`; the data lanes are `None` (referenced
            // by the post-deploy's `:r` includes, never compiled as schema).
            w.WriteStartElement "ItemGroup"
            if hasPostDeploy then
                elem "Build" "Remove" PostDeployEmitter.fileName
                elem "PostDeploy" "Include" PostDeployEmitter.fileName
            for p in dataLaneRelPaths do
                elem "Build" "Remove" p
                elem "None" "Include" p
            // G3 — the accumulated refactorlog rides the project as an
            // explicit `RefactorLog` item so `dotnet build` embeds it into
            // the `.dacpac` (refactor.xml) and incremental publish renames
            // instead of DROP+CREATE. Explicit rather than glob-reliant:
            // the SDK's default-item surface varies across versions; the
            // gated `.sqlproj`-build test proves the pairing compiles.
            if hasRefactorLog then
                elem "RefactorLog" "Include" refactorLogFileName
            w.WriteEndElement() // ItemGroup

            w.WriteEndElement() // Project
            w.Flush()
        )
        // XmlWriter omits a trailing newline; add one for POSIX-clean files (T1).
        System.String.Concat(sw.ToString(), "\n")  // LINT-ALLOW: terminal newline suffix on the XmlWriter-rendered .sqlproj text at the writer boundary
