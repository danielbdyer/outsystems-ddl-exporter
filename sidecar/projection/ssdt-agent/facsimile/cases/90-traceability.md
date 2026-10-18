# Traceability

*Generated from the catalog by `../tests/catalog_lint.py --matrix` (the prototype of `tests/run-cases.ps1 --lint`, which runs in CI as a step of the ssdt-agent gates), then annotated by hand with the audit record. Regenerate the matrices whenever a case changes; never edit them by hand.*

## State at G0 (2026-10-18)

- **1123 cases** in 12 families, **0 grammar errors, 0 coverage gaps**: every law has two or more cases, every operation its standard situations (or a stated `n/a`), every question kind a corpus case, every stop kind a case, and every one of the Twin's 36 responsibilities a parity or retirement case.
- **By family:** `00-invariants.md` 68 · `10-questions.md` 111 · `20-planter.md` 126 · `30-outcomes.md` 139 · `40-ops-columns.md` 99 · `41-ops-keys.md` 94 · `42-ops-tables.md` 111 · `43-compound.md` 24 · `50-environments.md` 72 · `60-privacy.md` 41 · `70-process.md` 115 · `80-twin-parity.md` 123.
- **By kind:** corpus 410 · pure 290 · process 238 · sql 174 · manual 11. Corpus cases are differential: the facsimile's verdict must equal the verdict of publishing onto a full copy of the stand-in environment (F3).
- **By source:** receipt 244 · expected 750 · verify 129. A receipt cites a recorded live proof; `expected` is documented engine behaviour not yet witnessed here; `verify` is genuinely open and is settled by the first run (list below).

## How the catalog was made

1. **The spine first.** `../ARCHITECTURE.md`, `../INVARIANTS.md` and `README.md` (the grammar) were written before any case, from a survey of every Twin responsibility, test and caller.
2. **Eight drafters, one family each,** wrote about 1,050 cases in parallel against the same grammar, schema (`pg`) and laws. Each reported where the spine was wrong or silent, and the findings agreed across families.
3. **The spine was patched once** with those findings: three statement classes and four new question kinds; coordinate resolution for objects created or renamed in the same delta; the predicate exception; remedies by predicate; merge-base reference data; linked orphan witnesses; trust-state reproduction; max-not-sum planting; the observed phase order (validation after post-deploy); `blocked authored`; eight stops; the VERDICT/STOP/DONE grammar; `down` keeping `fx_local`; the committed schema; D6 Silence; closed residual tokens; and tightened privacy laws.
4. **Three auditors** reconciled every case against the patched spine, closed every coverage gap, and checked every receipt: every cited Twin test name and every cited finding match their sources. They downgraded 14 receipts they could not substantiate, and upgraded 9 expectations that a sample PR's live record proves. Their remaining spine findings were folded in.

## Law → cases

