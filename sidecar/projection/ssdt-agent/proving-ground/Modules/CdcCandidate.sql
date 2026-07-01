/*
  dbo.CdcCandidate — the CDC proving table. ISOLATED-DB ONLY.

  ============================================================================================
  SURVIVAL RULE 1 / PROTOCOL §8 — MANDATORY ISOLATION. READ BEFORE ENABLING CDC.
  --------------------------------------------------------------------------------------------
  sp_cdc_enable_db flips CDC state INSTANCE-WIDE. NEVER enable CDC on the shared warm container
  (`projection-mssql-warm`) or on the shared `ProvingGround` DB. Any CDC proof (AUD-04/05/06,
  AUD-07N, TRAP-01N) runs ONLY inside a UNIQUE, per-executor database
  (/TargetDatabaseName:PG_<testId>_<rand>) that you create and drop yourself. This mirrors the F#
  test suite's IsolatedContainerFixture rule (CLAUDE.md survival rule 1) and self-test PROTOCOL §8.
  A CDC proof on the shared DB corrupts every other executor and the warm container.
  ============================================================================================

  CREATE-only schema item. A plain table whose PURPOSE is CDC proving. The point of CDC in this
  proving ground is to demonstrate that CDC is NOT in the dacpac model — the declarative attempt
  to "turn on CDC" is SILENTLY IGNORED (prove the empty delta), so CDC is Script-Only, +1 Tier
  forever, and its failure mode is SILENCE (a missing column in the feed, no error). See
  skills/_index/cdc/ for the standing tax and skills/op/enable-cdc/ for the op.

  WHAT THIS TABLE UNLOCKS (all ISOLATED-DB only)
  ----------------------------------------------
  - enable-cdc (AUD-04): sys.sp_cdc_enable_table on THIS table inside the unique DB; prove the
    declarative attempt produces an EMPTY delta. See skills/op/enable-cdc/.
  - recreate-capture-instance (AUD-05): ADD a column (e.g. AMOUNT) AFTER enabling capture, then
    prove the existing capture instance does NOT see it (silence) until a dual-instance
    v1/v2 recreate. See skills/op/recreate-capture-instance/ and skills/_index/cdc/.
  - change-tracking (AUD-06): the lighter sibling (which rows changed, not the column-level feed).
    See skills/op/change-tracking/.
  - drop-CDC-tracked-table (AUD-07N): dropping a CDC-tracked table is a negative — the capture
    instance and cleanup jobs must be torn down first.
  - nullable-add-to-CDC-table (TRAP-01N): adding even a nullable column to a CDC table is the +1
    CDC-Surprise face — the feed silently omits the new column until capture is recreated.

  Notes NVARCHAR(200) NULL gives AUD-05 / TRAP-01N a place to add a second column too.

  PARALLEL EXECUTORS — READ FIRST: copy the tree, publish to a UNIQUE database, and enable CDC
  ONLY in that unique DB. See `../self-test/PROTOCOL.md` §8.

  UNLOCKS self-test ids: AUD-04 (enable-cdc), AUD-05 (recreate-capture-instance),
  AUD-06 (change-tracking), AUD-07N (drop-CDC-tracked-table), TRAP-01N (nullable-add-to-CDC-table).
*/

CREATE TABLE dbo.CdcCandidate
(
    Id      INT             IDENTITY(1,1) NOT NULL,
    Name    NVARCHAR(100)   NOT NULL,
    Notes   NVARCHAR(200)   NULL,

    CONSTRAINT PK_CdcCandidate PRIMARY KEY CLUSTERED (Id)
);
GO
