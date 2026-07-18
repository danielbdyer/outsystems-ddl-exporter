# 3. The Translation Layer

---

## Why This Section Exists

You know how to build software. You know OutSystems. You've shipped features, fixed bugs, and made schema changes hundreds of times.

Now we're asking you to do the same work in a different tool, with different mechanics, and different risk profiles. That's disorienting — not because you lack skill, but because your hard-won intuitions don't map cleanly.

This section is your translation guide. It won't make SSDT feel like OutSystems (it isn't), but it will help you find your footing faster by connecting what you know to what you're learning.

---

## 3.1 OutSystems → SSDT Rosetta Stone

### Vocabulary

| OutSystems Term | SSDT / SQL Server Term | Notes |
|-----------------|------------------------|-------|
| Entity | Table | Same concept. In SSDT, you define it in a `.sql` file with a `CREATE TABLE` statement. |
| Entity Attribute | Column | Same concept. Defined inline within the table definition. |
| Entity Identifier | Primary Key | OutSystems auto-generated this. Now you define it explicitly with `PRIMARY KEY`. |
| Auto Number | IDENTITY | `IDENTITY(1,1)` means "start at 1, increment by 1." |
| Reference Attribute | Foreign Key (FK) | The relationship OutSystems drew as a line. Now you write `FOREIGN KEY ... REFERENCES`. |
| Reference (the line in diagrams) | Foreign Key Constraint | Same thing, explicit syntax. |
| Delete Rule (Protect/Delete/Ignore) | ON DELETE (NO ACTION/CASCADE/SET NULL) | You now control cascade behavior explicitly. |
| Index (in Entity properties) | Index | Same concept, more control over type (clustered, non-clustered, filtered, covering). |
| Static Entity | Lookup/Reference Table + Seed Data | Two pieces: the table structure (declarative) and the data (post-deployment script). |
| Entity Record | Row | Same concept. |
| Entity Diagram | No direct equivalent | Your `.sql` files *are* the schema. Use SSMS diagrams or VS schema compare for visualization. |
| Data Type (Integer, Text, Date, etc.) | Data Type (INT, NVARCHAR, DATETIME2, etc.) | Similar but more specific. See data type mapping below. |
| Is Mandatory = Yes | NOT NULL | OutSystems terminology → SQL constraint. |
| Is Mandatory = No | NULL (or omit — NULL is default) | Column allows empty values. |
| Default Value | Default Constraint | Same concept, explicit syntax: `CONSTRAINT DF_Table_Column DEFAULT (value)`. |
| Unique Attribute | Unique Constraint | `CONSTRAINT UQ_Table_Column UNIQUE`. |
| Service Studio | Visual Studio + SSDT | Where you edit. |
| Service Center | SQL Server Management Studio (SSMS) | Where you inspect the running database. |
| Publish (1-Click Publish) | Deploy / Publish | Similar lifecycle, but SSDT deploys via pipeline, not directly from your IDE. |
| eSpace / Module | SSDT Project / .sqlproj | The unit of deployment. |
| Solution | Solution (same term) | Container for multiple projects. |
| Integration Studio | Integration Studio (still used) | The bridge — you refresh External Entity definitions here after SSDT deploys. |
| External Entity | External Entity (same term) | An entity whose schema lives outside OutSystems — in our SSDT-managed database. |

### Data Type Mapping

| OutSystems Type | SQL Server Type | Notes |
|-----------------|-----------------|-------|
| Integer | INT | Exact match. |
| Long Integer | BIGINT | For values > 2.1 billion. |
| Decimal | DECIMAL(p,s) | You specify precision (p) and scale (s). e.g., `DECIMAL(37,8)` for currency. |
| Boolean | BIT | 0 = false, 1 = true. |
| Text | NVARCHAR(n) or NVARCHAR(MAX) | We use NVARCHAR (Unicode) by default. A declared length is preserved verbatim up to 4000; beyond that, NVARCHAR(MAX). |
| Date | DATETIME | The platform mapping — date-only intent, stored with a midnight time component. |
| Time | DATETIME | The platform mapping — time-only intent, stored on the datetime epoch date. |
| Date Time | DATETIME | `rtDateTime` maps to `DATETIME`; `rtDateTime2` maps to `DATETIME2(7)`. |
| Binary Data | VARBINARY(n) or VARBINARY(MAX) | For files, images, etc. |
| Email, Phone Number | VARCHAR(n) | ANSI `VARCHAR` — the platform mapping: Email `VARCHAR(250)`, Phone `VARCHAR(20)`. |
| Currency | DECIMAL(37,8) | We use DECIMAL, not the MONEY type. |
| Entity Identifier (FK) | BIGINT | Must match the referenced primary key's type. |

