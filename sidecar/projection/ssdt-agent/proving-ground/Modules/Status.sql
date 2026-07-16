/*
  dbo.Status — the static / lookup entity.

  CREATE-only schema item. No IDENTITY: lookup rows carry explicit, stable ids so the seed
  MERGE can address them and so FK targets (Order.StatusId) are deterministic. An OutSystems
  Static Entity becomes exactly this: an explicit-id table seeded by an idempotent post-deploy
  MERGE.

  WHAT THIS TABLE EXERCISES
  -------------------------
  - static-data seed: the post-deploy MERGE in Data/Seed.sql populates these rows. A re-publish
    with the seed unchanged must be silent — the guarded WHEN MATCHED captures 0 rows. An
    unconditional WHEN MATCHED over-captures, breaking CDC-silence. (self-test prompt 6.)

  - extract-values-to-lookup / FK target: Order.StatusId references Status.Id, so adding a new
    lookup value ('Refunded') needs no schema change — it ships as a post-deploy change: just a
    new MERGE row in the seed.

  - add/remove-IDENTITY (Auto Number): IDENTITY cannot be added to an existing column by ALTER.
    Turning this into an Auto Number entity is a table-swap (IDENTITY_INSERT + reseed + recreate
    FKs), not a one-line edit. See skills/operations/structural.md.
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
