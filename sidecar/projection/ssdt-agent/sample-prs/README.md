# Sample PRs — Twin-proven, DBA-fidelity worked examples

A directory of **worked example pull requests, one per schema operation** — the change an
OutSystems developer's edit becomes in SSDT, and *what the database actually does with it*. Every
example is **proven objectively**: not "this should block" but a green integration test that publishes
the change to a live **Twin**, fills it with real-shaped synthetic data, and asserts the outcome by
consuming that data. Where intuition and reality disagree, these PRs teach reality.

## How each example is proven

- **The Twin** is a disposable SQL Server database published from this estate (Status → Customer →
  Order → OrderLine, a real FK chain with IDENTITY keys) and filled with deterministic, relationally
  sound synthetic data — the same engine the tooling ships.
- **Production-faithful publish.** The proof publishes with `BlockOnPossibleDataLoss = true`,
  `GenerateSmartDefaults = false`, `DropObjectsNotInSource = false` — DacFx 162.5.57, the same engine
  `sqlpackage` wraps, at the same settings a real deployment runs. This is why the outcomes match
  production and not a relaxed dev push.
- **Objective evidence.** Every "Deployment evidence" section quotes the *captured* output of the run —
  the block text, the row counts, the `sys.*` facts, the content digests — pasted verbatim, never
  asserted.
- **Where the proofs live.** `../../tests/Twin.Tests.Integration/SamplePr*Tests.fs` (11 classes, 41
  facts, all green). Run one class:
  `dotnet test tests/Twin.Tests.Integration/Twin.Tests.Integration.fsproj --filter "FullyQualifiedName~SamplePrTighteningTests"`.

## Read these first — the fidelity findings that surprise people

These are the examples where a plausible assumption is *wrong*, and the Twin proves it:

- **[move-schema](./move-schema.md)** and **[rename-entity](./rename-entity.md)** — a header-edit "move"
  or "rename" with no refactorlog is a **silent phantom**: the publish returns `Ok`, but it creates an
  *empty* new table and strands the populated original. A green deploy that didn't do what you asked.
  The real move/rename is `sp_rename` / `ALTER SCHEMA TRANSFER` (identity + rows preserved).
- **[add-check](./add-check.md)** / **[create-fk-orphan](./create-fk-orphan.md)** — a constraint over
  bad data doesn't cleanly block: SSDT adds it `WITH NOCHECK`, then `WITH CHECK CHECK` fails (Msg 547)
  and leaves it **untrusted** — the bad row survives and the optimizer ignores the rule. Fix is
  reconcile-then-trust.
- **[make-mandatory](./make-mandatory.md)** / **[add-mandatory](./add-mandatory.md)** — the block is
  **row-presence, not blank content**: a populated table is refused even at zero NULLs, so backfilling
  is necessary but not sufficient. **[add-default](./add-default.md)** is the safe counterpart.
