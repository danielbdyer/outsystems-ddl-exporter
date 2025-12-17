# 24. Troubleshooting Playbook

---

## Build Failures

### "Unresolved reference to object"

**Symptom:** Build error citing an object that doesn't exist.

**Causes:**
- FK references a table not in the project
- View references a column that was removed
- Cross-database reference without database reference configured

**Resolution:**
- Check if the referenced object should exist — add it if missing
- Check if you removed something that's still referenced — fix the reference
- For cross-database: add a database reference to the project

---

### "Syntax error in SQL"

**Symptom:** Build error with SQL syntax issue.

**Causes:**
- Typo in SQL
- Missing comma, parenthesis, or keyword
- Using syntax not supported by target SQL version

**Resolution:**
- Read the error message — it usually points to the line
- Check for missing commas between column definitions
- Verify the syntax is valid for your target SQL Server version

---

### "Duplicate object name"

**Symptom:** Build error saying an object is defined multiple times.

**Causes:**
- Two files define the same object
- Copy/paste error left duplicate definition
- Merge conflict resolved incorrectly

**Resolution:**
- Search the project for the object name
- Remove the duplicate definition
- Check git history to understand how duplication occurred

---

## Deployment Failures

### "BlockOnPossibleDataLoss"

**Symptom:** Deployment fails with message about potential data loss.

**What triggered it:**
- Dropping a column that contains data
- Narrowing a column below current data size
- Dropping a table with rows
- Changing type in a lossy way

**This is the system protecting you.**

**Resolution:**
1. Review the generated script — what's it trying to do?
2. If data loss is intentional: Handle explicitly in pre-deployment script, then proceed
3. If unintentional: Fix your schema change
4. Never set `BlockOnPossibleDataLoss=False` for production

---

### "ALTER TABLE ALTER COLUMN failed because the column is referenced by a constraint"

**Symptom:** Can't modify a column because something depends on it.

**What triggered it:**
- Column is part of an index
- Column has a default constraint
- Column is referenced by a computed column or view

**Resolution:**
1. Identify the dependent object (error message usually says which)
2. In pre-deployment: drop the dependency
3. Let SSDT make the column change
4. In post-deployment: recreate the dependency (or let SSDT do it if declarative)

---

### "The INSERT statement conflicted with the FOREIGN KEY constraint"

**Symptom:** Post-deployment script fails on FK violation.

**What triggered it:**
- Your script is inserting data that references a non-existent parent
- Seed data has bad references

**Resolution:**
1. Check your seed data — do all FK values exist in parent tables?
2. Ensure parent data is seeded before child data
3. Fix the data in your script

---

### "Cannot insert NULL into column"

**Symptom:** Deployment fails on NOT NULL violation.

**What triggered it:**
- Adding NOT NULL column without default to table with existing data
- Post-deployment script inserting incomplete data

**Resolution:**
- Add a default constraint, or
- Make the column nullable initially, backfill, then alter to NOT NULL, or
- Backfill in pre-deployment script

---

### "Timeout expired"

**Symptom:** Deployment times out.

**What triggered it:**
- Large table operation (index build, table rebuild)
- Lock contention
- Long-running pre/post script

**Resolution:**
1. For large tables: Consider deploying during maintenance window
2. For indexes: Consider online index operations (Enterprise Edition)
3. For scripts: Batch large operations
4. Increase timeout in publish profile (but understand why it's slow first)

---

## Refactorlog Issues

### Rename treated as drop+create

**Symptom:** Generated script shows DROP COLUMN + ADD COLUMN instead of sp_rename.

**Cause:** Missing refactorlog entry.

**Resolution:**
1. Do not deploy — this will lose data
2. Either:
   - Use GUI rename to create refactorlog entry, or
   - Manually add refactorlog entry
3. Rebuild, verify generated script now shows rename

---

### Merge conflict in refactorlog

**Symptom:** Git merge conflict in `.refactorlog` file.

**Resolution:**
1. Keep BOTH rename entries (if different objects)
2. If same object renamed differently in each branch, coordinate with other developer
3. Ensure all GUIDs are unique
4. Build after merge to verify

---

## CDC Issues

### Capture instance not tracking new column

**Symptom:** Change History doesn't show changes to a new column.

**Cause:** Old capture instance doesn't know about the new column.

**Resolution:**
- Development: Disable and re-enable CDC on the table
- Production: Create new capture instance, update consumers

---

### "Invalid object name 'cdc.fn_cdc_get_all_changes_...'"

**Symptom:** CDC function doesn't exist.

**Cause:** Capture instance was disabled or never created for this table.

**Resolution:**
1. Check if CDC is enabled: `SELECT * FROM cdc.change_tables`
2. If missing, create capture instance: `EXEC sys.sp_cdc_enable_table ...`

---

## "It Works Locally But Fails in Pipeline"

### Check Environment Differences

| Check | How |
|-------|-----|
| SQL Server version | Compare local version to pipeline target |
| Connection string | Verify pipeline connects to right database |
| Publish profile | Ensure pipeline uses correct profile |
| Pre-existing state | Local DB may have different starting state than target |
| Permissions | Pipeline service account may have different permissions |

### Common Causes

1. **Local DB has manual changes:** Your local DB has objects or data that aren't in the project. Pipeline's target doesn't.

2. **Different starting state:** You created local DB fresh; pipeline targets DB that has history.

3. **Cached dacpac:** Pipeline using old build artifact. Ensure clean build.

4. **Profile mismatch:** Different publish profile settings between local and pipeline.

---

## Error Message Translation

| Error | Plain English | OutSystems Equivalent | Fix |
|-------|--------------|----------------------|-----|
| "Cannot insert NULL into column 'X'" | Column requires a value but none provided | Like when required attribute has no default | Add default or backfill |
| "FOREIGN KEY constraint failed" | Orphan data — parent doesn't exist | Like when reference points to deleted record | Clean orphans or add parent |
| "String or binary data would be truncated" | Value too long for column | Like when text exceeds attribute length | Increase column size or validate data |
| "Cannot drop column because it's referenced by..." | Something depends on this column | Like when attribute is used elsewhere | Drop dependencies first |
| "BlockOnPossibleDataLoss" | SSDT protecting you from destructive change | OutSystems warning on entity delete | Review, handle explicitly if intentional |

---

