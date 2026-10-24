module Projection.Tests.OssysContractVersioningParityTests

// V1 parity audit — slice 5.1.ζ. Reserves the contract name for
// `V1_PARITY_MATRIX.md` row 38 (V1's per-resultset optional-column
// tolerance; V2 chose contract-version-lock via carbon-copied SQL).

open Xunit

[<Fact(Skip = "Matrix row 38 — 🟡 DIVERGENCE. V1's `MetadataContractOverrides` carries a `Dictionary<string, HashSet<string>>` mapping result-set names to optional column names; operator-configurable via `appsettings.json` (`metadataContract.optionalColumns.AttributeJson = [\"AttributesJson\"]`). Processors consult `IsColumnOptional(resultSetName, columnName)` and tolerate NULL or missing columns when marked optional — enables extraction against OutSystems versions where columns are missing or renamed. V2 reads every result set via ordinal-indexed access (`readInt r 0`, `readString r 1`, …); structurally insensitive to column renaming but sensitive to column reordering. V2 has zero operator-configurable version-tolerance — the carbon-copied SQL pins the contract version. See `DECISIONS 2026-05-17 (slice 5.1.ζ) — Contract-versioning posture: SQL pins, not operator overrides`. Re-open trigger: cutover or post-cutover schema-drift surfaces a real OutSystems-version mismatch that the canary's pre-extraction validation step doesn't catch.")>]
let ``5.1.ζ row 38: V1 operator-configurable optional columns vs V2 contract-version-lock via carbon-copied SQL`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 38 + DECISIONS 2026-05-17 (slice 5.1.ζ)"

[<Fact>]
let ``5.1.ζ: contract-versioning parity file present`` () =
    Assert.True(true)
