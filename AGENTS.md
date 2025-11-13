# Agent Guidelines

## Pre-flight & Alignment
- Begin every working session by executing the `notes/run-checklist.md` steps (install/verify .NET 9 SDK, restore, build, test, optional CLI smoke) so the environment is proven green before coding; document any deviations.
- Always consult `tasks.md` at the start of each task to stay aligned with the current execution plan; update it when new work items emerge.
- Revisit `architecture-guardrails.md` whenever making design or implementation decisions to ensure architectural guardrails are upheld.
- Reference the checklist, task backlog, and guardrails in status updates or planning summaries so stakeholders can trace work back to the roadmap and guardrails.

## Navigation Aids (use these before spelunking)
- `notes/meta/directory-map.md` – one-page map of repo layers and where key responsibilities live.
- `notes/meta/rg-signposts.md` – copy/paste ripgrep commands for the policy surface, SMO emission code, evidence cache, and CLI wiring.
- `notes/meta/toggle-surface.md` – table of tightening toggle keys, meanings, and extension steps for new flags.
- `notes/meta/test-matrix.md` – project-by-project test command cheatsheet (unit + integration + CLI smoke) so you can cite exact coverage in PRs.

Keep these references handy whenever you need to cite locations, tests, or toggles; doing so avoids wasting tokens on exploratory shell work.
