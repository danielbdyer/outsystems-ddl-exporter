# Profiling Output Alignment Notes

These notes document the core concerns the CLI must surface for both single-environment profiling runs and multi-environment consensus captures. They reflect the tightened console contract introduced while addressing the profiler usability feedback.

## Single-Environment Profiler Output

- **NOT NULL enforcement**
  - Highlight violating columns with row counts, null percentages, and severity scoring so remediation can be prioritized.
  - Surface sampling gaps by showing probe status alongside null statistics to flag when the evidence itself is incomplete.
- **Unique key readiness**
  - Group issues by severity and scope (single vs. composite keys) and summarize the number of critical vs. warning-level risks before listing detailed rows.
  - Preserve probe diagnostics so duplicate detections and coverage warnings are distinguishable.
- **Foreign key integrity**
  - Call out orphaned relationships, `NO CHECK` definitions, and probe failures with severity summaries to guide corrective action order.
- **Evidence hygiene**
  - Retain overflow hints for each table so operators know additional rows exist beyond the default console window.

## Multi-Environment Profiler Output

- **Environment-at-a-glance table**
  - Continue reporting per-environment module counts, probe coverage, and anomaly totals with clear labeling of primary vs. secondary sources.
- **Readiness digest**
  - Surface secondary environments that exceed the primary's null, duplicate, or orphan counts so operators see "Review <env> data quality" nudges before diving into the detailed findings.
- **Findings feed**
  - Elevate cross-environment drifts (nulls, uniqueness, foreign keys, evidence gaps) with explicit action guidance so the highest-severity data issues are obvious.
- **Constraint readiness digest**
  - Summarize DDL blockers by constraint type, including the worst consensus ratio and recommendation text, so teams can see exactly what prevents a safe rollout.
  - When the configured consensus threshold is less than unanimity, list the "safe but non-unanimous" watchlist so partial agreement still receives scrutiny.
- **Detailed consensus tables**
  - Maintain safe and unsafe constraint tables ordered by consensus ratio to support drill-down after reviewing the digest.

## Follow-On Considerations

- Align the CLI guidance with the tightening policy documentation in `notes/design-contracts.md` so policy toggles and console cues stay synchronized.
- Consider persisting the digest sections into structured telemetry once the CLI emission contracts are finalized (Guardrails §6).
- Evaluate whether remediation bundles should hyperlink directly from the digest to future SQL repair packs (Backlog §8 roadmap).
