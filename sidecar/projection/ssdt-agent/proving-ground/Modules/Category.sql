/*
  dbo.Category — a second static / lookup entity (explicit id, NO IDENTITY) + identity-swap SOURCE.

  CREATE-only schema item. Like dbo.Status, lookup rows carry EXPLICIT, stable ids so the seed
  MERGE can address them and FK targets are deterministic across environments (a constant id must
  mean the same row everywhere — see skills/_index/idempotent-seed/). Product gets a new nullable
  CategoryId so Category is a REAL FK target, which is what makes the delete-seed-value negative
  fire (a hard DELETE of a referenced value orphans Product rows).

  PARALLEL EXECUTORS — READ FIRST: do NOT edit this authored file in place. Copy the tree, publish
  to a UNIQUE database per `../self-test/PROTOCOL.md`.

  WHAT THIS TABLE UNLOCKS
  -----------------------
  - create-static-seed (STA-01): a brand-new static entity with a guarded MERGE. See
    skills/op/create-static-seed/ and skills/_index/idempotent-seed/.
  - edit-seed (STA-02): add a 'Refunded'-shaped value — a Declarative+Post-Deploy row add, no
    schema change. See skills/op/edit-seed/.
  - delete-seed-value (STA-04N): the deactivate-don't-delete proof. Because Product.CategoryId
    references Category, a HARD DELETE of a referenced Category row orphans Product (or vetoes on
    the FK). The correct move is IsActive=0, not DELETE. See skills/op/delete-seed-value/ and
    skills/_index/idempotent-seed/ (deactivate-don't-delete).
  - identity-swap SOURCE (STR-04): Category has NO IDENTITY. "Turn on Auto Number" here is a
    table REBUILD (you cannot ALTER a column to add IDENTITY) — shadow table + IDENTITY_INSERT +
    reseed + recreate FKs. See skills/op/identity-swap/.

  IsActive DEFAULT 1 gives the deactivate-don't-delete path a column to flip.

  UNLOCKS self-test ids: STA-01 (create-static-seed), STA-02 (edit-seed),
  STA-04N (delete-seed-value negative — Product FK makes the orphan real), STR-04 (identity-swap).
*/

CREATE TABLE dbo.Category
(
    -- Explicit id, NO IDENTITY. Lookup rows own their keys; the same constant must mean the same
    -- Category across environments. This is the identity-swap SOURCE (adding IDENTITY = rebuild).
    Id          INT             NOT NULL,
    Code        NVARCHAR(30)    NOT NULL,

    -- deactivate-don't-delete lever: retire a value by flipping this to 0, never a hard DELETE
    -- that orphans the Product rows pointing at it.
    IsActive    BIT             NOT NULL CONSTRAINT DF_Category_IsActive DEFAULT (1),

    CONSTRAINT PK_Category PRIMARY KEY CLUSTERED (Id)
);
GO
