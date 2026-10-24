namespace Projection.Core

open Projection.Core

/// H-028 — `IsPresentButInactive` surfacing in diagnostics. Reads
/// `Profile.AttributeRealities` and emits one `DiagnosticEntry` per
/// attribute where `IsPresentButInactive = true`.
///
/// These entries indicate attributes that are physically present at
/// the deployed target but logically inactive in the OutSystems model.
/// The operator can decide whether to exclude them via
/// `SelectionPolicy` or retain them with an explicit annotation.
///
/// **Pillar 9 classification.** Pure `DataIntent` scan — the
/// `IsPresentButInactive` flag derives entirely from the deployed-
/// target reflection in `AttributeReality`; no operator opinion is
/// introduced. The operator acts *after* seeing the diagnostic.
///
/// **Empty-profile identity.** Returns `[]` when
/// `Profile.AttributeRealities` is empty (the default
/// `Profile.empty` path used by `runWithConfig` until the
/// LiveProfiler is wired in). The function is a no-op in that case
/// and carries no runtime cost.
[<RequireQualifiedAccess>]
module InactiveAttributeDiagnostics =

    let private source = "selectionScan"

    let private inactiveAttributeCode = "selection.inactive-attribute"

    /// Emit one `DiagnosticEntry` per attribute whose
    /// `AttributeReality.IsPresentButInactive` is `true`. Returns
    /// `[]` when the profile's `AttributeRealities` list is empty.
    let emit (profile: Profile) : DiagnosticEntry list =
        profile.AttributeRealities
        |> List.choose (fun reality ->
            if reality.IsPresentButInactive then
                Some {
                    Source          = source
                    Severity        = DiagnosticSeverity.Warning
                    Code            = inactiveAttributeCode
                    Message         = "Attribute is physically present at the deployed target but is marked inactive in the OutSystems model. Review whether to exclude via SelectionPolicy or retain with an inactive-attribute annotation."
                    SsKey           = Some reality.AttributeKey
                    Metadata        = Map.empty
                    SuggestedConfig = None
                }
            else
                None)
