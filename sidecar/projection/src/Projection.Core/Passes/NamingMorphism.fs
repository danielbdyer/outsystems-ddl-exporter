namespace Projection.Core.Passes

open Projection.Core

/// The naming-morphism pass — A15. Applies a `Name -> Name` morphism
/// across every named node in the catalog (modules, kinds, attributes,
/// references). Identity (`SsKey`) is **untouched** by construction:
/// the pass operates only on `Name` fields. The compiler enforces
/// this — `SsKey` does not appear on the right-hand side of any
/// assignment in this module.
///
/// The morphism is a parameter to the pass for now. When an emitter
/// proves it needs the morphism baked into Policy, a `Naming` axis can
/// be added without touching this pass's signature (the pass will
/// receive the morphism from `Policy.Emission.Naming.Transform` or
/// equivalent). Don't pre-add the field — see DECISIONS.md
/// "IR grows under evidence."
[<RequireQualifiedAccess>]
module NamingMorphism =

    /// Pass version. Bump when the rename-event semantics change.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "namingMorphism"

    /// A naming morphism: a pure function from `Name` to `Name`. The
    /// identity morphism is `id`; composition is function composition.
    type Morphism = Name -> Name

    let private renamedEvent (key: SsKey) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = key
          TransformKind = Renamed }

    /// Apply the morphism to a single name. If the name is unchanged the
    /// caller emits no event (lineage records actual renames, not
    /// no-ops).
    let private rename (m: Morphism) (key: SsKey) (name: Name) (events: LineageBuffer.Buffer) : Name =
        let after = m name
        if after <> name then LineageBuffer.add (renamedEvent key) events
        after

    let private renameAttribute (m: Morphism) (events: LineageBuffer.Buffer) (a: Attribute) : Attribute =
        { a with Name = rename m a.SsKey a.Name events }

    let private renameReference (m: Morphism) (events: LineageBuffer.Buffer) (r: Reference) : Reference =
        { r with Name = rename m r.SsKey r.Name events }

    let private renameStaticRow (row: StaticRow) : StaticRow =
        // Static-row identifiers are SsKeys (untouched per A15) and
        // their `Values` map is keyed by `Name`. Rekeying the map by
        // morphed names is a separate concern — it would change the
        // shape of the row from the consumer's perspective. For this
        // pass we leave row values untouched; a later emitter-side
        // morphism (or a different pass) handles per-row name
        // rewriting if needed.
        row

    let private renameModality (m: ModalityMark) : ModalityMark =
        match m with
        | Static rows   -> Static (rows |> List.map renameStaticRow)
        | TenantScoped  -> TenantScoped
        | SoftDeletable -> SoftDeletable

    let private renameKind (m: Morphism) (events: LineageBuffer.Buffer) (k: Kind) : Kind =
        { k with
            Name       = rename m k.SsKey k.Name events
            Attributes = k.Attributes |> List.map (renameAttribute m events)
            References = k.References |> List.map (renameReference m events)
            Modality   = k.Modality   |> List.map renameModality }

    let private renameModule (m: Morphism) (events: LineageBuffer.Buffer) (mdl: Module) : Module =
        { mdl with
            Name  = rename m mdl.SsKey mdl.Name events
            Kinds = mdl.Kinds |> List.map (renameKind m events) }

    /// Run the pass. Walks the catalog top-to-bottom; for every named
    /// node the morphism is applied and a `Renamed` lineage event is
    /// emitted **only when the morphism produced a different name**
    /// (no-op morphisms emit no events). Identity is preserved across
    /// the entire pass (A3, A4, A15) — `SsKey` fields are byte-
    /// identical between input and output.
    let run (morphism: Morphism) (c: Catalog) : Lineage<Catalog> =
        let events = LineageBuffer.create ()
        let renamed =
            { Modules = c.Modules |> List.map (renameModule morphism events) }
        Lineage.ofValueAndEvents (LineageBuffer.toList events) renamed

    /// Convenience constructors for common morphisms. Each is a pure
    /// `Name -> Name` and composable via `>>`.

    /// Lowercase every character.
    let toLower : Morphism = fun (Name n) -> Name (n.ToLowerInvariant())

    /// Uppercase every character.
    let toUpper : Morphism = fun (Name n) -> Name (n.ToUpperInvariant())

    /// Prepend a fixed string.
    let withPrefix (prefix: string) : Morphism =
        fun (Name n) -> Name (prefix + n)

    /// Append a fixed string.
    let withSuffix (suffix: string) : Morphism =
        fun (Name n) -> Name (n + suffix)

    /// The identity morphism — names pass through untouched. Useful for
    /// tests and for "no naming policy" pipelines.
    let identity : Morphism = id
