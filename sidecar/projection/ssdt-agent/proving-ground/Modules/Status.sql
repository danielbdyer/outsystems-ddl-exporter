/*
  dbo.Status — the static / lookup entity.

  CREATE-only schema item. NO IDENTITY: lookup rows carry EXPLICIT, stable ids so the seed
  MERGE can address them and so FK targets (Order.StatusId) are deterministic. An OutSystems
  Static Entity becomes exactly this: an explicit-id table seeded by an idempotent post-deploy
  MERGE.

  WHAT THIS TABLE EXERCISES
  -------------------------
  - static-data seed: the post-deploy MERGE in Data/Seed.sql populates these rows. A re-publish
    with the seed UNCHANGED must be SILENT — the guarded WHEN MATCHED captures 0 rows. An
    unconditional WHEN MATCHED over-captures (the CDC-silence anti-proof). (self-test prompt 6.)

  - extract-values-to-lookup / FK target: Order.StatusId references Status.Id, so adding a new
    lookup value ('Refunded') is a Declarative+Post-Deploy change (Mechanism 2): no schema
    change, just a new MERGE row.

  - add/remove-IDENTITY (Auto Number): you CANNOT ALTER a column to add IDENTITY. Turning this
    into an Auto Number entity is a table-swap (IDENTITY_INSERT + reseed + recreate FKs), not a
    one-line edit. See skills/operations/structural.md.
*/

CREATE TABLE dbo.Status
(
    -- Explicit id, NO IDENTITY. Lookup rows own their keys.
    Id          INT             NOT NULL,
    Code        NVARCHAR(20)    NOT NULL,
    IsActive    BIT             NOT NULL CONSTRAINT DF_Status_IsActive DEFAULT (1),

    CONSTRAINT PK_Status PRIMARY KEY CLUSTERED (Id)
);
GO

-- One NULL is allowed under a UNIQUE index, so Code uniqueness is a clean separate proof.
CREATE UNIQUE INDEX UX_Status_Code ON dbo.Status (Code);
GO