### Structural Concepts

| OutSystems Concept | SSDT Equivalent | How It Works |
|--------------------|-----------------|--------------|
| Entity relationships shown as lines | Foreign key constraints | You define them explicitly in the table or as separate constraint objects. |
| Cascade delete (Delete Rule = Delete) | ON DELETE CASCADE | Deleting a parent automatically deletes children. |
| Protect (Delete Rule = Protect) | ON DELETE NO ACTION | Delete fails if children exist. This is the default. |
| Computed/calculated attributes | Computed columns | `[FullName] AS ([FirstName] + ' ' + [LastName])` — SQL Server calculates it. |
| Entity versioning / history | System-versioned temporal tables or CDC | OutSystems didn't have this. Now you can track all historical states. |

---

## 3.2 "I Used To... Now I..."

This is your quick-reference for the action translation. When you find yourself thinking "I used to just...", look here.

### Adding and Modifying Structure

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| Add an attribute in Service Studio → Publish | Add column to table's `.sql` file → PR → merge → deploy | PR review required. More eyes on changes. |
| Set "Is Mandatory = Yes" on new attribute | Add `NOT NULL` and a `DEFAULT` constraint | If table has data, existing rows need a value. You must provide default or backfill. |
| Set "Is Mandatory = No" | Add column without `NOT NULL` (or explicitly write `NULL`) | Simpler — this is the easier direction. |
| Change attribute data type → Publish | Classify the change first (implicit or explicit conversion?) | Some type changes are safe; others need multi-phase. See Operation Reference. |
| Rename an attribute → Publish | Use SSDT GUI rename (creates refactorlog entry) → PR → deploy | **Critical:** Without refactorlog, SSDT drops the column and recreates it. Data loss. |
| Delete an attribute → Publish | Follow deprecation workflow: soft-deprecate → verify unused → drop | OutSystems could roll back. SQL Server deletion is permanent. Process protects you. |
| Change attribute length (Text 50 → Text 100) | Change length in column definition: `NVARCHAR(50)` → `NVARCHAR(100)` | Widening is safe. Narrowing is dangerous (data truncation). |
| Add an index in Entity properties | Create index in SSDT: `CREATE INDEX IX_Table_Column ON Table(Column)` | Same concept, explicit syntax. Large tables = blocking during creation. |

### Working with Relationships

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| Create a Reference Attribute → line appears | Add FK column + foreign key constraint | Two parts: the column itself, then the constraint. |
| Set Delete Rule = Delete (cascade) | Add `ON DELETE CASCADE` to FK definition | Explicit syntax. Be careful — cascades can be surprising. |
| Delete a reference → Publish | Drop the FK constraint (may need to drop column too) | Order matters. Drop constraint before dropping column. |
| Add a Reference to existing data | Verify no orphan data exists first, then add FK | OutSystems checked this for you. Now you run validation queries. |

### Working with Static Entities (Lookup Tables)

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| Create Static Entity → add records | Create table (declarative) + seed data (post-deployment script) | Structure and data are separate concerns. |
| Add a record to Static Entity → Publish | Add INSERT to post-deployment script (must be idempotent) | Script must handle "already exists" case gracefully. |
| Rename Static Entity record | Update the record in seed script; update FK references if needed | More manual than OutSystems, but same concept. |

### The Development Workflow

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| Make change → Publish → see it in Service Center | Make change → build locally → deploy to local SQL → PR → merge → pipeline deploys | More steps, but each is a checkpoint. Errors caught earlier. |
| See schema visually in Entity Diagram | Use SSMS database diagrams, VS schema compare, or read `.sql` files | Visualization is available but separate from editing. |
| Trust that Publish wouldn't break things | Review generated script before deploy; trust settings like `BlockOnPossibleDataLoss` | You have more control, which means more responsibility. |
| Rollback by republishing previous version | Rollback varies by change type — some are symmetric, some need restore | No universal "undo." Plan rollback before you deploy. |

