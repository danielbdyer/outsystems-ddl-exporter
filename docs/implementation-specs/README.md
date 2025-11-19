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
- [M1.0-global-topological-ordering.md](./M1.0-global-topological-ordering.md) - **🔴 MVP**: Bootstrap snapshot, global FK ordering, observability, and per-table emission
- [M1.1-export-verification-framework.md](./M1.1-export-verification-framework.md) - **🔴 MVP**: Export verification system (manifest/artifact validation)

#### Basic Validation (Ship After MVP)
- [M1.2-topological-proof-validation.md](./M1.2-topological-proof-validation.md) - **🟡 Basic**: Runtime ordering validation (fail-fast checks)
- [M1.3-data-integrity-checks.md](./M1.3-data-integrity-checks.md) - **🟡 Basic**: Row count and NULL checks (quick sanity verification)

#### Advanced Validation & Observability (Ship When Requested)
- [M1.7-topological-proof-generation.md](./M1.7-topological-proof-generation.md) - **🔵 Deferred**: Full topological proof artifacts (documentation/auditing)
- [M1.8-data-integrity-verification.md](./M1.8-data-integrity-verification.md) - **🔵 Deferred**: Comprehensive data integrity (DMM replacement)
  - Includes Appendix A: Research findings on checksum algorithms, NULL handling, and pipeline architecture
- [M1.4-verification-test-coverage.md](./M1.4-verification-test-coverage.md) - Test scenarios for verification systems

### Milestone 2: UAT-Users Transformation Guarantees

**Critical Path (Milestone Deadline)**:
- [M2.1-uat-users-verification-framework.md](./M2.1-uat-users-verification-framework.md) - **🔴 Critical Path**: UAT-users artifact verification framework (map completeness, FK catalog, SQL safety)
  - Includes "Codebase Integration Guide" with verification patterns, validation logic reuse, and CLI integration
- [M2.4-insert-transformation-implementation.md](./M2.4-insert-transformation-implementation.md) - **🔴 Critical Path**: INSERT transformation implementation (pre-transformed INSERT generation)
  - Like M1.0 (critical path implementation) - verification can be deferred
  - Refactors pipeline to run UAT-users before build, injects TransformationContext into DynamicEntityInsertGenerator
  - Enables full-export + uat-users integration for milestone deadline
  - **~3 weeks effort** (validated by codebase analysis)

**Verification (Deferred Verifiability)**:
- [M2.2-transformation-verification.md](./M2.2-transformation-verification.md) - **🟡 Verification (Deferred)**: Unified transformation verification for both INSERT and UPDATE modes
  - Like M1.1/M1.2 (verification) - proves M2.4 (critical path) works correctly
  - **Part A (Shared Fundamentals)**: Transformation map validation, FK catalog verification, core infrastructure (deliver FIRST)
  - **Part B (INSERT Verification)**: Parse pre-transformed INSERT scripts, verify orphan elimination, UAT inventory compliance
  - **Part C (UPDATE Verification)**: Parse UPDATE scripts, verify CASE blocks, WHERE guards
  - **Cross-Validation**: Compare INSERT vs UPDATE transformation counts, prove equivalence
  - **Modular Report**: Extends M1.3 base verification with `uatUsersVerification` section
  - Can be deferred after M2.4 ships (manual inspection sufficient for milestone)
- [M2.3-uat-users-integration-tests.md](./M2.3-uat-users-integration-tests.md) - **🟡 Verification (Deferred)**: Comprehensive integration tests (edge cases, idempotence, error handling)

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
1. **M1.0-global-topological-ordering.md** - Bootstrap snapshot solution with observability
   - **NEW**: Includes "Codebase Integration Guide" with exact file paths, integration points, and critical questions
2. **M1.1-export-verification-framework.md** - Artifact verification (parallel track)
   - **NEW**: Includes "Codebase Integration Guide" with DI setup, CLI integration, and implementation phases
3. **M1.2-topological-proof-validation.md** - Runtime ordering validation (basic safety)
4. **M1.3-data-integrity-checks.md** - Quick row count/NULL checks (basic verification)

**For understanding validation progression (MVP → Full Features):**
1. **MVP Path**: M1.0 (includes per-table emission) → M1.1 → M1.2 (basic proof) → M1.3 (basic integrity)
2. **Full Features**: M1.7 (comprehensive proof) → M1.8 (DMM replacement)
3. **UAT Pipeline** (Critical Path vs Verification):
   - **Critical Path**: M2.1 (artifact verification) → M2.4 (INSERT transformation implementation)
   - **Verification (Deferred)**: M2.2 Part A → Part B (INSERT verification) + Part C (UPDATE verification) → M2.3 (integration tests)
   - **Pattern**: Like M1.0 (implementation) vs M1.1/M1.2 (verification) - deferred verifiability for milestone deadline
   - **Structure**: M2.2 uses Part A → Part B or C approach, prioritizing shared essence before mode-specific implementations
4. **Integration**: M3.1 (manifest metadata) → M3.2 (load harness verification)

**🚀 RECOMMENDED IMPLEMENTATION ORDER:**
1. **M1.0 MVP** - Bootstrap snapshot + PostDeployment + observability + per-table emission → Fixes FK violations, provides fine-grain version control
2. **M1.1 MVP** (parallel with M1.0) - Export verification framework → Manifest/artifact validation
3. **M1.2 Basic Validation** (after M1.0) - Runtime ordering validation → Catches sorting bugs early
4. **M1.3 Basic Verification** (after M1.0+M1.2) - Row count/NULL checks → Quick sanity verification
5. **M1.7 Full Observability** (when operators request) - Topological proof artifacts → Documentation/auditing
6. **M1.8 DMM Replacement** (when ready for production) - Comprehensive data integrity → Hash comparison, full validation
7. **M2.1 UAT-Users Verification** (can parallel with M1.x) - CRITICAL PATH → Artifact verification framework, CI/CD gates
8. **M2.4 INSERT Transformation** (after M2.1) - CRITICAL PATH (~3 weeks) → Pre-transformed INSERT generation, full-export integration
9. **M2.2 Part A** (after M2.4 ships) - DEFERRED VERIFICATION → Shared fundamentals, transformation map validation
10. **M2.2 Part B** (after Part A) - DEFERRED VERIFICATION → INSERT verification, orphan elimination proof
11. **M2.2 Part C** (after Part A) - DEFERRED VERIFICATION → UPDATE verification, CASE block validation
12. **M2.3 Integration Tests** (after M2.2) - DEFERRED VERIFICATION → Edge cases, idempotence, error handling
13. **M3.1 Manifest Extensions** (after M2.4) - UAT-users metadata in manifest → Enables automation
14. **M3.2 Load Harness Verification** (after all above) - End-to-end ETL verification → Full confidence in UAT deployments

---

*Generated: 2025-11-19*
*Updated with M2.* UAT-Users Transformation Verification specs*
*Context: Pre-implementation technical specifications for Release Candidate tasks*
