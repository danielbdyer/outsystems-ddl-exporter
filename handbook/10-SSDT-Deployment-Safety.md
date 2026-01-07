# 10. SSDT Deployment Safety

*(This section consolidates the settings discussion from earlier)*

---

## The Publish Profile Settings That Matter

Your publish profile (`.publish.xml`) controls deployment behavior. These settings are your safety net.

---

## `BlockOnPossibleDataLoss`

**What it does:** If SSDT's generated script would drop a column containing data, drop a table with rows, or narrow a column, deployment fails instead of proceeding.

**Setting:** `True` — always, every environment, non-negotiable.

```xml
<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
```

**When it fires:**
- Dropping a column that has data
- Dropping a table that has rows
- Narrowing a column that contains values too long for new size
- Changing type in a way that can lose precision

**What to do when it fires:**
1. Stop. This is the system protecting you.
2. Review the generated script — what's it trying to do?
3. If the data loss is intentional, handle it explicitly in pre-deployment
4. If unintentional, fix your schema change

**Never set this to False for production deploys.** If you need to override, do it consciously via pre-deployment scripting, not by disabling the guard.

---

## `DropObjectsNotInSource`

**What it does:** When `True`, objects in the target database that aren't in your SSDT project are dropped. When `False`, they're ignored.

```xml
<DropObjectsNotInSource>False</DropObjectsNotInSource>
```

**Recommendations:**

| Environment | Setting | Rationale |
|-------------|---------|-----------|
| Dev | True | Keep it clean, catch issues early |
| Test | True | Should match prod process |
| UAT | False | Don't accidentally drop test fixtures |
| Prod | **False** | Never auto-drop in prod |

**For production:** If you want something gone, do it explicitly in the schema (delete the file) so it goes through PR review. Don't rely on SSDT's diff to clean up.

---

## `IgnoreColumnOrder`

**What it does:** When `True`, SSDT ignores differences in column order. When `False`, it will reorder columns to match the definition.

```xml
<IgnoreColumnOrder>True</IgnoreColumnOrder>
```

**Keep this True.** Column order changes can trigger full table rebuilds for cosmetic benefit. Not worth it.

---

## `GenerateSmartDefaults`

**What it does:** When adding a NOT NULL column, SSDT can auto-generate a default value to populate existing rows.

```xml
<GenerateSmartDefaults>False</GenerateSmartDefaults>
```

**Recommendation:**
- `True` for dev (velocity)
- `False` for test/UAT/prod (force explicit handling)

Smart defaults can mask problems. You may not want empty strings or zeros silently backfilled.

---

## Settings Matrix by Environment

| Setting | Dev | Test | UAT | Prod |
|---------|-----|------|-----|------|
| BlockOnPossibleDataLoss | True | True | True | **True** |
| DropObjectsNotInSource | True | True | False | **False** |
| IgnoreColumnOrder | True | True | True | True |
| GenerateSmartDefaults | True | True | False | **False** |
| AllowIncompatiblePlatform | False | False | False | False |
| TreatTSqlWarningsAsErrors | False | True | True | True |

---

## The Settings as Last Line of Defense

These settings are guardrails, not substitutes for good process.

```
Tier system (process) → catches most issues
    ↓
PR review → catches issues process missed
    ↓
Local testing → catches issues review missed
    ↓
Settings (BlockOnPossibleDataLoss, etc.) → catches what everything else missed
```

When a setting blocks you, that's the system working. Investigate, don't bypass.

---

I'll continue with Sections 11-12 (Multi-Phase Evolution and CDC), then move to the Execution Layer (Pattern Templates and Anti-Patterns), then Process.

---

