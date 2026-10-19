# Review Feedback Digest – September 2024 Pass

This digest captures the outstanding asks from the latest repo review so we can track remediation alongside `tasks.md` and the living test plan.

## Immediate (P0)
- ✅ Extracted the Advanced SQL export into `src/AdvancedSql/outsystems_model_export.sql` and referenced it from the README.
- ✅ Publish fixture-driven E2E outputs (edge case plus rename override) and keep them fresh whenever the emission pipeline evolves.
- ☐ Stand up a GitHub Actions pipeline (Windows/Linux) that restores, builds, tests, and runs a CLI smoke against fixtures; publish artifacts for inspection.
- ✅ Ensure CLI emits machine-readable outputs (`policy-decisions.json`, `dmm-diff.json`) and document how to surface them in PR automation (README appendix).
- ✅ Verified platform auto-index toggles through SMO + SSDT emission tests so OSIDX scripts stay opt-in.

## Near-term (P1)
- ✅ Implement config/env overrides for profiler selection, cache roots, and connection strings; provide `config/appsettings.example.json`.
- ☐ Wire CodeQL and Dependabot for NuGet + workflow updates.
- ✅ Extend the DMM comparator to canonicalize data types and support inline PK syntax.
- ☐ Flesh out `notes/design-contracts.md` with DTO examples, failure codes, and telemetry expectations.

## Medium-term (P2+)
- ☐ Add profiler sampling/timeouts plus cache drift detection heuristics.
- ☐ Add OSS hygiene docs (`LICENSE`, `CONTRIBUTING.md`, `CODEOWNERS`).
- ☐ Cover ScriptDom normalization fuzz tests and FK cross-schema toggles as the live SQL adapter hardens.

Cross-reference this digest during planning sessions to ensure we keep marching toward the guardrails documented in `architecture-guardrails.md` and `tasks.md`.
