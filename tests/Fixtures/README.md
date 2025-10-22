# Test Fixtures

This directory hosts deterministic JSON datasets that back the fixture-first strategy outlined in the architecture guardrails. The `model.edge-case.json` payload exercises static entities, external entities, inactive-but-physical columns, delete rule permutations, multi-column indexes, platform auto-indexes, and cross-schema nuances. The `profiling/profile.edge-case.json` companion captures the mocked `Columns`, `UniqueCandidates`, and `FkReality` profiler outputs used by test doubles.

Micro fixtures (F1–F3) live alongside the edge-case dataset to drive targeted policy and pipeline tests:

| Fixture | Purpose |
| --- | --- |
| `model.micro-unique.json` + `profiling/profile.micro-unique*.json` | Single-module user entity with a unique email column; supports clean and null-drift variations for tightening and remediation tests. |
| `model.micro-unique-composite.json` + `profiling/profile.micro-unique-composite*.json` | Composite unique index that differentiates clean evidence from duplicate findings for multi-column tightening scenarios. |
| `model.micro-fk-protect.json` + `profiling/profile.micro-fk-protect.json` | Parent/child relationship with Protect semantics and clean data to validate NOT NULL + FK creation paths. |
| `model.micro-fk-default-delete-rule.json` + `profiling/profile.micro-fk-default-delete-rule.json` | Parent/child reference without an explicit delete rule to document the default `NoAction` behavior and ensure FK creation stays enabled. |
| `model.micro-fk-ignore.json` + `profiling/profile.micro-fk-ignore.json` | Ignore delete rule with orphaned children to assert defensive skips on FK creation and NOT NULL tightening. |
| `model.micro-physical.json` + `profiling/profile.micro-physical.json` | Entity with a physically enforced non-null column to verify policy reactions when catalog metadata already guarantees NOT NULL. |
| `model.legacy-guid-reference.json` | Minimal module demonstrating recovery of `btGUID*GUID` legacy reference encoding and the resulting FK relationship. |
| `policy/kernel-model.json` + `policy/kernel-profile.json` | Golden tightening vectors that document how Cautious, EvidenceGated, and Aggressive modes react to shared signals across nullability, foreign keys, and uniqueness. |

Use the `Tests.Support.FixtureFile`, `ModelFixtures`, and `ProfileFixtures` helpers to locate and open these assets from any test project without hard-coding relative paths.
