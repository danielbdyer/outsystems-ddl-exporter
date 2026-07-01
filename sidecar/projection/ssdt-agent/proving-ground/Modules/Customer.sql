/*
  dbo.Customer — the make-mandatory and rename-attribute proving table.

  This is a CREATE-only schema item (Build Action = Build). You prove a change by editing THIS
  destination, never by writing ALTER. "Edit the CREATE, never write ALTER."

  PARALLEL EXECUTORS — READ FIRST: if you are one of many subagents proving a case at once, do
  NOT edit this authored file in place. Copy the whole proving-ground tree to a private scratch
  dir and publish to a UNIQUE database (/TargetDatabaseName:PG_<testId>_<rand>) per the protocol
  in `../self-test/PROTOCOL.md`. The authored tree and the default DB are shared and read-only;
  the scratch copy + unique DB are how a hundred provers run without colliding.

  WHAT THIS TABLE EXERCISES
  -------------------------
  - make-mandatory (Email NULL -> NOT NULL): the seed plants Email NULL rows. The mechanism is
    decided ENTIRELY by whether the table HAS ROWS — not by whether the Email column has NULLs.
    SSDT's BlockOnPossibleDataLoss guard for this change is generated as
        IF EXISTS (SELECT TOP 1 1 FROM dbo.Customer) RAISERROR(...,16,127)
    placed BEFORE the ALTER COLUMN. That is TABLE-HAS-ROWS, not column-has-NULLs. Consequences:
      * EMPTY table  -> the IF EXISTS is false, the RAISERROR never fires, the ALTER COLUMN
        NOT NULL lands. Clean Mechanism 1, Pure Declarative, single-phase, Tier 1.
      * POPULATED table (NULLs present OR zero NULLs — it does NOT matter which) -> Strict
        ALWAYS vetoes. A pre-deploy backfill that clears every NULL does NOT clear the veto;
        the column stays nullable. PROVEN on the proving ground: backfilled to 0 NULL emails,
        Strict STILL vetoed. So on a populated table this is NOT a clean Mechanism 3.
    The honest remedy on a populated table is a CONSCIOUS, DOCUMENTED decision taken AFTER a
    verified-zero-NULL backfill (the zero-NULL probe is necessary but not sufficient): either
      (a) a targeted relaxation of BlockOnPossibleDataLoss for this ONE change — operationally
          Mechanism 4 / Script-Only with a named, logged gate-relaxation — or
      (b) restructure as Mechanism 5, Multi-Phase, multi-PR.
    Tier 2 baseline; +1 for CDC / >1M rows / first-time. This is the headline
    same-op × different-seed proof (self-test COL-03 / COL-03B / COL-03C).

  - rename-attribute (ContactPhone -> MobileNumber): rename by editing the column name here.
    WITHOUT a refactorlog entry the delta is DROP ContactPhone + ADD MobileNumber = the Naked
    Rename catastrophe (every phone number lost). WITH the refactorlog entry it becomes
    sp_rename and the data survives. (self-test COL-08 / COL-08N.)

  - move-attribute SOURCE (STR-03): Region + the 1:1 AccountId link were ADDED (2026-06-30) so
    Region can be moved Customer -> Account and proven total across the 1:1 join. A cross-table
    move is copy-then-drop, NOT an sp_rename — there is no refactorlog identity mapping for a
    move. temporal-convert (AUD-02) also converts THIS populated Customer in a SCRATCH copy (no
    authored change — greenfield-vs-convert contrast lives in skills/op/temporal-convert/).
    See skills/_index/multi-phase/ and skills/_index/identity-and-refactorlog/.

  HOW TO PROVE make-mandatory (the showcase finding the run must EMPIRICALLY confirm):
    1. Edit `Email NVARCHAR(256) NULL` to `Email NVARCHAR(256) NOT NULL` below, rebuild, and run
       /Action:Script. In the delta you will SEE the `IF EXISTS(SELECT TOP 1 1 FROM Customer)
       RAISERROR(...,16,127)` guard placed ABOVE the `ALTER COLUMN ... NOT NULL` — that is the
       table-has-rows guard in the flesh.
    2. Strict publish on the default (populated) seed -> Strict VETOES.
    3. Author the pre-deploy backfill (see Script.PreDeployment.sql), re-run the NULL probe
       (`SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL`) -> prove it returns 0.
    4. Re-run Strict -> it STILL VETOES and the column STAYS NULLABLE. This is the finding.
    5. Deliver the corrected verdict: on a populated table, backfill alone cannot pass the
       prod-strict gate; choose (a) named gate-relaxation after proven-zero-NULL or
       (b) multi-phase, and PROVE the chosen path lands the NOT NULL.
    The EMPTY-table leg (truncate Customer, or skip the seed, before publishing) is the clean
    Mechanism 1 contrast: with no rows the guard's IF EXISTS is false and the ALTER lands.
    Do NOT report the old "backfill -> clean NOT NULL = Mechanism 3" recipe — it is WRONG and
    was disproven here.
*/

CREATE TABLE dbo.Customer
(
    Id              INT             IDENTITY(1,1) NOT NULL,
    Name            NVARCHAR(100)   NOT NULL,

    -- make-mandatory target. Default state is NULLABLE. Editing this to NOT NULL is the
    -- one-line change whose mechanism the DATA decides — and on a populated table the guard is
    -- table-has-rows, so it vetoes even after the NULLs are backfilled away.
    Email           NVARCHAR(256)   NULL,

    -- rename-attribute target. Rename this to MobileNumber by editing the name. Without a
    -- refactorlog entry that rename is a DROP+CREATE = Naked Rename data loss.
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
