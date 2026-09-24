# 9. The Refactorlog and Rename Discipline

---

## What the Refactorlog Is

The refactorlog is an XML file that tracks identity-preserving changes — specifically, renames.

When you rename a column in SSDT using the GUI:
- SSDT updates the column name in your `.sql` file
- SSDT adds an entry to the refactorlog

The refactorlog entry says: "This object used to be called X, now it's called Y. They're the same object."

---

## The Silent Catastrophe

Without a refactorlog entry, SSDT interprets a rename as:
- Old column: deleted
- New column: created (fresh, empty)

**Generated script without refactorlog:**
```sql
ALTER TABLE dbo.Person DROP COLUMN FirstName
ALTER TABLE dbo.Person ADD GivenName NVARCHAR(100) NULL
```

Under the production posture (`BlockOnPossibleDataLoss=true`) that drop-and-add is **refused** — the row-presence guard fires on the populated table (`Msg 50000`), the deploy rolls back, and the data in `FirstName` survives untouched. It only loses the column if the guard is relaxed. Either way the rename didn't happen.

**Generated script with refactorlog:**
```sql
EXEC sp_rename 'dbo.Person.FirstName', 'GivenName', 'COLUMN'
-- Data preserved, in place.
```

**The whole-object case is the truly silent one.** A bare *table* rename or *schema* move (rather than a column) doesn't even block. Under the production posture (`DropObjectsNotInSource=false`) it **phantoms**: the publish returns `Ok`, creates an empty table at the new name, and leaves the populated original stranded under the old name with its `object_id` unchanged — a green deploy that quietly moved nothing. The remedy is the same refactorlog entry (or a scripted `EXEC sp_rename` / `ALTER SCHEMA TRANSFER`) so SSDT renames the same object instead of inventing a new one.

---

## How to Rename Correctly

### Method 1: GUI Rename (Preferred)

1. In Solution Explorer or table designer, right-click the object
2. Select Rename
3. Enter new name
4. SSDT updates the file AND adds refactorlog entry

### Method 2: Manual Refactorlog Entry

If you've already edited the file directly, you can manually add the entry:

```xml
<Operation Name="Rename Refactor" Key="[unique-guid]" ChangeDateTime="2025-01-15T10:30:00">
  <Property Name="ElementName" Value="[dbo].[Person].[FirstName]" />
  <Property Name="ElementType" Value="SqlSimpleColumn" />
  <Property Name="ParentElementName" Value="[dbo].[Person]" />
  <Property Name="ParentElementType" Value="SqlTable" />
  <Property Name="NewName" Value="GivenName" />
</Operation>
```

But this is error-prone. Use the GUI.

---

## Protecting the Refactorlog

### Branch Merges

When two branches rename different objects, both add refactorlog entries. Merge conflicts in XML can be tricky.

**Resolution approach:**
- Keep BOTH entries (they're independent operations)
- Ensure GUIDs are unique
- Validate by building after merge

### Never Delete Entries

Refactorlog entries are needed for fresh environment deployments. Deleting "old" entries means fresh deployments treat those renames as drop-and-create.

**Even if the rename happened a year ago, keep the entry.**

### CI Validation

Consider adding a pipeline check:
- Detect column name changes in `.sql` files
- Verify corresponding refactorlog entry exists
- Fail PR if rename detected without refactorlog

---