| Law | Cases | IDs |
|---|---:|---|
| F1 | 251 | CLI-20 CLI-23 CMP-01 CMP-03 CMP-04 CMP-05 CMP-06 CMP-07 CMP-11 CMP-13 CMP-14 CMP-16 CMP-17 CMP-18 CMP-19 CMP-20 CMP-22 ENV-01 ENV-02 ENV-04 ENV-26 ENV-27 ENV-30 ENV-38 ENV-60 ENV-62 ENV-63 ENV-64 INV-M2-02 INV-S2-02 OP-add-check-01 OP-add-check-02 OP-add-check-03 OP-add-check-04 OP-add-check-07 OP-add-index-03 OP-add-mandatory-04 OP-add-optional-03 OP-add-unique-01 OP-add-unique-02 … |
| F2 | 39 | CLI-24 ENV-42 ENV-45 ENV-47 INV-D2-02 OP-add-check-05 OP-add-default-04 OP-add-unique-08 OP-create-fk-orphan-04 OP-define-pk-08 OP-delete-entity-04 OP-junction-02 OP-modify-index-09 OP-toggle-trust-01 OP-toggle-trust-02 OP-toggle-trust-03 OP-toggle-trust-09 PLT-100 PLT-101 PLT-36 PLT-37 PLT-39 PLT-45 PLT-46 PLT-47 PLT-85 PLT-87 PLT-88 PLT-89 PLT-90 PLT-99 TWN-116 TWN-120 TWN-125 TWN-126 TWN-147 TWN-252 TWN-255 TWN-308 |
| F3 | 283 | CI-02 CI-03 CI-04 CMP-02 CMP-07 CMP-20 ENV-01 ENV-02 ENV-05 ENV-11 ENV-22 ENV-23 ENV-24 ENV-26 ENV-27 ENV-30 ENV-31 ENV-37 ENV-38 ENV-39 ENV-45 ENV-46 ENV-50 ENV-51 ENV-60 INV-F8-01 OP-add-check-01 OP-add-check-02 OP-add-check-03 OP-add-check-04 OP-add-check-05 OP-add-check-06 OP-add-default-01 OP-add-default-02 OP-add-default-04 OP-add-index-01 OP-add-index-02 OP-add-index-03 OP-add-index-04 OP-add-index-06 … |
| F4 | 178 | CI-08 CMP-01 CMP-02 CMP-09 CMP-16 CMP-22 INV-E1-02 OP-add-check-01 OP-add-default-03 OP-add-index-01 OP-add-index-02 OP-add-index-05 OP-add-mandatory-02 OP-add-mandatory-03 OP-add-mandatory-08 OP-add-optional-03 OP-archive-entity-01 OP-audit-columns-03 OP-change-delete-rule-01 OP-create-entity-01 OP-create-entity-02 OP-create-entity-03 OP-create-fk-clean-01 OP-create-fk-clean-04 OP-create-fk-orphan-08 OP-create-static-seed-04 OP-create-static-seed-06 OP-define-pk-01 OP-define-pk-07 OP-define-pk-09 OP-delete-attribute-03 OP-drop-check-01 OP-drop-check-02 OP-drop-fk-01 OP-drop-fk-02 OP-drop-fk-05 OP-drop-index-01 OP-drop-index-02 OP-drop-index-04 OP-drop-pk-01 … |
| F5 | 12 | ENV-10 ENV-45 ENV-47 ENV-65 ENV-67 INV-F5-01 INV-F5-02 OP-make-optional-04 OP-rename-entity-08 OP-toggle-trust-03 OP-widen-06 QST-03 |
| F6 | 51 | CFG-12 CFG-13 CFG-14 CFG-15 CLI-14 CLI-15 CLS-23 CLS-55 CMP-06 ENV-22 ENV-23 ENV-42 INV-F6-01 INV-F6-02 INV-S1-02 OP-add-mandatory-07 OP-add-optional-03 OP-create-fk-orphan-06 OP-create-fk-orphan-08 OP-delete-entity-01 OP-delete-entity-02 OP-delete-entity-03 OP-delete-entity-07 OP-drop-fk-05 OP-drop-index-04 OP-drop-pk-04 OP-drop-unique-04 OP-make-mandatory-03 OP-merge-tables-06 OP-modify-index-04 OP-modify-index-10 OP-move-schema-01 OP-move-schema-02 OP-narrow-03 OP-narrow-04 OP-narrow-05 OP-narrow-06 OP-rebuild-index-03 OP-rename-entity-02 OP-rename-entity-03 … |
| F7 | 74 | CFG-15 CLI-11 CLI-12 CLI-44 CMP-18 CMP-19 CMP-20 CMP-21 ENV-11 ENV-18 ENV-20 ENV-22 ENV-24 ENV-25 ENV-26 ENV-27 ENV-28 ENV-29 ENV-30 ENV-31 ENV-34 ENV-35 ENV-37 ENV-39 ENV-46 ENV-54 ENV-55 ENV-57 ENV-59 ENV-61 ENV-72 INV-H7-02 INV-S5-02 OP-create-entity-06 OP-delete-entity-07 OP-delete-seed-value-06 OP-drop-check-04 OP-drop-fk-04 OP-drop-index-03 OP-drop-pk-04 … |
| F8 | 30 | CMP-11 CMP-13 CMP-14 CMP-15 CMP-17 ENV-31 ENV-33 ENV-35 ENV-36 INV-D6-02 INV-F8-01 INV-F8-02 OP-add-mandatory-05 OP-add-mandatory-06 OP-delete-attribute-04 OP-delete-attribute-05 OP-delete-entity-05 OP-make-mandatory-04 OP-make-mandatory-05 OP-move-attribute-08 OP-narrow-07 OP-narrow-08 OP-split-table-08 OP-temporal-convert-03 OP-temporal-convert-04 QST-82 QST-83 TWN-243 TWN-245 TWN-247 |
| F9 | 53 | CLS-61 ENV-38 ENV-39 ENV-40 ENV-41 ENV-42 ENV-45 ENV-46 ENV-47 OP-add-default-04 OP-add-unique-04 OP-add-unique-05 OP-add-unique-10 OP-change-delete-rule-03 OP-define-pk-04 OP-define-pk-05 OP-extract-to-lookup-04 OP-identity-swap-06 OP-modify-default-03 OP-modify-index-05 OP-modify-index-06 OP-retype-implicit-04 OP-temporal-convert-04 OP-toggle-trust-01 OP-toggle-trust-02 OP-toggle-trust-03 OP-toggle-trust-04 OP-toggle-trust-05 OP-toggle-trust-06 OP-toggle-trust-08 OP-toggle-trust-09 PLT-101 PLT-37 PLT-43 PLT-45 PLT-59 PLT-70 PLT-85 PLT-87 PLT-88 … |
| H1 | 58 | CFG-16 CLI-01 CLI-03 CLI-06 CLI-09 CLI-10 CLI-12 CLI-32 CLI-35 CLI-36 CLI-37 CLI-43 CLI-44 CLS-01 CLS-02 CLS-04 CLS-06 CLS-32 CLS-36 CLS-38 CLS-50 CLS-51 CLS-55 ENV-06 ENV-13 ENV-15 ENV-16 ENV-17 ENV-19 ENV-71 INV-D6-03 INV-H1-01 INV-H1-02 INV-H1-03 OP-drop-pk-03 PLT-116 PLT-118 PLT-120 PLT-48 PRV-20 … |
| H2 | 117 | CLI-16 CLI-21 CLI-36 CLI-43 CLS-01 CLS-02 CLS-03 CLS-09 CLS-10 CLS-32 CLS-50 CLS-51 CLS-54 CLS-65 CMP-17 ENV-03 ENV-04 ENV-05 ENV-09 ENV-10 ENV-18 ENV-20 ENV-24 ENV-40 ENV-48 ENV-49 ENV-50 ENV-51 ENV-52 ENV-61 ENV-72 INV-D2-03 INV-D6-02 INV-F8-02 INV-H1-03 OP-add-check-04 OP-add-check-05 OP-add-check-06 OP-add-check-07 OP-add-default-01 … |
| H3 | 131 | CFG-03 CFG-05 CFG-06 CFG-08 CFG-10 CFG-11 CFG-12 CLI-02 CLI-03 CLI-06 CLI-08 CLI-15 CLI-16 CLI-17 CLI-18 CLI-19 CLI-20 CLI-21 CLI-22 CLI-23 CLI-24 CLI-26 CLI-31 CLI-38 CLI-39 CLI-40 CLI-41 CLI-42 CLS-35 CLS-37 CLS-44 CLS-45 CLS-46 CLS-47 CLS-48 CLS-49 CLS-66 CMP-12 CMP-22 ENV-03 … |
| H4 | 99 | CI-11 CLI-11 CLI-13 CLS-49 CMP-24 ENV-05 ENV-22 ENV-25 ENV-35 ENV-36 ENV-40 ENV-41 ENV-43 ENV-44 ENV-48 ENV-49 ENV-53 ENV-57 ENV-59 ENV-61 ENV-63 INV-E2-02 OP-add-check-02 OP-add-index-02 OP-add-index-04 OP-add-index-05 OP-add-index-06 OP-add-mandatory-08 OP-add-mandatory-09 OP-add-optional-01 OP-archive-entity-02 OP-archive-entity-08 OP-change-delete-rule-02 OP-change-delete-rule-04 OP-create-fk-clean-05 OP-create-fk-orphan-08 OP-drop-check-02 OP-drop-check-04 OP-drop-fk-02 OP-drop-fk-04 … |
| H5 | 151 | CLS-03 CLS-04 CLS-05 CLS-06 CLS-07 CLS-08 CLS-09 CLS-10 CLS-11 CLS-12 CLS-13 CLS-14 CLS-15 CLS-16 CLS-17 CLS-18 CLS-19 CLS-20 CLS-21 CLS-22 CLS-23 CLS-24 CLS-25 CLS-26 CLS-27 CLS-28 CLS-29 CLS-30 CLS-31 CLS-33 CLS-34 CLS-35 CLS-39 CLS-40 CLS-41 CLS-42 CLS-43 CLS-52 CLS-53 CLS-56 … |
| H6 | 71 | CLI-19 CLS-14 CLS-63 CMP-03 CMP-04 CMP-05 CMP-06 CMP-08 CMP-09 CMP-18 CMP-24 INV-H6-01 OP-add-check-04 OP-add-unique-03 OP-archive-entity-06 OP-audit-columns-05 OP-change-delete-rule-03 OP-create-fk-orphan-01 OP-create-fk-orphan-02 OP-create-fk-orphan-03 OP-create-fk-orphan-05 OP-create-fk-orphan-06 OP-create-static-seed-03 OP-create-static-seed-06 OP-define-pk-03 OP-delete-attribute-06 OP-delete-entity-02 OP-delete-entity-06 OP-drop-check-03 OP-drop-default-04 OP-drop-fk-03 OP-extract-to-lookup-02 OP-extract-to-lookup-05 OP-identity-swap-04 OP-junction-03 OP-junction-05 OP-junction-06 OP-make-mandatory-06 OP-make-mandatory-07 OP-merge-tables-02 … |
| H7 | 31 | CFG-15 CI-03 CI-07 CI-13 ENV-01 ENV-11 ENV-43 ENV-53 ENV-54 ENV-55 ENV-56 ENV-59 INV-D5-03 INV-E2-01 INV-E2-02 INV-E3-02 INV-H7-01 INV-H7-02 OP-add-mandatory-09 OP-make-mandatory-08 OP-temporal-new-02 PRV-06 PRV-15 REC-25 REC-26 REC-27 TLC-02 TLC-03 TWN-110 TWN-128 TWN-308 |
| P1 | 30 | CLI-04 CLI-39 ENV-02 ENV-60 PLT-124 PRV-01 PRV-02 PRV-03 PRV-04 PRV-05 PRV-06 PRV-19 PRV-20 PRV-26 PRV-27 PRV-30 PRV-32 PRV-34 PRV-35 PRV-38 PRV-39 PRV-40 TLC-13 TWN-106 TWN-121 TWN-122 TWN-146 TWN-254 TWN-310 TWN-311 |
| P2 | 25 | CLI-26 CLI-42 ENV-07 ENV-19 ENV-21 ENV-56 INV-M3-01 PRV-07 PRV-08 PRV-09 PRV-10 PRV-11 PRV-12 PRV-13 PRV-14 PRV-15 PRV-16 PRV-17 PRV-18 PRV-24 PRV-41 QST-106 QST-98 TWN-113 TWN-115 |
| P3 | 16 | CLI-38 ENV-06 ENV-08 ENV-09 ENV-71 OP-add-check-06 PLT-119 PRV-02 PRV-16 PRV-17 PRV-18 PRV-19 PRV-20 PRV-21 PRV-40 QST-106 |
| P4 | 25 | CLI-05 CLI-08 CLS-47 ENV-01 ENV-02 ENV-03 ENV-04 ENV-05 ENV-07 ENV-12 ENV-58 ENV-60 ENV-62 ENV-65 ENV-66 ENV-67 ENV-68 INV-M3-02 INV-S1-02 PRV-09 PRV-14 PRV-22 PRV-23 PRV-24 PRV-37 |
| P5 | 43 | CLI-41 OP-add-check-06 OP-add-index-03 OP-add-unique-04 OP-add-unique-05 OP-add-unique-06 OP-define-pk-04 OP-define-pk-05 OP-define-pk-06 OP-edit-seed-03 OP-extract-to-lookup-02 OP-extract-to-lookup-04 OP-modify-index-05 OP-modify-index-06 OP-modify-index-07 OP-narrow-04 OP-retype-explicit-09 PLT-12 PLT-124 PLT-14 PLT-16 PLT-17 PLT-19 PLT-20 PLT-23 PLT-61 PLT-64 PLT-65 PLT-67 PLT-71 PLT-84 PLT-95 PRV-01 PRV-04 PRV-25 PRV-26 PRV-27 TWN-118 TWN-122 TWN-254 … |
| P6 | 25 | CLI-25 ENV-07 ENV-21 OP-extract-to-lookup-01 OP-extract-to-lookup-04 OP-extract-to-lookup-06 OP-move-attribute-07 OP-split-table-04 PRV-13 PRV-16 PRV-28 PRV-29 PRV-30 PRV-31 PRV-32 PRV-33 PRV-34 PRV-35 PRV-36 PRV-37 PRV-38 TWN-146 TWN-242 TWN-243 TWN-310 |
| S1 | 12 | CFG-14 CLI-28 CLI-34 INV-S1-01 INV-S1-02 INV-S6-01 INV-S6-03 PLT-108 PRV-14 PRV-22 TWN-101 TWN-301 |
| S2 | 5 | INV-S2-01 INV-S2-02 PLT-113 PLT-115 TWN-112 |
| S3 | 37 | CLI-06 CLI-18 CMP-16 ENV-10 ENV-18 ENV-19 ENV-20 ENV-21 ENV-37 OP-create-entity-02 OP-create-entity-03 OP-create-static-seed-04 OP-create-static-seed-06 OP-junction-01 OP-move-schema-07 OP-rename-entity-05 PRV-08 PRV-10 PRV-12 PRV-13 QST-100 QST-101 QST-102 QST-103 QST-104 QST-105 QST-45 QST-46 QST-58 QST-59 QST-62 TIER-08 TWN-113 TWN-114 TWN-115 TWN-213 TWN-227 |
| S4 | 13 | CI-02 CLS-35 CLS-48 CLS-61 ENV-43 INV-F6-01 OP-temporal-new-02 REC-07 TIER-21 TLC-04 TLC-05 TWN-135 TWN-314 |
| S5 | 11 | CLI-14 CLI-34 CLI-35 CLI-37 INV-S2-01 INV-S5-01 INV-S5-02 PLT-102 PLT-115 TWN-112 TWN-303 |
| S6 | 14 | CLI-28 CLI-29 CLI-36 CLI-37 CLI-43 INV-H1-02 INV-S6-01 INV-S6-02 INV-S6-03 TLC-11 TLC-12 TWN-305 TWN-306 UP-14 |
| D1 | 100 | CFG-02 CI-05 CLI-34 CLS-52 CMP-21 ENV-28 ENV-29 ENV-54 ENV-69 INV-D1-01 INV-D1-02 INV-D1-03 INV-D3-02 INV-D4-02 INV-D6-01 INV-H7-02 OP-audit-columns-03 PLT-06 PLT-07 PLT-09 PLT-102 PLT-103 PLT-104 PLT-109 PLT-11 PLT-112 PLT-123 PLT-19 PLT-29 PLT-32 PLT-33 PLT-34 PLT-41 PLT-75 PLT-76 PLT-83 PLT-86 QST-02 QST-03 QST-04 … |
| D2 | 17 | CLI-29 ENV-54 ENV-69 ENV-70 INV-D2-01 INV-D2-02 INV-D2-03 INV-S5-01 PLT-102 TLC-10 TWN-129 TWN-141 TWN-147 TWN-305 TWN-306 TWN-309 UP-11 |
| D3 | 41 | INV-D3-01 INV-D3-02 PLT-01 PLT-02 PLT-03 PLT-04 PLT-05 PLT-06 PLT-07 PLT-08 PLT-09 PLT-10 PLT-103 PLT-104 PLT-105 PLT-11 PLT-113 PLT-12 PLT-13 PLT-14 PLT-15 PLT-16 PLT-17 PLT-18 PLT-19 PLT-20 PLT-22 PLT-23 PLT-24 PLT-25 PLT-27 PLT-32 PLT-40 PLT-61 PLT-83 PLT-95 PRV-25 TWN-118 TWN-127 UP-06 … |
| D4 | 24 | INV-D4-01 INV-D4-02 INV-T3-01 PLT-112 PLT-113 TWN-107 TWN-111 TWN-148 TWN-240 TWN-241 TWN-242 TWN-248 TWN-301 TWN-302 TWN-317 UP-01 UP-02 UP-03 UP-04 UP-05 UP-09 UP-10 UP-11 UP-14 |
| D5 | 10 | CLI-35 INV-D5-01 INV-D5-02 INV-D5-03 TLC-07 TWN-107 TWN-111 TWN-129 TWN-150 TWN-309 |
| D6 | 192 | CLI-09 CMP-01 CMP-02 CMP-07 CMP-11 CMP-13 CMP-14 CMP-15 CMP-17 CMP-18 CMP-19 CMP-20 CMP-22 INV-D6-01 INV-D6-02 INV-D6-03 INV-F8-02 OP-add-check-01 OP-add-check-02 OP-add-check-03 OP-add-check-05 OP-add-check-07 OP-add-default-01 OP-add-default-02 OP-add-default-04 OP-add-default-06 OP-add-index-01 OP-add-index-02 OP-add-index-04 OP-add-index-06 OP-add-mandatory-01 OP-add-mandatory-02 OP-add-mandatory-06 OP-add-mandatory-07 OP-add-optional-01 OP-add-optional-02 OP-add-optional-03 OP-add-unique-01 OP-add-unique-02 OP-add-unique-08 … |
| M1 | 29 | CI-01 CI-16 CI-18 CLS-58 INV-E1-01 INV-M1-01 INV-M1-02 INV-M4-02 PLT-92 QST-01 QST-02 QST-16 QST-17 QST-19 QST-22 QST-33 QST-35 QST-41 QST-47 QST-50 QST-60 QST-63 QST-66 QST-76 QST-95 TIER-01 TIER-02 TIER-03 TIER-04 |
| M2 | 75 | CLI-10 ENV-28 ENV-30 INV-M2-01 INV-M2-02 OP-add-index-03 OP-add-optional-02 OP-add-unique-03 OP-add-unique-07 OP-archive-entity-02 OP-create-fk-clean-02 OP-create-fk-clean-03 OP-create-fk-clean-04 OP-create-fk-orphan-01 OP-create-fk-orphan-07 OP-define-pk-03 OP-delete-entity-03 OP-delete-entity-04 OP-delete-seed-value-01 OP-extract-to-lookup-08 OP-identity-swap-02 OP-junction-02 OP-make-mandatory-03 OP-merge-tables-01 OP-merge-tables-02 OP-merge-tables-03 OP-move-attribute-01 OP-move-attribute-02 OP-move-attribute-03 OP-split-table-01 OP-split-table-02 OP-widen-02 PLT-03 PLT-104 PLT-106 PLT-107 PLT-122 PLT-125 PLT-14 PLT-35 … |
| M3 | 26 | CI-05 CLI-04 CLI-05 CLI-07 CLI-09 CLI-13 CLI-27 ENV-06 ENV-15 ENV-16 ENV-17 ENV-53 ENV-58 ENV-65 ENV-66 ENV-67 ENV-68 ENV-69 ENV-70 INV-D2-03 INV-M3-01 INV-M3-02 PRV-24 PRV-37 TWN-310 UP-12 |
| M4 | 6 | CI-01 CI-15 INV-M4-01 INV-M4-02 TWN-144 TWN-149 |
| E1 | 22 | CI-16 CI-17 CI-18 CLS-40 CLS-41 CLS-42 CLS-54 CLS-56 CLS-57 CLS-65 CMP-10 INV-E1-01 INV-E1-02 INV-M1-01 OP-delete-entity-06 OP-move-schema-04 PRV-16 QST-01 QST-15 QST-93 QST-95 TWN-134 |
| E2 | 10 | CI-01 CI-13 INV-D5-03 INV-E2-01 INV-E2-02 INV-H6-01 REC-25 REC-27 TLC-03 TWN-140 |
| E3 | 23 | CI-05 CI-06 CI-07 CI-08 CI-09 CI-10 CI-11 CLI-04 CLI-17 CLI-39 ENV-52 ENV-55 ENV-56 ENV-70 INV-E3-01 INV-E3-02 PRV-05 PRV-15 PRV-39 TWN-142 TWN-143 TWN-304 TWN-312 |
| E4 | 28 | CFG-01 CFG-02 CFG-03 CFG-04 CFG-05 CFG-06 CFG-07 CFG-08 CFG-09 CFG-10 CFG-11 CFG-15 CFG-16 CFG-17 CLI-30 CLI-31 INV-E4-01 INV-E4-02 INV-E4-03 PRV-06 TWN-103 TWN-104 TWN-105 TWN-106 TWN-139 TWN-251 TWN-307 TWN-311 |
| A1 | 37 | CLI-01 CLI-02 CLI-04 CLI-09 CLI-10 CLI-11 CLI-12 CLI-13 CLI-14 CLI-15 CLI-25 CLI-27 CLI-32 CLI-33 CLI-44 CMP-12 CMP-21 CMP-24 ENV-13 INV-A1-01 INV-A1-02 INV-A3-02 INV-H1-01 OP-modify-default-03 PLT-108 PLT-116 PRV-01 PRV-21 PRV-38 PRV-40 TLC-15 TLC-20 TWN-101 TWN-301 TWN-302 TWN-304 TWN-316 |
| A2 | 11 | CI-01 CI-14 INV-A2-01 INV-A2-02 TLC-17 TLC-18 TLC-19 TLC-20 TWN-144 TWN-149 TWN-314 |
| A3 | 87 | CFG-12 CFG-13 CI-02 CI-10 CLI-01 CLI-02 CLI-06 CLI-07 CLI-08 CLI-09 CLI-10 CLI-15 CLI-16 CLI-26 CLI-31 CLI-32 CLI-38 CLI-39 CLI-40 CLI-42 CLI-43 CLS-01 CLS-03 CLS-04 CLS-12 CLS-14 CLS-18 CLS-27 CLS-36 CLS-37 CLS-44 CLS-45 CLS-46 CLS-47 CLS-66 CMP-12 ENV-03 ENV-06 ENV-08 ENV-13 … |
| A4 | 3 | INV-A4-01 INV-A4-02 TLC-21 |
| A5 | 101 | CFG-07 CLI-03 CLI-17 CLI-18 CLI-19 CLI-20 CLI-21 CLI-22 CLI-23 CLI-24 CLI-37 CLI-38 CLI-39 CLI-40 CLI-41 CMP-01 CMP-07 CMP-08 CMP-10 CMP-11 CMP-13 CMP-14 CMP-15 CMP-17 CMP-22 ENV-08 ENV-14 ENV-32 ENV-33 ENV-52 INV-A5-01 INV-A5-02 OP-add-check-04 OP-add-check-05 OP-add-check-06 OP-add-check-07 OP-add-default-06 OP-add-mandatory-06 OP-add-unique-08 OP-archive-entity-02 … |
| T1 | 57 | CI-16 PLT-110 PLT-39 TWN-101 TWN-102 TWN-103 TWN-104 TWN-105 TWN-106 TWN-107 TWN-108 TWN-109 TWN-110 TWN-111 TWN-112 TWN-113 TWN-114 TWN-115 TWN-116 TWN-117 TWN-118 TWN-119 TWN-120 TWN-121 TWN-122 TWN-123 TWN-124 TWN-125 TWN-126 TWN-127 TWN-128 TWN-129 TWN-130 TWN-131 TWN-132 TWN-133 TWN-134 TWN-135 TWN-136 TWN-137 … |
| T2 | 118 | CMP-02 CMP-04 OP-add-check-02 OP-add-check-04 OP-add-index-02 OP-add-mandatory-01 OP-add-mandatory-02 OP-add-mandatory-04 OP-add-optional-02 OP-add-unique-02 OP-add-unique-03 OP-archive-entity-02 OP-audit-columns-03 OP-change-delete-rule-02 OP-create-entity-02 OP-create-fk-clean-02 OP-create-fk-orphan-01 OP-create-static-seed-01 OP-define-pk-02 OP-delete-attribute-02 OP-delete-entity-01 OP-delete-seed-value-02 OP-delete-seed-value-03 OP-drop-check-02 OP-drop-check-03 OP-drop-default-02 OP-drop-fk-02 OP-drop-fk-03 OP-drop-index-02 OP-drop-pk-02 OP-drop-pk-03 OP-drop-unique-02 OP-drop-unique-03 OP-edit-seed-02 OP-extract-to-lookup-01 OP-identity-swap-06 OP-junction-01 OP-make-mandatory-01 OP-make-mandatory-02 OP-make-optional-02 … |
| T3 | 49 | CLI-27 CLI-28 CLI-29 CLI-30 INV-D4-01 INV-S6-03 INV-T3-01 INV-T3-02 PLT-108 PLT-109 PLT-110 PLT-111 PLT-112 TWN-130 TWN-139 TWN-148 TWN-150 TWN-248 TWN-249 TWN-250 TWN-255 TWN-301 TWN-302 TWN-303 TWN-304 TWN-305 TWN-306 TWN-307 TWN-308 TWN-309 TWN-310 TWN-311 TWN-314 TWN-315 TWN-316 TWN-317 UP-01 UP-02 UP-03 UP-04 … |
| T4 | 8 | CI-12 INV-T4-01 INV-T4-02 TWN-140 TWN-312 TWN-313 TWN-314 TWN-315 |

