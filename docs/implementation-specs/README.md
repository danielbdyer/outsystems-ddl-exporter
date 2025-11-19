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

### 🚨 **CRITICAL PATH ANALYSIS**
- [M1.0-MVP-CRITICAL-PATH.md](./M1.0-MVP-CRITICAL-PATH.md) - **READ THIS FIRST**: Separates MVP from validation features

### Milestone 1: Export Artifact Verification

#### Core Functionality (Ship First)
- [M1.0-global-topological-ordering.md](./M1.0-global-topological-ordering.md) - **🔴 MVP**: Bootstrap snapshot and global FK ordering (1.5-2 days)
- [M1.1-export-verification-framework.md](./M1.1-export-verification-framework.md) - Export verification system (independent track)

#### Validation & Observability (Ship Later)
- [M1.2-topological-proof-generation.md](./M1.2-topological-proof-generation.md) - **🔵 M1.7**: Topological proof (deferred - observability)
- [M1.3-data-integrity-verification.md](./M1.3-data-integrity-verification.md) - **🔵 M1.8**: Data integrity (deferred - DMM replacement)
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
1. **M1.0-MVP-CRITICAL-PATH.md** - Understand what ships first vs. validation features
2. **M1.0-global-topological-ordering.md** - Bootstrap snapshot solution (1.5-2 days)
3. **M1.1-export-verification-framework.md** - Artifact verification (parallel track)

**For understanding verification architecture:**
1. M1.1 → M1.7 (M1.2) → M1.8 (M1.3) → M3.2 (base verification stack)
2. M2.1 → M2.2 (specialized verification layer)
3. M3.1 (integration metadata)

**🚀 RECOMMENDED IMPLEMENTATION ORDER (Deadline-Driven):**
1. **M1.0 MVP** (this week) - Bootstrap snapshot + PostDeployment template → Fixes FK violations
2. **M1.1** (parallel) - Export verification framework → Manifest/artifact validation
3. **M1.5** (next sprint) - Per-table emission mode → Production enhancement
4. **M1.6** (if requested) - Observability measures → Diagnostic aids
5. **M1.7** (when ready for CI/CD) - Topological proof → Automated verification
6. **M1.8** (DMM replacement) - Data integrity → High-confidence validation
7. M2.1 (UAT verification - can parallel with M1.x)
8. M2.2 (transformation verification - depends on M1.8 + M2.1)
9. M3.1 (manifest - depends on all above)
10. M3.2 (load harness - depends on all above)

---

*Generated: 2025-11-18*
*Context: Pre-implementation technical specifications for Release Candidate tasks*
