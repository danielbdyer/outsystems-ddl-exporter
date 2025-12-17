# 8. Referential Integrity Basics

---

## What Foreign Keys Actually Enforce

A foreign key constraint says: "Every value in this column must exist in that other table's column."

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
```

This means:
- **INSERT to Order:** CustomerId must exist in Customer
- **UPDATE Order.CustomerId:** New value must exist in Customer
- **DELETE from Customer:** Fails if Orders reference that Customer (default behavior)

---

## The Dependency Graph

Foreign keys create dependencies. Understanding the graph matters for:
- **Insert order:** Parent must exist before child
- **Delete order:** Children must be removed before parent
- **Drop order:** Can't drop parent table if FKs point to it

```
Customer (parent)
    │
    └──► Order (child)
            │
            └──► OrderLine (grandchild)
```

**Insert:** Customer → Order → OrderLine (parent first)
**Delete:** OrderLine → Order → Customer (children first)

---

## CASCADE Options

You control what happens when a parent row is deleted or updated:

| Option | On DELETE | On UPDATE |
|--------|-----------|-----------|
| `NO ACTION` (default) | Fail if children exist | Fail if children reference old value |
| `CASCADE` | Delete all children automatically | Update all children automatically |
| `SET NULL` | Set FK column to NULL in children | Set FK column to NULL |
| `SET DEFAULT` | Set FK column to default value | Set FK column to default value |

**Be cautious with CASCADE.** It's powerful but can cause surprising mass deletions.

---

## `WITH NOCHECK` and Trust

When you add an FK to a table with existing data, SQL Server validates all rows. If orphans exist, it fails.

You can skip validation:

```sql
ALTER TABLE dbo.[Order] WITH NOCHECK
ADD CONSTRAINT FK_Order_Customer 
FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId)
```

**But this creates an untrusted constraint:**
- New rows are validated
- Existing rows are not
- Query optimizer ignores untrusted constraints (can't use them for optimization)

**To regain trust:**

```sql
ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer
```

This validates all existing rows and marks the constraint as trusted.

---

## Finding Orphan Data

Before adding an FK, check for orphans:

```sql
-- Find orders with no matching customer
SELECT o.OrderId, o.CustomerId
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON o.CustomerId = c.CustomerId
WHERE c.CustomerId IS NULL
```

---

## SSDT and Referential Integrity

SSDT validates referential integrity at **build time**. If you define an FK to a table that doesn't exist in your project, build fails.

SSDT generates FK creation at **deploy time**. If orphan data exists, deploy fails (unless you use `WITH NOCHECK` via script).

---