## Operation → cases (standard set)

| Operation | Cases | Tags present |
|---|---:|---|
| add-check | 7 | clean edge empty remedy violating |
| add-default | 6 | clean edge empty remedy violating |
| add-index | 6 | clean edge empty remedy violating |
| add-mandatory | 9 | clean edge empty remedy violating |
| add-optional | 4 | clean edge empty refuse remedy violating |
| add-unique | 10 | clean edge empty remedy violating |
| archive-entity | 10 | clean edge empty remedy violating |
| audit-columns | 5 | clean edge empty remedy violating |
| change-delete-rule | 5 | clean edge empty remedy violating |
| create-entity | 6 | clean edge empty remedy violating |
| create-fk-clean | 5 | clean edge empty remedy violating |
| create-fk-orphan | 8 | clean edge empty remedy violating |
| create-static-seed | 7 | clean edge empty remedy violating |
| define-pk | 9 | clean edge empty remedy violating |
| delete-attribute | 8 | clean edge empty refuse remedy violating |
| delete-entity | 7 | clean edge empty remedy violating |
| delete-seed-value | 7 | clean edge empty remedy violating |
| drop-check | 4 | clean edge empty remedy violating |
| drop-default | 5 | clean edge empty remedy violating |
| drop-fk | 5 | clean edge empty remedy violating |
| drop-index | 4 | clean edge empty remedy violating |
| drop-pk | 5 | clean edge empty refuse remedy violating |
| drop-unique | 4 | clean edge empty remedy violating |
| edit-seed | 8 | clean edge empty remedy violating |
| extract-to-lookup | 10 | clean edge empty remedy violating |
| identity-swap | 7 | clean edge empty remedy violating |
| junction | 6 | clean edge empty remedy violating |
| make-mandatory | 9 | clean edge empty remedy violating |
| make-optional | 4 | clean edge empty remedy violating |
| merge-tables | 8 | clean edge empty remedy violating |
| modify-default | 4 | clean edge empty remedy violating |
| modify-index | 10 | clean edge empty remedy violating |
| move-attribute | 8 | clean edge empty remedy violating |
| move-schema | 7 | clean edge empty remedy violating |
| narrow | 10 | clean edge empty remedy violating |
| rebuild-index | 3 | clean edge empty refuse remedy violating |
| rename-attribute | 8 | clean edge empty refuse remedy violating |
| rename-entity | 8 | clean edge empty remedy violating |
| retype-explicit | 9 | clean edge empty remedy violating |
| retype-implicit | 5 | clean edge empty remedy violating |
| split-table | 8 | clean edge empty remedy violating |
| temporal-convert | 7 | clean edge empty remedy violating |
| temporal-new | 4 | clean edge empty remedy violating |
| toggle-trust | 9 | clean edge empty remedy violating |
| widen | 6 | clean edge empty remedy violating |

