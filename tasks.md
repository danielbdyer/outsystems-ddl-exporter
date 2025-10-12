# OutSystems DDL Exporter – Remaining Backlog (Release Candidate Focus)

> This backlog captures the open scope needed to finish the extraction → tightening → SMO emission pipeline described in the README while honoring the architectural guardrails and the living test plan. Completed work from earlier iterations has been collapsed into guardrail references so we can focus on the remaining shippable increments.

## 0. Foundational Stabilization
- [ ] **Unblock the .NET 9 build** by resolving the `System.Index` vs. `Microsoft.SqlServer.Management.Smo.Index` ambiguity in `SmoObjectGraphFactory`. Add a regression test in `Osm.Smo.Tests` to guard the namespace import order. *(Guardrails §5; Test Plan smoke prereq)*
- [ ] Review solution-wide nullable annotations and `LangVersion` settings so the compiler baseline matches the `notes/run-checklist.md` tooling expectations.
- [ ] Refresh the contributor onboarding docs (README + `notes/run-checklist.md`) with any additional steps discovered while fixing the current build break.

## 1. Policy & Decision Telemetry Hardening
- [ ] Finish codifying the tightening policy matrix for `Cautious`, `EvidenceGated`, and `Aggressive` modes, documenting every NOT NULL / UNIQUE / FK rule with references to profiling evidence requirements. Surface the matrix in `notes/design-contracts.md` and assert it with table-driven tests. *(Guardrails §4; Test Plan §14.1)*
- [ ] Expand decision telemetry so the CLI emits per-module rollups (tightened columns, skipped FKs with rationale) alongside the existing manifest. *(Guardrails §6; Test Plan §17)*
- [ ] Add property-based tests that prove decision monotonicity when null counts drop or orphans are eliminated. *(Test Plan §§11.1–11.2)*
- [ ] Document toggle precedence (CLI flags → environment → config) and ensure the policy engine logs when overrides change default behavior. *(Guardrails §2; Test Plan §§7.3–7.5)*

## 2. SMO & Emission Validation
- [ ] Extend `SmoModelFactoryTests` to assert NOT NULL / UNIQUE / FK propagation for the edge-case fixture so SMO output mirrors policy decisions end-to-end. *(Guardrails §5; Test Plan §4)*
- [ ] Add pre-remediation idempotence and batch limit tests for the emission path, ensuring reruns do not duplicate scripts or overrun configured thresholds. *(Test Plan §§8.1–8.2)*
- [ ] Verify FK scripting uses `WITH CHECK` and trusted states under positive evidence, and capture opt-out coverage when evidence is missing. *(Test Plan §8.3; Guardrails §8)*
- [ ] Create an SSDT import smoke test that compiles the emitted artifacts inside an SSDT project to prove downstream consumption. *(Test Plan §13.1)*
- [ ] Introduce mutation tests that shuffle index ordinals or toggle inactive columns to guarantee the SMO builder rejects invalid graphs. *(Test Plan §11.3; Guardrails §1)*

## 3. Evidence Extraction & Caching
- [ ] Implement the concrete `IAdvancedSqlExecutor` using `Microsoft.Data.SqlClient`, honoring Guardrail §10 by keeping database concerns in the Pipeline layer. *(Test Plan §18.5 prerequisite)*
- [ ] Stand up an integration test harness (containerized SQL Server or deterministic stub) that exercises the live extraction adapter and asserts parity with the fixture-generated JSON. *(Test Plan §18.5)*
- [ ] Add cache eviction heuristics so stale payloads age out when module selections shrink or toggles change, and cover the logic with unit + integration tests. *(Test Plan §18.6)*
- [ ] Surface timeout and sampling knobs for the extractor, propagate them through manifests, and validate they appear in cache telemetry. *(Test Plan §18.7; Guardrails §10)*
- [ ] Introduce evidence cache drift detection that invalidates cache entries when profiling row counts deviate beyond configured tolerances. *(Test Plan §8.4)*

## 4. Performance & Scale Readiness
- [ ] Execute wide-table profiling benchmarks to validate the mock profiler and tightening pipeline maintain acceptable throughput for tables with hundreds of columns. *(Test Plan §2.5; Guardrails §7)*
- [ ] Add large-entity-count integration tests (500 entities × 10 attributes) to prove the pipeline does not exhibit quadratic behavior. *(Test Plan §10.1; Guardrails §7)*
- [ ] Capture timing baselines for the hot policy and SMO paths on representative datasets; publish results in `notes/perf-readout.md`. *(Test Plan §10.2)*

## 5. Error Handling & Observability
- [ ] Improve JSON ingestion error messages with JSONPath context so CLI failures point to the exact offending field. *(Test Plan §9.1; Guardrails §6)*
- [ ] Enhance DMM parse diagnostics to include file, line, and token context on ScriptDom failures. *(Test Plan §9.2)*
- [ ] Harden filesystem error handling so unwritable output roots produce actionable messages and leave partial artifacts in a recoverable state. *(Test Plan §9.3)*
- [ ] Emit structured telemetry artifacts (JSON log + manifest + decision report) from CI matrix runs and document retention expectations. *(Test Plan §17.3; Guardrails §6)*
- [ ] Extend `notes/design-contracts.md` with sample failure payloads and telemetry schemas for downstream integrations. *(Architecture Guardrails §8)*

## 6. Security & Operations Enablement
- [ ] Validate that the profiler and extraction adapters operate correctly under read-only SQL principals with minimal permissions. *(Test Plan §12.1)*
- [ ] Author an operator runbook covering cache warm-up, toggle strategies for incremental hardening, and incident response for failed tightening decisions. *(Backlog §9 alignment)*
- [ ] Document strategies for substituting live profilers vs. fixture mocks, including environment variable recipes for CI/CD vs. local runs. *(Backlog §9; Guardrails §3)*

## 7. Quality Gates & Release Packaging
- [ ] Introduce `dotnet format` and Roslyn analyzer enforcement with `TreatWarningsAsErrors` for critical projects. *(Backlog §8; Guardrails §1)*
- [ ] Configure a GitHub Actions matrix (Linux + Windows) that runs the run-checklist commands, publishes CLI artifacts, and uploads telemetry bundles. *(Backlog §8; Test Plan §17.3)*
- [ ] Enable CodeQL and Dependabot (NuGet + GitHub Actions) to keep dependencies current and secure. *(Backlog §8)*
- [ ] Add OSS hygiene files (`LICENSE`, `CONTRIBUTING.md`, `CODEOWNERS`) and link them from the README onboarding section. *(Backlog §8)*
- [ ] Package the CLI as a global tool or self-contained distribution with versioned release notes once the backlog above is green. *(Release readiness)*

## 8. Forward-Looking Enhancements
- [ ] Design delta-only emission (schema diff) capability that reuses the SMO graph but limits output to tightened artifacts, documenting guardrails for drift detection. *(Roadmap continuation)*
- [ ] Explore multi-environment manifest comparisons to support dev/test/prod promotion workflows. *(Backlog §9)*
- [ ] Scope optional remediation-pack generation that translates policy "pre-remediation required" hints into executable SQL bundles behind a feature flag. *(Guardrails §8 roadmap)*
- [ ] Capture open performance and scalability questions (e.g., 200+ entity modules, streaming IO) for future grooming sessions in `notes/backlog-ideas.md`.