- **[delete-entity](./delete-entity.md)** / **[rebuild-index](./rebuild-index.md)** — removing an Entity
  from the model does **not** drop the table under the production posture (phantom survival); an index
  rebuild produces **no declarative delta at all** (it's maintenance, `NothingToApply`).
- **[identity-swap](./identity-swap.md)** — removing Auto-Number is a **table rebuild**, and the
  data-loss gate *allows* it (a rebuild moves rows rather than dropping them) — the opposite of what the
  "BlockOnPossibleDataLoss" name suggests.

## The catalog

Every row links to its PR; each PR names the exact green test that proves it.

### Columns (`SamplePrTighteningTests`, `SamplePrCleanApplyTests`, `SamplePrCleanApply2Tests`)
| Operation | In OutSystems | What the publish actually does (proven) |
|---|---|---|
| [add-optional](./add-optional.md) | add a non-mandatory Attribute | clean apply; column live, rows untouched |
| [add-mandatory](./add-mandatory.md) | add Attribute, Is Mandatory = Yes (no default) | value-less NOT NULL **blocks** a populated table; empty applies |
| [add-default](./add-default.md) | add mandatory Attribute with a default | default **backfills every existing row**, so a populated table applies clean |
| [make-optional](./make-optional.md) | uncheck Is Mandatory | NOT NULL → NULL applies clean |
| [make-mandatory](./make-mandatory.md) | check Is Mandatory | **blocks** a populated table (row-presence guard) even at 0 NULLs; empty applies |
| [widen](./widen.md) | enlarge a Text Attribute's length | applies clean; every value preserved (digest identical) |
| [narrow](./narrow.md) | shrink a Text Attribute's length | over-length data **blocks**; a width that fits applies |
| [retype-implicit](./retype-implicit.md) | widen a numeric type | applies in place; every value preserved |
| [retype-explicit](./retype-explicit.md) | lossy type change | narrowing **blocks** (overflow); widening applies |
| [delete-attribute](./delete-attribute.md) | delete an Attribute | column drop **blocks** on a populated table; the scripted DROP is the irreversible step |
| [modify-default](./modify-default.md) | change an Attribute's default value | affects only future inserts; existing rows unchanged |
| [audit-columns](./audit-columns.md) | add Created/Updated stamps | NOT NULL stamps with function defaults **backfill every row** |

### Constraints (`SamplePrTighteningTests`, `SamplePrReferenceIntegrityTests`)
| Operation | In OutSystems | What the publish actually does (proven) |
|---|---|---|
| [add-check](./add-check.md) | enforce a business rule at the DB | over a violating row: `WITH CHECK CHECK` fails (Msg 547), leaves it **untrusted**; conforming data lands trusted |
| [add-unique](./add-unique.md) | mark an Attribute unique | duplicates **block** the unique index build (Msg 1505); a unique column applies |
| [toggle-trust](./toggle-trust.md) | validate an untrusted constraint | `WITH CHECK CHECK` flips `is_not_trusted` 1→0 over clean data; a violation keeps it untrusted |

### Indexes (`SamplePrCleanApplyTests`, `SamplePrSchemaChangeTests`, `SamplePrRemovalTests`)
| Operation | In OutSystems | What the publish actually does (proven) |
|---|---|---|
| [add-index](./add-index.md) | add an index for performance | non-unique index builds clean; rows untouched |
| [modify-index](./modify-index.md) | change an index's columns | clean DROP+CREATE; rows intact |
| [rebuild-index](./rebuild-index.md) | rebuild/defragment an index | **`NothingToApply`** — operational maintenance, no declarative delta |
| [drop-index](./drop-index.md) | remove an index | clean declarative drop; rows intact |

### Keys & references (`SamplePrReferenceIntegrityTests`, `SamplePrSchemaChangeTests`, `SamplePrRemovalTests`)
| Operation | In OutSystems | What the publish actually does (proven) |
|---|---|---|
| [create-fk-clean](./create-fk-clean.md) | link an Entity to another (clean data) | FK lands **trusted**, orphan probe 0; an orphan becomes rejected (Msg 547) |
| [create-fk-orphan](./create-fk-orphan.md) | link an Entity (orphan present) | `WITH CHECK CHECK` fails (Msg 547), FK left **untrusted**; reconcile → 1→0 |
| [drop-fk](./drop-fk.md) | remove a reference | clean drop; rows survive, the **guarantee** is what you lose (orphan probe 547→0) |
| [define-pk](./define-pk.md) | give an Entity its identifier | PK + clustered index built; rows/digest intact |
| [junction](./junction.md) | model many-to-many | new bridge, both FKs trusted, composite PK; a duplicate pair rejected (Msg 2627) |
| [change-delete-rule](./change-delete-rule.md) | set the Delete Rule (Protect/Delete) | FK NO ACTION → CASCADE, clean metadata DROP+ADD; Protect blocks, Cascade removes children |

### Static data (`SamplePrSeedTests`)
| Operation | In OutSystems | What the publish actually does (proven) |
|---|---|---|
| [create-static-seed](./create-static-seed.md) | a Static Entity with records | first converge inserts; second is **silent** (0 rows + identical hash + NothingToApply) |
| [edit-seed](./edit-seed.md) | change a Static Entity record | the guarded MERGE touches **exactly the changed row**; re-run silent |
| [delete-seed-value](./delete-seed-value.md) | remove a Static Entity record | removing it from the seed does **not** delete the row (the seed is additive); the explicit delete is FK-guarded — a **referenced** value is blocked (Msg 547); prefer IsActive=0 |
| [extract-to-lookup](./extract-to-lookup.md) | free-text Attribute → Static Entity ref | phase 1: lookup seeded, 0 unmapped, backfill 0 NULL/0 orphan, FK trusted |

### Tables & Entities (`SamplePrCleanApply2Tests`, `SamplePrRemovalTests`, `SamplePrRenameTests`, `SamplePrSchemaChangeTests`)
| Operation | In OutSystems | What the publish actually does (proven) |
|---|---|---|
| [create-entity](./create-entity.md) | create a new Entity | new table + IDENTITY PK + trusted FK; other tables untouched |
| [delete-entity](./delete-entity.md) | delete an Entity | **phantom survival** — the table + rows survive; the scripted DROP is the real, destructive step |
| [rename-entity](./rename-entity.md) | rename an Entity | bare rename is a **phantom**; `sp_rename` preserves object_id + rows (references break) |
| [rename-attribute](./rename-attribute.md) | rename an Attribute | bare rename **blocks** (drop+add); `sp_rename` preserves every value |
| [move-schema](./move-schema.md) | move an Entity to another module/schema | header edit is a **phantom move**; `ALTER SCHEMA TRANSFER` is the real one |
| [archive-entity](./archive-entity.md) | retire an Entity, keep the data | conservation — live + archived = original, byte-identical |
| [temporal-new](./temporal-new.md) | new Entity with full history | system-versioned (temporal_type=2) + auto history table |

### Structural / multi-phase (`SamplePrStructuralTests`, `SamplePrRebuildTests`)
| Operation | In OutSystems | What the publish actually does (proven) |
|---|---|---|
| [split-table](./split-table.md) | one Entity → two | phase 1: new table + 1:1 copy (digest match); the source-column drop is the guarded later phase |
| [merge-tables](./merge-tables.md) | two Entities → one | prove **1:1 cardinality before the copy** or a 1:many merge silently drops rows |
| [move-attribute](./move-attribute.md) | a field crosses Entities | copy-then-drop, never a rename; 1:1 join + digest match; the source drop is the guarded later phase |
| [identity-swap](./identity-swap.md) | toggle Auto-Number | a **table rebuild** the gate *allows* (rows moved, not dropped); keys + FKs preserved |
| [temporal-convert](./temporal-convert.md) | turn on history for an existing Entity | single publish **blocks** (no-default period columns); ships **staged** with historical defaults |

---

*41 operations · 41 PRs · 11 test classes · every proof green against a live Twin. The evidence in each
PR is captured output, not assertion — that is the point.*