### The Integration Studio Bridge

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| (External Entities didn't exist in this form) | Make schema change in SSDT → deploy → refresh External Entity in Integration Studio → publish extension → refresh in Service Studio | This is the new boundary. Schema changes flow through SSDT first. |

---

## 3.3 Risk Recalibration: "Feels Like / Actually Is"

OutSystems abstracted safety. It prevented many mistakes by design. SSDT gives you more power, which means more ways to hurt yourself.

This table recalibrates your risk intuition.

### Operations That Are More Dangerous Than They Feel

| Operation | In OutSystems this felt... | In SSDT it actually is... | Why |
|-----------|---------------------------|---------------------------|-----|
| **Rename attribute** | Safe — just a name change | 🔴 **Dangerous** — data loss without refactorlog | OutSystems tracked identity internally. SSDT tracks by name. A renamed column looks like "old deleted, new created." |
| **Change data type** | Usually safe — OutSystems converted | 🟡 **Variable** — safe to dangerous depending on types | OutSystems did safe conversions silently. SQL Server may fail, truncate, or require explicit conversion. |
| **Delete attribute** | Safe — could rollback | 🔴 **Permanent** — data gone forever | OutSystems versioned everything. SQL Server DROP means gone. Backup restore is your only recovery. |
| **Add NOT NULL attribute** | Safe — OutSystems handled it | 🟡 **Requires thought** — existing rows need values | OutSystems figured out defaults. SQL Server fails if existing rows have no value. |
| **Delete entity with data** | Guarded — OutSystems warned you | 🔴 **Permanent** — `BlockOnPossibleDataLoss` is your only guard | Setting must be True. If False, silent data destruction. |
| **Change attribute length (narrowing)** | Safe — OutSystems checked fit | 🔴 **Dangerous** — truncation or failure | OutSystems validated. SQL Server truncates or errors depending on settings. You must verify data fits. |

### Operations That Are Safer Than They Feel

| Operation | This might feel scary... | But it's actually... | Why |
|-----------|-------------------------|----------------------|-----|
| **Add new entity (table)** | New, unfamiliar syntax | 🟢 **Safe** — purely additive, Tier 1 | Nothing references it yet. Can't break existing code. |
| **Add nullable attribute** | Editing a production table | 🟢 **Safe** — purely additive, Tier 1 | Existing rows get NULL. Existing queries unaffected. |
| **Add an index** | Touching table structure | 🟢 **Mostly safe** — additive for queries | Queries get faster. Only risk is blocking during creation on large tables. |
| **Add default constraint** | Changing column behavior | 🟢 **Safe** — only affects future inserts | Existing data unchanged. New inserts get the default if no value provided. |
| **Create a view** | Adding complexity | 🟢 **Safe** — just a named query | Views are additive. Can be dropped without affecting underlying data. |
| **Widen column (TEXT 50 → 100)** | Changing column definition | 🟢 **Safe** — all existing values still fit | Widening never loses data. Only narrowing is dangerous. |

### The Mental Model Shift

**In OutSystems:** "Publish and see what happens. If it's wrong, publish again."

**In SSDT:** "Classify, plan, verify, deploy. Know your rollback before you go."

This isn't because SSDT is worse — it's because you now have direct access to the database, without the abstraction layer that protected you. That access enables things OutSystems couldn't do (CDC, temporal tables, complex constraints, precise indexing). The cost is that you must provide the judgment OutSystems provided for you.

**The good news:** The playbook encodes that judgment. Follow the process, use the tiers, and you'll develop the intuition over time.

---

## 3.4 The Integration Studio Bridge

This is the literal boundary between OutSystems and SSDT. Every schema change crosses this bridge.

### The Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│  1. SSDT CHANGE                                                         │
│                                                                         │
│     You make a schema change:                                           │
│     - Edit .sql file in Visual Studio                                   │
│     - PR → review → merge                                               │
│     - Pipeline deploys to target database                               │
│                                                                         │
│                              │                                          │
│                              ▼                                          │
├─────────────────────────────────────────────────────────────────────────┤
│  2. DATABASE UPDATED                                                    │
│                                                                         │
│     The SQL Server database now has the new schema.                     │
│     OutSystems doesn't know yet.                                        │
│                                                                         │
│                              │                                          │
│                              ▼                                          │
├─────────────────────────────────────────────────────────────────────────┤
│  3. INTEGRATION STUDIO REFRESH                                          │
│                                                                         │
│     Open Integration Studio:                                            │
│     - Connect to the database                                           │
│     - Select the External Entity                                        │
│     - Click "Refresh" to pull new schema                                │
│     - Review changes (new columns, modified types, etc.)                │
│     - Publish the extension                                             │
│                                                                         │
│                              │                                          │
│                              ▼                                          │
├─────────────────────────────────────────────────────────────────────────┤
│  4. SERVICE STUDIO UPDATE                                               │
│                                                                         │
│     In Service Studio:                                                  │
│     - Refresh references to the extension                               │
│     - Update application logic if needed (new attributes, etc.)         │
│     - Publish the application                                           │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Step-by-Step: Refreshing an External Entity

**Prerequisites:**
- SSDT change has been deployed to the target environment
- You have Integration Studio installed and access to the database

**Steps:**

1. **Open Integration Studio**
   - Launch from Windows Start menu or OutSystems Service Studio (Edit → Open Integration Studio)

2. **Open the Extension**
   - File → Open → navigate to your extension (`.xif` file)
   - Or connect to your environment and download the extension

3. **Connect to Database**
   - Go to the Entities folder in your extension
   - Right-click the External Entity you need to refresh
   - Select "Refresh Entity" (or similar — exact wording may vary by version)

4. **Review the Detected Changes**
   - Integration Studio shows what's different between the extension definition and the actual database
   - **Added columns:** Will appear as new attributes
   - **Removed columns:** Will be flagged for removal from entity
   - **Type changes:** May show as modified attributes
   - **Renamed columns:** Will appear as "old removed, new added" — this is expected if you renamed in SSDT

5. **Accept or Adjust**
   - For straightforward changes, accept the detected modifications
   - For renames: you may need to manually map the old attribute to the new one, or delete old and configure new
   - Verify data types mapped correctly (see data type mapping above)

6. **Verify Entity Configuration**
   - Check that Identifier is set correctly
   - Check that Is Mandatory matches your NOT NULL constraints
   - Check that data types are appropriate for OutSystems use

7. **Publish Extension**
   - Click "1-Click Publish" (or similar)
   - Extension is now updated in the environment

8. **Update Service Studio**
   - Open Service Studio
   - Open the module(s) that use this External Entity
   - Right-click the extension reference → Refresh
   - Review any breaking changes (removed attributes, type changes)
   - Update application logic as needed
   - Publish

### Common Integration Studio Scenarios

| You see... | It means... | What to do |
|------------|-------------|------------|
| New attribute detected | SSDT added a column | Accept it. Configure Is Mandatory, data type, etc. |
| Attribute missing | SSDT removed a column | Verify this was intentional. Accept removal. Update app logic if it used this attribute. |
| Attribute type changed | SSDT changed the column's data type | Accept it. Verify OutSystems type mapping is appropriate. May require app logic updates. |
| Entity not found | Table was renamed or dropped in SSDT | If renamed: create new External Entity pointing to new table name. If dropped: this is expected, remove from extension. |
| Unexpected schema differences | Something is out of sync | Verify SSDT deployment completed successfully. Check you're connected to the right database/environment. |
| No changes detected | Database matches extension | Nothing to do — your SSDT change may not have affected this entity, or refresh already happened. |

### Troubleshooting Integration Studio Refresh

| Problem | Likely Cause | Resolution |
|---------|--------------|------------|
| "Cannot connect to database" | Wrong connection string, network issue, permissions | Verify connection details. Check you have access to the database. |
| Refresh shows changes you didn't make | Connected to wrong environment | Check connection — are you pointing at dev when you meant test? |
| Refresh shows no changes but you expected some | Deployment didn't complete, or refreshed already | Verify SSDT pipeline succeeded. Check database directly with SSMS. |
| Type mapping seems wrong | OutSystems chose a different type than expected | You can manually adjust the attribute type in Integration Studio. |
| "Entity has dependencies" warning | OutSystems modules reference this entity | Expected if entity is in use. Proceed, then update dependent modules. |
| Publish fails after refresh | Extension has errors | Check the error log. Common issues: duplicate attribute names, invalid type mappings. |

### When Refresh Shows Unexpected Changes

If Integration Studio shows changes you didn't expect, pause and investigate:

1. **Verify the right environment:** Are you connected to the database you think you are?

2. **Check SSDT deployment status:** Did the pipeline succeed? Check Azure DevOps (or your CI/CD tool).

3. **Check the database directly:** Open SSMS, connect to the database, verify the table structure.

4. **Check for parallel changes:** Did someone else make SSDT changes that merged before yours?

5. **Check for missed migrations:** If this is a fresh environment, did all schema migrations run?

If something looks wrong, **do not accept the changes blindly**. Investigate first. It's easier to diagnose before you publish than after.

### Coordinating SSDT and OutSystems Releases

For changes that affect both schema and application logic:

**Simple case (additive changes):**
- SSDT deploys first (adds column, index, etc.)
- OutSystems deploys second (uses the new column)
- Order matters — OutSystems can't reference what doesn't exist yet

**Complex case (breaking changes):**
- May require multi-phase approach
- Example: Renaming a column
  - Phase 1: SSDT adds new column (old and new coexist)
  - Phase 2: OutSystems migrates to new column
  - Phase 3: SSDT removes old column

**Timing:**
- Allow time between SSDT deploy and OutSystems refresh
- Verify SSDT deployment succeeded before starting Integration Studio work
- In lower environments, this can be minutes apart
- In production, coordinate with release schedule

---

## 3.5 Quick Reference Card

*Print this. Pin it to your wall. Refer to it until you don't need to.*

```
┌─────────────────────────────────────────────────────────────────────────┐
│  SSDT TRANSLATION QUICK REFERENCE                                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  VOCABULARY                                                             │
│  Entity = Table          Attribute = Column       Reference = FK        │
│  Identifier = PK         Static Entity = Lookup Table + Seed Script     │
│  Publish = Deploy        Service Center = SSMS                          │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  DANGER ZONES (more dangerous than they feel)                           │
│  🔴 Rename attribute → Use GUI rename, verify refactorlog               │
│  🔴 Delete attribute → Follow deprecation workflow                      │
│  🔴 Narrow column → Verify all data fits first                          │
│  🟡 Change data type → Classify: implicit safe, explicit = multi-phase  │
│  🟡 Add NOT NULL → Need default or backfill                             │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  SAFE ZONES (safer than they feel)                                      │
│  🟢 Add new table → Purely additive, Tier 1                             │
│  🟢 Add nullable column → Purely additive, Tier 1                       │
│  🟢 Add index → Additive (watch blocking on large tables)               │
│  🟢 Add default constraint → Only affects future inserts                │
│  🟢 Widen column → All existing values still fit                        │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  THE FLOW                                                               │
│  Edit .sql → PR → Merge → Pipeline deploys → Integration Studio         │
│  refresh → Service Studio refresh → App publish                         │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  BEFORE ANY CHANGE                                                      │
│  □ Classified tier?    □ CDC table?    □ Need multi-phase?              │
│  □ Refactorlog needed? □ Reviewers tagged?                              │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## What's Next

Now that you have the translation layer:

- **For conceptual foundation:** Read [4. State-Based Modeling vs. Imperative Migrations](#)
- **For your first change:** Read [22. The Change/Release Process](#) and use the [Before-You-Start Checklist](#)
- **For specific operations:** Find your operation in [16. Operation Reference](#)
- **If you're stuck:** Check [24. Troubleshooting Playbook](#) or ask in #ssdt-questions

---

Let me map the optimal sequence based on dependencies and team needs:

**Foundation Layer (enables everything else):**
- Section 4: State-Based Modeling → Section 5: Anatomy of SSDT Project

**Conceptual Consolidation (thread content, needs structuring):**
- Sections 6-12: Pre/Post Scripts, Idempotency, Referential Integrity, Refactorlog, Safety, Multi-Phase, CDC

**Execution Layer (the "how to do it"):**
- Section 17: Multi-Phase Pattern Templates → Section 19: Anti-Patterns Gallery

**Process Layer (the human workflow):**
- Section 20: OutSystems → External Entities Workflow → Section 21: Local Dev Setup → Section 22: Change/Release Process

**Tools Layer (quick reference):**
- Section 18: Decision Aids

**Reference Layer (lookup material):**
- Remaining sections (Standards, Templates, Glossary, etc.)

Let me begin.

---