## Question kind → corpus cases

| Kind | Corpus cases |
|---|---|
| HasRows | 239: CMP-03 CMP-04 CMP-11 CMP-13 CMP-14 CMP-16 CMP-17 CMP-18 CMP-19 CMP-20 CMP-21 ENV-01 ENV-02 ENV-05 ENV-11 ENV-23 ENV-24 ENV-26 ENV-27 ENV-31 ENV-35 ENV-37 ENV-38 ENV-59 ENV-60 … |
| NotNull | 23: CMP-03 CMP-04 CMP-14 CMP-16 CMP-18 CMP-19 CMP-21 ENV-01 ENV-05 ENV-31 ENV-35 ENV-37 OP-define-pk-07 OP-make-mandatory-01 OP-make-mandatory-02 OP-make-mandatory-03 OP-make-mandatory-05 OP-make-mandatory-06 OP-make-mandatory-07 OP-make-mandatory-08 TWN-201 TWN-218 TWN-253 |
| Fits | 30: CMP-06 CMP-13 ENV-26 OP-move-schema-07 OP-narrow-01 OP-narrow-02 OP-narrow-03 OP-narrow-04 OP-narrow-05 OP-narrow-06 OP-narrow-08 OP-narrow-09 OP-rename-entity-05 OP-retype-explicit-01 OP-retype-explicit-02 OP-retype-explicit-03 OP-retype-explicit-04 OP-retype-explicit-05 OP-retype-explicit-06 OP-retype-explicit-09 OP-temporal-new-04 OP-widen-06 PLT-94 TWN-151 TWN-203 … |
| Unique | 44: CMP-06 CMP-20 ENV-11 ENV-27 ENV-38 ENV-39 ENV-59 OP-add-unique-01 OP-add-unique-02 OP-add-unique-03 OP-add-unique-04 OP-add-unique-05 OP-add-unique-06 OP-add-unique-07 OP-add-unique-08 OP-add-unique-09 OP-add-unique-10 OP-create-entity-03 OP-define-pk-01 OP-define-pk-02 OP-define-pk-03 OP-define-pk-04 OP-define-pk-05 OP-define-pk-06 OP-define-pk-07 … |
| Orphan | 41: CMP-01 CMP-05 CMP-07 CMP-17 CMP-22 OP-change-delete-rule-01 OP-change-delete-rule-02 OP-change-delete-rule-03 OP-change-delete-rule-04 OP-create-entity-02 OP-create-entity-06 OP-create-fk-clean-01 OP-create-fk-clean-02 OP-create-fk-clean-03 OP-create-fk-clean-04 OP-create-fk-orphan-01 OP-create-fk-orphan-02 OP-create-fk-orphan-03 OP-create-fk-orphan-04 OP-create-fk-orphan-05 OP-create-fk-orphan-06 OP-create-fk-orphan-07 OP-create-static-seed-04 OP-create-static-seed-06 OP-create-static-seed-07 … |
| CheckFalse | 12: OP-add-check-01 OP-add-check-02 OP-add-check-03 OP-add-check-04 OP-add-check-05 OP-add-check-06 OP-add-check-07 OP-drop-check-03 OP-toggle-trust-06 PLT-79 TWN-206 TWN-220 |
| KeyLength | 6: OP-add-index-03 OP-add-index-04 OP-widen-04 PLT-80 PLT-81 TWN-209 |
| Rebuild | 9: OP-add-optional-03 OP-identity-swap-01 OP-identity-swap-02 OP-identity-swap-04 OP-identity-swap-05 OP-identity-swap-06 OP-rename-entity-05 TWN-223 TWN-224 |
| Computed | 2: PLT-127 QST-109 |
| Temporal | 7: OP-temporal-convert-01 OP-temporal-convert-02 OP-temporal-convert-03 OP-temporal-convert-07 OP-temporal-new-03 QST-110 TWN-224 |
| Scan | 17: CMP-01 CMP-02 ENV-25 OP-add-index-01 OP-add-index-02 OP-add-index-06 OP-add-mandatory-02 OP-audit-columns-03 OP-create-static-seed-04 OP-create-static-seed-06 OP-create-static-seed-07 OP-modify-index-01 OP-modify-index-02 QST-108 TWN-133 TWN-208 TWN-209 |
| Authored | 98: CMP-01 CMP-07 CMP-08 CMP-09 CMP-10 CMP-11 CMP-13 CMP-14 CMP-15 CMP-17 CMP-18 CMP-22 OP-add-check-05 OP-add-default-06 OP-add-mandatory-06 OP-add-unique-08 OP-archive-entity-02 OP-archive-entity-03 OP-archive-entity-04 OP-archive-entity-05 OP-archive-entity-06 OP-archive-entity-07 OP-archive-entity-09 OP-archive-entity-10 OP-audit-columns-05 … |
| SeedDrift | 27: CMP-01 CMP-07 CMP-17 OP-create-static-seed-01 OP-create-static-seed-02 OP-create-static-seed-03 OP-create-static-seed-05 OP-delete-seed-value-01 OP-delete-seed-value-02 OP-delete-seed-value-03 OP-delete-seed-value-04 OP-delete-seed-value-05 OP-delete-seed-value-06 OP-delete-seed-value-07 OP-edit-seed-01 OP-edit-seed-02 OP-edit-seed-03 OP-edit-seed-04 OP-edit-seed-05 OP-edit-seed-06 OP-edit-seed-07 OP-edit-seed-08 OP-extract-to-lookup-01 OP-extract-to-lookup-08 QST-111 … |
| Unknown | 0:  |

