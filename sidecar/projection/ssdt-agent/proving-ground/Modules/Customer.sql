/*
  dbo.Customer — the make-mandatory and rename-attribute proving table.

  This is a CREATE-only schema item (Build Action = Build). Prove a change by editing this
  destination, never by writing ALTER. "Edit the CREATE, never write ALTER."

  PARALLEL EXECUTORS — READ FIRST: when many subagents prove a case at once, do not edit this
  authored file in place. Copy the whole proving-ground tree to a private scratch dir and publish
  to a unique database (/TargetDatabaseName:PG_<testId>_<rand>) per the protocol in
  `../self-test/PROTOCOL.md`. The authored tree and the default DB are shared and read-only; the
  scratch copy + a unique DB are how a hundred provers run without colliding.

  WHAT THIS TABLE EXERCISES
  -------------------------
  - make-mandatory (Email NULL -> NOT NULL): the seed plants Email NULL rows. The outcome is
    decided entirely by whether the table has rows — not by whether the Email column has NULLs.
    SSDT's BlockOnPossibleDataLoss guard for this change is generated as
        IF EXISTS (SELECT TOP 1 1 FROM dbo.Customer) RAISERROR(...,16,127)
    placed before the ALTER COLUMN. That is table-has-rows, not column-has-NULLs. Consequences:
      * Empty table -> the IF EXISTS is false, the RAISERROR never fires, the ALTER COLUMN
        NOT NULL lands. It ships as a single schema change applied in place, no data read or
        written, and any team member can review it.
      * Populated table (NULLs present or zero NULLs — it does not matter which) -> Strict
        always blocks it. A pre-deploy backfill that clears every NULL does not clear the block;
        the column stays nullable. Proven on a disposable copy of Dev: backfilled to 0 NULL
        emails, Strict still blocked it. So on a populated table this is not a clean
        pre-deploy-script-then-schema release.
    The honest remedy on a populated table is a conscious, documented decision taken after a
    verified-zero-NULL backfill (the zero-NULL probe is necessary but not sufficient): either
      (a) a targeted relaxation of BlockOnPossibleDataLoss for this one change — operationally a
          scripted change with a named, logged gate-relaxation, which cannot be expressed as a
          table definition — or
      (b) restructure it to ship across multiple releases (multiple PRs) so the running
          application keeps working while the change is in flight.
    A dev lead must review this: existing data is affected. Added scrutiny applies if the table
    feeds a change-data-capture stream, holds >1M rows, or is the first time this operation has
    run on the estate. This is the same-operation, different-seed proof (self-test COL-03 /
    COL-03B / COL-03C).

  - rename-attribute (ContactPhone -> MobileNumber): rename by editing the column name here.
    Without a refactorlog entry the delta is DROP ContactPhone + ADD MobileNumber — a rename with
    no refactorlog entry loses the column's data (every phone number). With the refactorlog entry
    it becomes sp_rename and the data survives. (self-test COL-08 / COL-08N.)

  - move-attribute SOURCE (STR-03): Region + the 1:1 AccountId link were added (2026-06-30) so
    Region can be moved Customer -> Account and proven total across the 1:1 join. A cross-table
    move is copy-then-drop, not an sp_rename — there is no refactorlog identity mapping for a
    move. temporal-convert (AUD-02) also converts this populated Customer in a scratch copy (no
    authored change — the greenfield-vs-convert contrast lives in skills/op/temporal-convert/).
    See skills/_index/multi-phase/ and skills/_index/identity-and-refactorlog/.

  HOW TO PROVE make-mandatory (the finding the run must confirm empirically):
    1. Edit `Email NVARCHAR(256) NULL` to `Email NVARCHAR(256) NOT NULL` below, rebuild, and run
       /Action:Script. In the delta the `IF EXISTS(SELECT TOP 1 1 FROM Customer)
       RAISERROR(...,16,127)` guard is placed above the `ALTER COLUMN ... NOT NULL` — the
       table-has-rows guard itself.
    2. Strict publish on the default (populated) seed -> Strict blocks it.
    3. Author the pre-deploy backfill (see Script.PreDeployment.sql), re-run the NULL probe
       (`SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL`) -> prove it returns 0.
    4. Re-run Strict -> it still blocks the change and the column stays nullable. This is the
       finding.
    5. Deliver the corrected verdict: on a populated table, backfill alone cannot pass the
       prod-strict gate; choose (a) a named gate-relaxation after proven-zero-NULL or
       (b) multi-phase, and prove the chosen path lands the NOT NULL.
    The empty-table leg (truncate Customer, or skip the seed, before publishing) is the clean
    in-place contrast: with no rows the guard's IF EXISTS is false and the ALTER lands as a
    single schema change, no data read or written.
    Do not report the old "backfill -> clean NOT NULL lands as a routine pre-deploy-script
    release" recipe — it is wrong and was disproven here.
*/

CREATE TABLE dbo.Customer
(
    Id              INT             IDENTITY(1,1) NOT NULL,
    Name            NVARCHAR(100)   NOT NULL,

    -- make-mandatory target. Default state is NULLABLE. Editing this to NOT NULL is the
    -- one-line change whose outcome the DATA decides — and on a populated table the guard is
    -- table-has-rows, so it blocks the change even after the NULLs are backfilled away.
    Email           NVARCHAR(256)   NULL,

    -- rename-attribute target. Rename this to MobileNumber by editing the name. Without a
    -- refactorlog entry that rename becomes a DROP + CREATE, and the column's data is lost.
    ContactPhone    NVARCHAR(40)    NULL,

    -- move-attribute SOURCE (STR-03). Region is the value moved FROM Customer TO Account across
    -- the 1:1 AccountId join. Seeded populated so the move has data to conserve. A cross-table
    -- move is copy-then-drop (no refactorlog identity) — see skills/_index/multi-phase/ and
    -- skills/_index/identity-and-refactorlog/.
    Region          NVARCHAR(50)    NULL,

    -- 1:1 link to dbo.Account (nullable so it does NOT disturb the existing make-mandatory /
    -- rename seed on rows without an Account). Makes the STR-03 move provably 1:1 and gives
    -- split/merge a real join column. Left as a plain column here — declaring the FK to
    -- Account(Id) is itself a create-fk proof (see skills/op/create-fk-clean/).
    AccountId       INT             NULL,

    CONSTRAINT PK_Customer PRIMARY KEY CLUSTERED (Id)
);
GO
