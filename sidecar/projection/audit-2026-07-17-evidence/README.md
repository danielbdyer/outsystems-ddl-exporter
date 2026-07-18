# Evidence bundle — AUDIT_2026_07_17 V1↔V2 parity

Companion to `../AUDIT_2026_07_17_V1_V2_PARITY.md`. All artifacts produced against tree `ea21b13`.

| File | What it is |
|---|---|
| `empirical-findings-ledger.md` | Findings proven by running BOTH engines against the shared live estate `ParityEstate` (localhost:11433) and diffing outputs + deployed databases. IDs `EF-*` (functional), `EP-*` (parity-confirmed), `EA-*` (aesthetic), `ED-*` (determinism), `EI-*` (interop), `EO-*` (operational). |
| `code-verifications.md` | The contestable high-severity claims I re-verified PERSONALLY by reading current code (`file:line`) rather than trusting static analysis. IDs `V-*`. Includes the topo-order blocker, the v1 FK cascade-case bug, the post-deploy lane-order hazard, and the user-seed gap. |
| `deployed-catalog.diff` | The `sys`-catalog diff between the two round-trip databases (`ParityV1DB` vs `ParityV2DB`): columns/types/nullability/defaults/identity/collation, FKs (actions+trust), indexes (options), triggers, extended properties, checks, schemas, row counts. This is the "are the loaded databases identical" evidence. |
| `findings-register.json` | The full consolidated register from the 13-agent static + empirical comparison (naming, ddl-shape, types, indexes, fks, data-lanes, bundle, + phase-3 mechanism-pinning/manifests/table-diffs/interop/sqlproj/console-ux). ~246 findings with per-finding v1/v2 cites, classification, direction, severity, confidence. |
| `reproduce.sh` | One-shot script to rebuild both engines, seed the shared estate, and run both `full-export` and `publish` against it. |

Provenance rule: empirical (`EF/EP/…`) and personally-verified (`V-*`) findings are ground
truth; static-analysis findings in `findings-register.json` are cross-checked against goldens
and current code but lane-specific claims are flagged in the report where not empirically
exercised. When any of this disagrees with the code or `DECISIONS.md`, they win.