## Stop → cases

| Stop | Cases |
|---|---|
| unreachable | 7: CLI-02 CLI-06 CLS-46 ENV-13 ENV-14 ENV-16 ENV-17 |
| no-access | 17: CLI-02 CLI-06 CLI-26 CLS-47 CLS-66 ENV-03 ENV-05 ENV-06 ENV-08 ENV-09 ENV-10 ENV-58 ENV-61 PRV-06 PRV-20 PRV-21 PRV-36 |
| missing-object | 11: CLI-06 CLI-18 CLI-26 ENV-19 ENV-21 ENV-37 PRV-12 PRV-13 QST-104 TWN-114 TWN-115 |
| stale-answers | 7: CI-07 CI-08 CLI-17 CLI-44 ENV-52 INV-E3-02 TWN-143 |
| authored-script | 111: CLI-19 CMP-01 CMP-07 CMP-08 CMP-10 CMP-11 CMP-13 CMP-14 CMP-15 CMP-17 CMP-22 CMP-23 OP-add-check-05 OP-add-default-05 OP-add-mandatory-05 OP-add-unique-08 OP-archive-entity-02 OP-archive-entity-03 OP-archive-entity-04 OP-archive-entity-05 … |
| check-witness | 10: CLI-20 OP-add-check-04 OP-add-check-06 OP-drop-check-03 OP-toggle-trust-06 PLT-120 PLT-78 PRV-18 TWN-206 TWN-220 |
| computed-witness | 3: CLI-21 PLT-127 QST-109 |
| planter-needs-row | 15: CLI-22 PLT-114 PLT-116 PLT-117 PLT-118 PLT-21 PLT-28 PLT-31 PLT-42 PLT-44 PLT-57 PLT-58 PLT-96 TWN-253 UP-13 |
| acceptance | 8: CLI-23 INV-M2-02 INV-S2-02 OP-add-check-07 PLT-92 PLT-93 PLT-98 TWN-124 |
| identity | 3: CLI-24 PLT-99 TWN-125 |
| in-flight | 6: CLI-16 CMP-12 ENV-32 ENV-34 OP-make-mandatory-09 TWN-138 |
| config | 31: CFG-02 CFG-03 CFG-04 CFG-05 CFG-06 CFG-07 CFG-08 CFG-09 CFG-10 CFG-11 CFG-15 CFG-16 CFG-17 CLI-02 CLI-08 CLI-15 CLI-16 CLI-26 CLI-31 INV-E4-01 … |
| toolchain | 12: CLI-02 CLI-03 CLI-15 CLS-44 CLS-45 REC-27 TLC-01 TLC-06 TLC-10 TLC-11 TWN-102 UP-13 |
| platform | 12: CLI-02 CLI-03 CLI-15 CLS-35 CLS-48 CLS-61 ENV-40 ENV-41 ENV-43 TLC-04 TWN-135 UP-13 |
| options-unknown | 9: CFG-12 CFG-13 CFG-15 CLI-02 CLI-03 CLI-06 CLI-15 CLI-30 INV-F6-02 |
| build-failed | 8: CLI-06 CLI-15 CLS-37 OP-create-entity-04 OP-delete-attribute-08 TLC-06 TWN-108 UP-13 |
| unknown-statement | 15: CLI-06 CLI-16 INV-E1-02 QST-14 QST-15 QST-55 QST-84 QST-85 QST-86 QST-87 QST-88 QST-90 QST-91 QST-92 QST-94 |
| unclassified | 16: CLI-16 CLI-36 CLS-03 CLS-09 CLS-10 CLS-11 CLS-50 CLS-51 CLS-54 CLS-58 CLS-62 CLS-65 INV-H1-03 OP-change-delete-rule-05 OP-move-schema-04 TWN-136 |
| cdc | 9: CLI-16 ENV-48 ENV-49 ENV-50 OP-add-optional-04 OP-delete-attribute-07 OP-extract-to-lookup-10 OP-rename-attribute-07 OP-rename-entity-06 |
| base-unpublishable | 5: CLI-16 CLS-49 ENV-44 PLT-46 PLT-48 |
| usage | 4: CLI-26 CLI-32 TWN-115 TWN-309 |
| probe-failed | 3: CLI-38 ENV-71 PRV-20 |
| answers-invalid | 10: CLI-39 ENV-55 ENV-56 ENV-70 INV-D2-03 INV-E3-02 INV-H1-03 PLT-60 PRV-15 PRV-39 |
| witness-failed | 3: CLI-40 PLT-119 TWN-125 |
| witness-not-synthetic | 3: CLI-41 PLT-124 PRV-27 |
| identifier-unsafe | 3: CLI-42 PRV-11 PRV-41 |
| interrupted | 2: CLI-37 INV-H1-02 |
| internal | 3: CLI-36 CLI-43 INV-H1-03 |

## Twin responsibility (ARCH §2) → cases

| Row | Cases |
|---:|---|
| 1 | TWN-101 TWN-102 TWN-120 TWN-314 |
| 2 | TWN-103 TWN-104 TWN-105 TWN-106 |
| 3 | TWN-107 TWN-108 TWN-242 |
| 4 | TWN-109 TWN-150 TWN-249 |
| 5 | TWN-110 |
| 6 | TWN-111 |
| 7 | TWN-112 |
| 8 | TWN-113 |
| 9 | TWN-114 TWN-115 |
| 10 | TWN-116 TWN-151 TWN-255 |
| 11 | TWN-117 TWN-126 TWN-249 |
| 12 | TWN-118 |
| 13 | TWN-119 TWN-120 TWN-253 TWN-313 |
| 14 | TWN-121 TWN-233 TWN-236 TWN-254 TWN-310 |
| 15 | TWN-122 TWN-311 |
| 16 | TWN-123 TWN-251 |
| 17 | TWN-111 TWN-124 TWN-125 TWN-251 TWN-317 |
| 18 | TWN-126 TWN-151 TWN-210 TWN-212 TWN-252 TWN-308 |
| 19 | TWN-127 |
| 20 | TWN-128 TWN-250 |
| 21 | TWN-129 TWN-309 |
| 22 | TWN-130 TWN-301 TWN-302 TWN-303 TWN-304 TWN-305 TWN-306 TWN-307 TWN-316 |
| 23 | TWN-131 TWN-132 TWN-133 TWN-234 |
| 24 | TWN-134 TWN-201 TWN-202 TWN-203 TWN-204 TWN-205 TWN-206 TWN-207 TWN-208 TWN-209 TWN-210 TWN-211 TWN-212 TWN-213 TWN-214 TWN-215 TWN-216 TWN-217 TWN-218 TWN-219 TWN-220 TWN-221 TWN-222 TWN-223 TWN-224 TWN-225 TWN-226 TWN-227 TWN-228 TWN-229 TWN-230 TWN-231 TWN-232 TWN-233 TWN-234 TWN-235 TWN-236 TWN-237 TWN-238 TWN-239 TWN-240 TWN-241 TWN-242 TWN-243 TWN-244 TWN-245 TWN-246 TWN-247 |
| 25 | TWN-135 TWN-315 |
| 26 | TWN-136 TWN-137 |
| 27 | TWN-138 TWN-139 |
| 28 | TWN-140 |
| 29 | TWN-141 |
| 30 | TWN-142 TWN-143 TWN-312 |
| 31 | TWN-144 |
| 32 | TWN-145 |
| 33 | TWN-146 |
| 34 | TWN-147 |
| 35 | TWN-148 TWN-248 |
| 36 | TWN-149 |

## Open `verify` cases

Each is settled by its first run against SQL Server 2022 and LocalDB (gate G2), then re-sourced as a receipt. The cross-cutting questions are `../ARCHITECTURE.md` §11.

### `00-invariants.md` (4)

