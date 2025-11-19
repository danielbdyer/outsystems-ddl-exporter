# Implementation Specifications

This directory contains detailed technical specifications for implementing the tasks outlined in `tasks.md`. Each specification provides a complete blueprint for implementation, including architecture, data models, integration points, and test scenarios.

## Purpose

These specifications serve as:
- **Implementation guides** for when development environment is fully operational
- **Design documentation** capturing architectural decisions
- **Reference materials** for understanding system evolution
- **Handoff artifacts** enabling context-free pickup of work

## Structure

Each specification follows a consistent format:
1. **Overview** - Task context and goals
2. **Architecture** - High-level design and component relationships
3. **Data Models** - Schema definitions and contracts
4. **Implementation Details** - Step-by-step breakdown with code locations
5. **Integration Points** - How new code connects to existing systems
6. **Test Scenarios** - Verification requirements and edge cases
7. **Migration Path** - How to deploy without breaking existing functionality

## Specifications

### Milestone 1: Export Artifact Verification

#### Core Functionality (Ship First)
- [M1.0-global-topological-ordering.md](./M1.0-global-topological-ordering.md) - **🔴 MVP**: Bootstrap snapshot, global FK ordering, and observability measures
- [M1.1-export-verification-framework.md](./M1.1-export-verification-framework.md) - **🔴 MVP**: Export verification system (manifest/artifact validation)

#### Basic Validation (Ship After MVP)
- [M1.2-topological-proof-validation.md](./M1.2-topological-proof-validation.md) - **🟡 MVP**: Runtime ordering validation (fail-fast checks)
- [M1.3-data-integrity-checks.md](./M1.3-data-integrity-checks.md) - **🟡 MVP**: Basic row count and NULL checks (quick sanity verification)

#### Production Enhancements (Ship Later)
- [M1.5-per-table-emission.md](./M1.5-per-table-emission.md) - **🟡 Enhancement**: Per-table baseline seed emission (fine-grain version control) - TBD

#### Advanced Validation & Observability (Ship When Requested)
- [M1.7-topological-proof-generation.md](./M1.7-topological-proof-generation.md) - **🔵 Deferred**: Full topological proof artifacts (documentation/auditing)
- [M1.8-data-integrity-verification.md](./M1.8-data-integrity-verification.md) - **🔵 Deferred**: Comprehensive data integrity (DMM replacement)
  - Includes Appendix A: Research findings on checksum algorithms, NULL handling, and pipeline architecture
- [M1.4-verification-test-coverage.md](./M1.4-verification-test-coverage.md) - Test scenarios for verification systems

### Milestone 2: UAT-Users Transformation Guarantees
- [M2.1-uat-users-verification-framework.md](./M2.1-uat-users-verification-framework.md) - UAT-users pipeline verification
- [M2.2-transformation-verification.md](./M2.2-transformation-verification.md) - Specialized transformation layer
- [M2.3-uat-users-integration-tests.md](./M2.3-uat-users-integration-tests.md) - Edge case test coverage

### Milestone 3: Integrated Workflow & Operational Readiness
- [M3.1-manifest-extensions.md](./M3.1-manifest-extensions.md) - FullExportRunManifest UAT-users metadata
- [M3.2-load-harness-extension.md](./M3.2-load-harness-extension.md) - End-to-end ETL verification
- [M3.3-idempotence-tests.md](./M3.3-idempotence-tests.md) - Determinism and reproducibility tests
- [M3.4-verification-contract-documentation.md](./M3.4-verification-contract-documentation.md) - Operator documentation

### Milestone 4: Performance & Security Validation
- [M4.1-performance-benchmarking.md](./M4.1-performance-benchmarking.md) - Scale testing framework
- [M4.2-security-validation.md](./M4.2-security-validation.md) - Permission and compliance validation

### Additional Sections
- [Section-1-policy-telemetry.md](./Section-1-policy-telemetry.md) - Policy matrix and decision telemetry
- [Section-2-smo-emission.md](./Section-2-smo-emission.md) - SMO validation and edge cases
- [Section-3-evidence-caching.md](./Section-3-evidence-caching.md) - Evidence extraction and cache management
- [Section-5-error-observability.md](./Section-5-error-observability.md) - Error handling and telemetry
- [Section-7-quality-gates.md](./Section-7-quality-gates.md) - Release packaging and hygiene

## Reading Order

**🔴 START HERE for deadline-driven delivery:**
1. **M1.0-global-topological-ordering.md** - Bootstrap snapshot solution with observability (includes critical path analysis)
2. **M1.1-export-verification-framework.md** - Artifact verification (parallel track)
3. **M1.2-topological-proof-validation.md** - Runtime ordering validation (basic safety)
4. **M1.3-data-integrity-checks.md** - Quick row count/NULL checks (basic verification)

**For understanding validation progression (MVP → Full Features):**
1. **MVP Path**: M1.0 → M1.1 → M1.2 (basic proof) → M1.3 (basic integrity) → M1.5 (per-table)
2. **Full Features**: M1.7 (comprehensive proof) → M1.8 (DMM replacement)
3. **UAT Pipeline**: M2.1 → M2.2 (specialized transformation layer)
4. **Integration**: M3.1 (manifest metadata) → M3.2 (load harness verification)

**🚀 RECOMMENDED IMPLEMENTATION ORDER (Deadline-Driven):**
1. **M1.0 MVP** (this week) - Bootstrap snapshot + PostDeployment template + observability → Fixes FK violations
2. **M1.1 MVP** (parallel with M1.0) - Export verification framework → Manifest/artifact validation
3. **M1.2 Basic Validation** (after M1.0) - Runtime ordering validation → Catches sorting bugs early
4. **M1.3 Basic Verification** (after M1.0+M1.2) - Row count/NULL checks → Quick sanity verification
5. **M1.5 Enhancement** (next sprint) - Per-table emission mode → Production version control
6. **M1.7 Full Observability** (when operators request) - Topological proof artifacts → Documentation/auditing
7. **M1.8 DMM Replacement** (when ready for production) - Comprehensive data integrity → Hash comparison, full validation
8. M2.1 (UAT verification - can parallel with M1.x)
9. M2.2 (transformation verification - depends on M1.8 + M2.1)
10. M3.1 (manifest extensions - depends on all above)
11. M3.2 (load harness verification - depends on all above)

---

*Generated: 2025-11-18*
*Context: Pre-implementation technical specifications for Release Candidate tasks*