| Case | Situation | What is uncertain |
|---|---|---|
| INV-D1-03 | nothing about the machine reaches the verdict | whether the two editions' ConvertTo-Json can be made to emit identical bytes (they differ in escaping and indentation), or the tool must pin one editi |
| INV-M3-01 | read makes exactly two environment sessions | whether sqlpackage's Extract opens one session or several; if several, M3 is restated as two tool invocations |
| INV-H6-01 | constraint validation runs after the post-deploy, on the pinned engine | the order was observed 2026-10-18 on sqlpackage 170.5.96 (ARCH §4, §5 step 10); this case pins it per engine version and is re-run on every pin bump |
| INV-A3-02 | the exit code survives every documented invocation | how each form passes a script's `exit n` to its caller (about_PowerShell_exe describes -File and -Command differently) |

### `10-questions.md` (21)

| Case | Situation | What is uncertain |
|---|---|---|
| QST-08 | two guarded tables ask one HasRows each | whether DacFx writes several guards in one batch or one batch each; the questions are the same either way, and CLS-07 attributes a block inside a mult |
| QST-13 | keywords match case-folded and whitespace-collapsed; the RAISERROR text and arguments do not | this is how SQL72045 re-formats the guard when it quotes it (scratch run); matching must hold for both spellings |
| QST-15 | the guard spelled without WITH NOWAIT stops instead of guessing | ARCH §11.3: whether DacFx 162.x writes the guard as 170.x does. A different spelling enters only as a vocabulary row with its case (E1) |
| QST-28 | decimal(18,4) → decimal(18,2): roundtrip | whether an ALTER COLUMN that reduces scale rounds silently (roundtrip) or raises (then overflow is the class) |
| QST-32 | int → a short string: overlen | whether a number too wide for the string raises (Msg 8115 for nvarchar, `*` for varchar) and so whether the class is overlen or overflow |
| QST-34 | a varchar collation change across code pages asks roundtrip | whether SQL Server rewrites varchar data across code pages on ALTER COLUMN (an nvarchar collation change does not) |
| QST-37 | a filtered index carries its filter; IS NOT NULL removes the nulls class | DacFx's spelling of the filter (copied into the coordinate, never re-parsed); the class rule is F10 ("unique among the filled values" is a filtered in |
| QST-55 | a WITH CHECK CHECK with no ADD in the delta stops | whether DacFx ever writes a bare CHECK CHECK (for example to re-trust a key an environment holds enabled but untrusted); if it does, a row is added th |
| QST-56 | a WITH NOCHECK ADD with no CHECK CHECK asks nothing | whether any effective option makes DacFx write this shape; if it can, the record states that the key lands untrusted (REC-15) |
| QST-59 | an FK to a parent created in this delta and filled by the post-deploy seed becomes Authored | ARCH §4 Coordinates rule 1 rests on the order observed 2026-10-18 on sqlpackage 170.5.96 (the post-deploy text precedes "Checking existing data", so a |
| QST-65 | a rebuild that moves a retyped column asks Fits for that column | whether DacFx rebuilds (rather than ALTERs) a column narrowed together with a reorder, whether it CASTs in the SELECT, and whether a guard then preced |
| QST-68 | an index over a non-persisted computed column asks Computed | building the index evaluates the expression for every row; ARCH §4's Computed row names only ADD … PERSISTED (reported) |
| QST-71 | a refactorlog rename and its __RefactorLog bookkeeping ask nothing | the sp_rename line is quoted live in sample-prs/rename-attribute.md (170.4.83.3); the __RefactorLog batches were seen in a scratch run |
| QST-72 | a schema move with its refactorlog entry asks nothing | the exact statement DacFx writes for a refactorlog schema move (sample-prs/move-schema.md proved the T-SQL by hand, not a generated delta) |
| QST-78 | a changed pre-deploy statement asks Authored, located by its text | sqlpackage embeds the pre-deploy text verbatim after USE [$(DatabaseName)], with no marker (scratch run), so the region is found by matching the head  |
| QST-80 | a changed MERGE seed asks SeedDrift | a `:r` include is expanded in the Script (scratch run), so the changed file is the included one; the kind is ARCH §4's SeedDrift row (an Authored subs |
| QST-88 | ALTER INDEX … REBUILD stops | whether DacFx writes this shape for an index option change; if it does, it enters as a no-question row with its case (E1) |
| QST-89 | turning SYSTEM_VERSIONING on over a history table that holds rows asks Temporal | the exact spelling DacFx writes; the kind is ARCH §4's Temporal row. The engine side passed on the Twin: SamplePr "temporal-convert: turning the exist |
| QST-90 | a smart-default backfill (GenerateSmartDefaults in the effective options) stops | whether and how DacFx writes the smart-default backfill; F6 decides whether the effective options can carry GenerateSmartDefaults = True at all |
| QST-105 | ADD PERIOD FOR SYSTEM_TIME over existing columns asks Temporal; over new columns it is answered from the delta | whether DacFx adds a period over existing columns with this statement or rebuilds the table (QST-63's shape); the question is ARCH §4's Temporal row |
| QST-110 | Temporal, end to end: versioning turned back on over a history table that holds rows | whether Extract keeps a period whose versioning is off (F9), and the SET (SYSTEM_VERSIONING = ON …) DacFx writes (QST-89); the engine fact that conver |

### `20-planter.md` (10)

| Case | Situation | What is uncertain |
|---|---|---|
| PLT-35 | temporal history is planted by UPDATE, never by INSERT | Does an UPDATE in the same transaction as the INSERT write a history row (start = end) that the probes count? If it does not, the two statements commi |
| PLT-46 | a memory-optimized table on LocalDB | Does LocalDB host In-Memory OLTP through a publish? The documentation does not settle it. The first Windows CI run decides which of the two outcomes h |
| PLT-47 | a columnstore index and a partitioned table on LocalDB | Both features are in every edition since SQL Server 2016 SP1. Untested: LocalDB through a publish, and a scheme spread over several filegroups, which  |
| PLT-71 | dup accent: a circumflexed `w` | Do N'ŵ1' and N'w1' compare equal under Latin1_General_CI_AI and SQL_Latin1_General_CP1_CI_AI? The first run decides. A varchar column has no accented  |
| PLT-87 | an untrusted FK: does Extract carry it, and does the copy keep it? | ARCH §11.1. If Extract drops WITH NOCHECK, the base publish makes the key trusted, and the planter adds trust(Order, FK_Order_Customer, untrusted): NO |
| PLT-88 | a disabled FK: does Extract carry it, and does the copy keep it? | ARCH §11.1. Red-team #1 shows a disabled FK changes the delta an environment receives (verified), but not that Extract preserves it. If Extract does n |
| PLT-90 | a system-named default: is `DF__…` preserved? | DacFx models a system-named constraint as unnamed, so a publish mints a new DF__ name. F9 asks for equal names "or stop". Does sp_rename clear is_syst |
| PLT-101 | the plan is empty after planting, the planter's own temporary changes included | Does DacFx's model carry trigger enabled state, and sequence and identity current values? If it does not, identity is blind to a failed restore, so pl |
| PLT-127 | a computed column the delta adds: stop computed-witness, then the declared row decides | whether DacFx adds a PERSISTED column in place (computed over every existing row) or rebuilds the table, and whether the failure is Msg 8115. The stop |
| PLT-122 | how often the real estate needs a declared row (ARCH §11.7) | ARCH §11.7. Do ordinal literals satisfy the estate's CHECKs often enough? A high CHECK count argues for using a column's literal default, which D3's s |

### `30-outcomes.md` (22)

| Case | Situation | What is uncertain |
|---|---|---|
| CLS-02 | a data-loss warning on a clean publish is attached, not a block | the `***` warning line during Initializing was seen in a scratch run (on a blocked publish); that a clean publish prints it the same way is unconfirme |
| CLS-04 | the data-loss guard, attributed by evidence | the layout, including SQL72045 re-formatting the batch, was seen in a scratch run; the block itself is receipt F7 / F13 |
| CLS-05 | the provider prefix is not part of any rule | "Framework …" is how scripts/prove.mjs's fixture and sample-prs quote it; the scratch run printed "Core …" |
| CLS-06 | a counterfeit guard in the pre-deploy is the authored script's refusal | red-team #12 (the counterfeit; not ✓); this output was seen in a scratch run |
| CLS-07 | in a batch holding several guards, SQL72014's line picks the table | whether DacFx puts several guards in one batch (QST-08), and that Line counts lines of the batch as the delta holds it |
| CLS-14 | Msg 547 on a foreign key at validation, after the post-deploy | the phase order (validation after the post-deploy, outside the transacted portion) was observed 2026-10-18 on sqlpackage 170.5.96 and is pinned per ve |
| CLS-19 | Msg 1505 then 1750 on ADD CONSTRAINT PRIMARY KEY: the first decides, both are recorded | whether sqlpackage prints 1750 as its own SQL72014 line (sample-prs/define-pk.md saw 1505 then 1750 from hand-run T-SQL) |
| CLS-39 | the first engine error decides; every one is recorded | whether sqlpackage prints 3621 as a second SQL72014 line (the scratch run's 1505 printed one line only) |
| CLS-42 | Msg 248 (a digit string too large for int) is overflow | that ALTER COLUMN raises 248 for this class; the mapping is ARCH §6 (220, 248 → overflow) |
| CLS-44 | a command timeout on the LocalDB copy | the text of the timeout line; ARCH §6's toolchain stop covers a LocalDB that does not respond; a witness-scale copy never needs that long, so the mach |
| CLS-45 | a connection failure to the copy is the machine, not the environment | the wording (scripts/prove.mjs matches "Unable to connect"); `unreachable` (exit 4) is reserved for environments |
| CLS-46 | a connection failure during `read` is unreachable | the Extract wording for a refused connection |
| CLS-48 | a platform mismatch at publish | the wording and the DacFx message number; `doctor` should stop this before any publish (S4) |
| CLS-49 | the base does not publish to LocalDB | whether LocalDB supports full-text, and the number it raises |
| CLS-52 | reordered lines classify the same | whether sqlpackage's order ever varies (prove.mjs's fixture and the scratch run differ in where "*** Could not deploy package." falls) |
| CLS-53 | localized engine text classifies by number | the localized wording, and that the Msg/Level/State prefix stays English; the object comes from the quoted batch first, the message text second |
| CLS-54 | a localized success line is never read as clean | whether sqlpackage localizes its own lines by the machine's UI culture; if it does, the localized line enters as a rule with its case |
| CLS-66 | Extract that cannot log in is no-access | the Extract wording for a refused login; the template is ARCH §6's no-access ("cannot log in to") |
| REC-12 | effects: renames from sp_rename; the __RefactorLog bookkeeping is not an effect | the delta shape (QST-71); the effect itself is the rename sample-prs/rename-attribute.md proved |
| REC-15 | a key added WITH NOCHECK and never validated is recorded as untrusted | whether DacFx writes the shape (QST-56); the untrusted outcome of a NOCHECK add without re-validation is F9's anti-pattern |
| REC-17 | effects: a schema transfer is a rename | the delta shape (QST-72) |
| REC-19 | residue after Msg 547 at validation: main and post-deploy applied, the key untrusted | the order (post-deploy before validation, outside the transacted portion) was observed 2026-10-18 on sqlpackage 170.5.96 and is pinned by INV-H6-01; t |

### `40-ops-columns.md` (9)

| Case | Situation | What is uncertain |
|---|---|---|
| OP-add-mandatory-07 | GenerateSmartDefaults in the effective options | skills/op/add-mandatory says a smart default silently stamps a value (e.g. ''). Uncertain: whether DacFx then omits the guard, and what it stamps into |
| OP-widen-03 | a widening raises no Fits question | uncertain: whether DacFx 162.x and 170.x name every lossy ALTER COLUMN in the data-loss comment above the guard, and never a widening. The delta carri |
| OP-widen-04 | the widened column sits in an index: warning 1945 now, Msg 1946 later | red-team #16 records 1945 warning now and 1946 failing later (✓). Uncertain: whether DacFx widens the indexed column in place (SQL Server allows a sam |
| OP-widen-05 | more than a million rows: Rows is read even when nothing is asked | ARCH §7 raises scale only under a data-sensitive, Scan, Rebuild or authored-DML statement, and ARCH §4's Scan row does not list a widening ALTER COLUM |
| OP-retype-implicit-05 | widening a primary-key IDENTITY column | uncertain: which shape DacFx emits for a type change on a clustered primary-key IDENTITY column: a drop and re-add of PK_OrderLine_Id (which asks Uniq |
| OP-retype-explicit-09 | edge: a text date's conversion depends on the session's language and date format | ARCH §11.12 (which language and date format the Octopus login runs under) · ARCH §5 step 2 (probes and the planter run under us_english and ymd). Unce |
| OP-audit-columns-05 | the one-release staging backfill the skill describes | skills/op/audit-columns says NOT NULL with no default ships in one release behind a pre-deploy backfill. By F2 the plan is computed before the pre-dep |
| OP-add-default-04 | the environment holds a system-named default on the column | ARCH §11.10: whether Extract keeps a system-generated default name, and whether the copy built from it carries that name (F9) or a fresh one. If a fre |
| OP-identity-swap-06 | an incoming foreign key around the rebuild | SamplePr "identity-swap: removing IDENTITY from Order.Id is a table rebuild — the true outcome under the production gate, keys and incoming FK preserv |

### `41-ops-keys.md` (12)

| Case | Situation | What is uncertain |
|---|---|---|
| OP-add-check-07 | the planter's presence literal breaks the new rule: acceptance stops it | ARCH §11.7: whether the ordinal literal for a decimal(18,2) column falls at or below 100 is what the first run decides; the case pins that acceptance, |
| OP-define-pk-07 | violating:nulls: a key column that is nullable in the environment | skills/op/define-pk (nullable key → Msg 8111 + 1750, proven by a direct ALTER, not through a DacFx delta) · F7 (a populated NOT NULL alter is guarded) |
| OP-drop-pk-04 | a drifted environment holds a key the project does not know references it | sample-prs/drop-pk.md says a drifted reference "fails this publish there"; red-team #14 says environment-only constraints are dropped silently: with D |
| OP-toggle-trust-03 | does the environment's delta re-trust an untrusted key on its own? | ARCH §11.1 (does Extract preserve an enabled-but-untrusted constraint?) and DacFx's IgnoreWithNocheckOnForeignKeys (default False): whether the offlin |
| OP-toggle-trust-04 | a disabled key in the environment: an unrelated release re-enables it and fails 547 | red-team #1 (a disabled FK changes the delta the environment receives ✓) and F9 (the re-add is WITH NOCHECK ADD + WITH CHECK CHECK and blocks Msg 547  |
| OP-toggle-trust-05 | a disabled key over clean data: an unrelated release silently turns it back on | red-team #1 (the delta differs ✓): the exact re-enable statements on DacFx 162.x and 170.x (ARCH §11.3) and their offline form (ARCH §11.1) are what t |
| OP-toggle-trust-06 | a disabled CHECK with a FALSE row: the re-enable needs the agent's witness | red-team #1 records the FK face; whether a disabled CHECK is re-enabled the same way, and survives Extract (ARCH §11.1), is what the first run decides |
| OP-toggle-trust-07 | a post-deploy that disables the key on every publish ("so the load can run") | INVARIANTS D6 records a non-empty second plan as not-silent; whether the re-enable appears in it at all depends on the disabled flag reaching Script ( |
| OP-change-delete-rule-03 | a legacy untrusted key with an orphan: the re-add fails 547 after the new rule committed | ARCH §11.1: the verdict follows F9's re-add law; whether the copy reproduces the legacy untrusted key with its orphan (through Extract or the trust wi |
| OP-change-delete-rule-05 | SET NULL on a NOT NULL reference: never clean, the run decides how it fails | skills/op/change-delete-rule ("ON DELETE SET NULL if the FK column is nullable"): SQL Server refuses the re-add in the generated main phase (Msg 1761) |
| OP-add-index-06 | ONLINE = ON: an edition feature LocalDB does not have | whether DacFx carries ONLINE = ON into the delta at all, and if it does, whether LocalDB (Express) fails it with Msg 1712 (then the expectation become |
| OP-rebuild-index-03 | a fill-factor edit meant to force a rebuild yields no delta | DacFx's IgnoreFillFactor (default True) is expected to hide the edit; with it False, DacFx is expected to re-create the index, which is modify-index;  |

### `42-ops-tables.md` (16)

| Case | Situation | What is uncertain |
|---|---|---|
| OP-rename-entity-08 | the offline delta under a refactorlog | whether Script(head, extracted schema) consults the target's applied refactorlog keys as the live Script does: an Extract carries schema, not `__Refac |
| OP-move-schema-03 | the refactorlog entry carries the move | which statement DacFx emits for a refactorlog schema move: the skills name both a schema-qualified sp_rename and `ALTER SCHEMA … TRANSFER`, and the li |
| OP-move-schema-04 | the scripted transfer in the same release as the header edit | F2 is the receipt that the plan is computed before the pre-deploy runs; the failing statement and its number (2714 on CREATE SCHEMA or on CREATE TABLE |
| OP-move-schema-07 | edge: a question on the moved table names its new address | whether DacFx orders the move before the guarded ALTER (so the guard names [sales].[OrderLine]); the resolution itself is ARCH §4's coordinate rule 2  |
| OP-temporal-new-03 | per environment: QA already has a plain table of that name | as OP-temporal-convert-01 (the conversion delta's shape is unrecorded) |
| OP-temporal-new-04 | a later change on the populated temporal table | whether DacFx alters a system-versioned column in place (history table alongside) or rebuilds behind SYSTEM_VERSIONING = OFF; and that a presence row  |
| OP-temporal-convert-01 | populated: the declarative conversion in one release | the delta is unrecorded: SamplePr "temporal-convert: turning the existing populated Customer system-versioned — the true outcome under the production  |
| OP-temporal-convert-02 | an empty table converts in place | as -01 (the delta's shape is unrecorded); skills/op/temporal-convert ("existing empty table → collapses to a single schema change") |
| OP-temporal-convert-03 | remedy: the staged conversion with historical defaults | the staged R1 is receipt-backed only on the path where the gate blocked (SamplePr "temporal-convert: turning the existing populated Customer system-ve |
| OP-temporal-convert-04 | edge: system-named defaults in R1 leave R2 a non-empty plan | ARCH §11.10 (whether DacFx treats a system-named default as different from a named one with the same definition); INVARIANTS F9 names default-constrai |
| OP-temporal-convert-06 | edge: ValidTo not the datetime2 maximum | the message (13575 or a neighbour) and which statement raises it; the Twin test's remedy notes "ValidTo must be the datetime2 max for every current ro |
| OP-temporal-convert-07 | violating: re-enabling versioning over a history row whose period ends before it starts | ARCH §6 (`blocked temporal`: the consistency check's failure, raised by a generated `Temporal` statement) · ARCH §11.13 (whether SET SYSTEM_VERSIONING |
| OP-create-static-seed-04 | edge: a child's new NOT NULL reference to the lookup, satisfied by the post-deploy seed | ARCH §4 (coordinates: an FK to a new parent is answered from the delta, and a seeded lookup counts) · ARCH §11.9: constraint validation ran after the  |
| OP-create-static-seed-05 | edge: a bare INSERT seed duplicate-keys on the second publish | skills/_index/idempotent-seed ("Never a bare INSERT (duplicate-keys on the second deploy)") · ARCH §5 step 11 runs the replay but does not say how an  |
| OP-create-static-seed-06 | violating:orphan, the reference's default is a value the seed does not carry | as -04 (ARCH §11.9: validation after the post-deploy); the untrusted residue after a 547 at WITH CHECK CHECK is F9 and red-team #4 ✓ |
| OP-create-static-seed-07 | remedy: the seed carries the reference's default value | as -04 (ARCH §11.9) · skills/_index/idempotent-seed (explicit ids, a constant meaning the same row in every environment) |

### `43-compound.md` (1)

| Case | Situation | What is uncertain |
|---|---|---|
| CMP-16 | the tightened column is the one renamed in the same release | whether DacFx emits the sp_rename before the guarded ALTER on the new name; the resolution itself is ARCH §4's coordinate rule 2 and INVARIANTS S3 (re |

### `50-environments.md` (13)

| Case | Situation | What is uncertain |
|---|---|---|
| ENV-04 | VIEW DEFINITION only, empty table: violation questions are implied | ARCH §11.2: is sys.partitions.rows exactly 0 for an empty table under a VIEW DEFINITION-only login on 2019 and 2022? If not, the implication is unsoun |
| ENV-10 | VIEW DEFINITION granted on one schema only | does Extract under schema-scoped VIEW DEFINITION refuse, or silently omit the invisible schema? If it omits, read must check database-level VIEW DEFIN |
| ENV-12 | the developers' real Dev login | TWIN_SUCCESSION §6b ("check what the developers' Dev login actually holds") |
| ENV-17 | the count batch on the real estate's largest table | how long one batched count takes on the real estate, and what command timeout read needs (TWIN_SUCCESSION §6, volume). ARCH §6's unreachable carries t |
| ENV-42 | compatibility level | does publishing schema.QA.dacpac under the effective options set the copy's level (it depends on ScriptDatabaseOptions), and does the identity step se |
| ENV-45 | an untrusted FK and an untrusted CHECK in the environment | ARCH §11.1: does Extract carry WITH NOCHECK, and does DacFx then emit WITH CHECK CHECK for an untrusted constraint the model declares trusted? If Extr |
| ENV-46 | a disabled FK in QA | ARCH §11.1, whether Extract carries the disabled state onto the copy; that a disabled FK changes the environment's delta is red-team #1 ✓ |
| ENV-47 | a system-named default in the environment | does Extract keep a system-generated constraint name, and can an offline Script name it in a DROP? If not, F5 fails here and the copy publishes a diff |
| ENV-50 | a rename on a CDC-tracked table: blocked cdc on a server with CDC, stop cdc on LocalDB | whether the copy reproduces QA's capture instance (CDC is not in the dacpac model, so it would be a metadata witness); red-team #6 (4928 on sp_rename  |
| ENV-62 | zero vs non-zero on a quiet database | ARCH §11.2 (sys.partitions.rows is documented as approximate; is it exact at zero vs non-zero?) |
| ENV-63 | a database that is not quiet | whether sys.partitions counts ghost and uncommitted rows; at deploy time the guard would wait on the uncommitted row's lock instead |
| ENV-66 | what Extract sends | INVARIANTS P4: the offline Script sends nothing, and a live Script sends xp_instance_regread; does Extract send it too, or create temp tables? |
| ENV-67 | a hardened login without xp_instance_regread | TWIN_SUCCESSION §10 asks for xp_instance_regread "for Script"; if the offline path never sends it, the grant leaves the access ticket and P4's trace c |

### `60-privacy.md` (3)

| Case | Situation | What is uncertain |
|---|---|---|
| PRV-10 | names outside ASCII | ARCH §11.5 and §11.8: does the sqlcmd the laptop has (VS-bundled or go-sqlcmd), driven under ConstrainedLanguage, pass N'…' identifiers without code-p |
| PRV-41 | sqlcmd variable syntax inside a name: sent verbatim, or identifier-unsafe | sqlcmd substitutes $(var) unless -x is given (Microsoft docs); whether the sqlcmd on the laptop (VS-bundled or go-sqlcmd) honours -x under Constrained |
| PRV-24 | the statement trace | whether Extract's own catalog queries create #temp tables or EXEC anything (ENV-66); the tool's own statements are expected |

### `70-process.md` (12)

| Case | Situation | What is uncertain |
|---|---|---|
| CLI-22 | A5 · planter-needs-row, then write the INSERT | whether witness-not-synthetic (ARCH §5 step 5) accepts a literal whose shape the repository's own CHECK forces: no `w`-marked value fits 'P-[0-9][0-9] |
| CLI-33 | stdout carries one line; progress goes to stderr | whether Write-Host or information-stream progress reaches redirected stdout under `powershell.exe -File`, and which stderr writer is ConstrainedLangua |
| CFG-02 | the example as printed, comments included | Windows PowerShell 5.1's ConvertFrom-Json rejects `//` comments where PowerShell 7 accepts them, so ARCH's own example may not parse on the laptop's d |
| CFG-17 | duplicate keys | whether ConvertFrom-Json on 5.1 and on 7 reports a same-case duplicate or keeps the last value silently; if silently, Read-FxConfig must find it in th |
| CI-02 | the LocalDB job | which LocalDB major the windows-latest image carries; a 15.x would stop `platform` on Sql160 |
| CI-14 | the ConstrainedLanguage job | whether `__PSLockdownPolicy` forces ConstrainedLanguage for Windows PowerShell 5.1 and for PowerShell 7 on the hosted image (it is a test hook, not a  |
| TLC-07 | a classic .sqlproj builds with Visual Studio's MSBuild | ARCH §11.4: whether the classic project builds with only VS's MSBuild on a corporate laptop, and how long it takes |
| TLC-08 | an SDK-style .sqlproj | whether VS's MSBuild resolves the Microsoft.Build.Sql SDK through the proxy without a .NET SDK |
| TLC-09 | sqlcmd flavours | ARCH §11.5: whether VS ships sqlcmd, and whether go-sqlcmd reaches LocalDB with the same flags (-b, -V, -h -1, -W) |
| TLC-11 | CREATE, RESTORE and DROP get 180 seconds | ARCH §11.6: how long LocalDB takes to CREATE, RESTORE and DROP a ~300-table schema; the 180 s is v3's (TWIN_SUCCESSION Appendix B) and ARCH §6's toolc |
| TLC-15 | encodings, in files and on stdout | 5.1's Set-Content (ANSI) and Out-File (UTF-16LE) defaults against 7's BOM-less UTF-8; whether 5.1 under ConstrainedLanguage can write UTF-8 without a  |
| TLC-17 | Windows PowerShell 5.1 and PowerShell 7 | ARCH names no PowerShell edition or version floor; 5.1 lacks `ConvertFrom-Json -AsHashtable` and comment support, and the editions' ConvertTo-Json dif |

### `80-twin-parity.md` (6)

| Case | Situation | What is uncertain |
|---|---|---|
| TWN-119 | row 13 · inverted trust gate: the Twin's forced trust passes a change the environment blocks | ARCH §11.1 (does Extract keep the WITH NOCHECK state?) · the Twin arm's wrong clean, by receipt: TwinMintLoopTests "trust gate: a violating mint refus |
| TWN-120 | row 13 · a disabled FK, a system-named default and a column collation survive onto the copy | ARCH §11.1 · the Twin side: it publishes the repository's schema and re-validates every check and FK (THE_TWIN §9), so none of these states can exist  |
| TWN-223 | row 24 · identity-swap · removing IDENTITY from Order.Id is a table rebuild | the Twin fact accepts either outcome and asserts the end state only (Id no longer IDENTITY; keys, rows and the incoming FK preserved): SamplePr "ident |
| TWN-224 | row 24 · temporal-convert · the existing populated Customer becomes system-versioned | the Twin fact accepts either outcome and asserts the end state only (temporal, history linked, rows and business data unchanged): SamplePr "temporal-c |
| TWN-302 | row 22 · `twin up` when current → "Nothing to apply.", fast | the Twin answered in about a second (THE_TWIN §7); whether the facsimile's no-op stays that fast depends on fingerprinting head without a full MSBuild |
| TWN-314 | row 1 · G4: the laptop check; `twin up` users move to `facsimile up` on the corporate machine | ARCH §11.4–11.6, §11.8 · TWIN_SUCCESSION Appendix C |

