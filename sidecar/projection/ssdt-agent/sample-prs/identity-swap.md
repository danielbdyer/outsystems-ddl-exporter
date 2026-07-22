# Order: stop auto-numbering the Id (a table rebuild — every foreign key dropped and recreated)

**In OutSystems** — You turn *Auto Number = No* on the `Order` Entity's `Id`, because you want to assign Order Ids yourself instead of letting the database generate them.
**In SSDT** — `[Id] INT IDENTITY(1,1)` becomes `[Id] INT NOT NULL` in `Tables/dbo.Order.sql`. You delete two words; the publish engine turns that one-line edit into a **full table rebuild**.

## Summary

You ask for a one-word change — turn Auto Number off — and the size of the `.sql` edit tells you nothing
about the size of the deploy. **A column's IDENTITY property is fixed when the column is created and cannot
be `ALTER`ed off in place.** So SSDT rebuilds the whole `Order` table behind the scenes: it builds a shadow
copy of the table *without* Auto Number, copies every row across with its `Id` preserved, drops the original,
renames the shadow into place, and **drops and recreates every foreign key that touches `Order`** — the
incoming one from `OrderLine`, *and* `Order`'s own two outgoing keys to `Customer` and `Status` — around the
rebuild. This is the most dangerous kind of "one-line edit" in the catalogue: if the copy did not preserve
the keys, every `Id` would be re-minted and every `OrderLine` would point at the wrong order.

This was proven objectively against a **Twin** — a disposable SQL Server 2022 database published from this
estate and filled with real-shaped synthetic data — under a **production-faithful** publish
(`BlockOnPossibleDataLoss = true`, `GenerateSmartDefaults = false`, `DropObjectsNotInSource = false`), the
deployment a real environment runs. **The discovered outcome: the production gate *performed* the rebuild.**
Because a rebuild *moves* every row into the shadow table rather than dropping any, it is data-preserving, so
`BlockOnPossibleDataLoss` does not block it — the publish applied in a single atomic transaction, every `Id`
value came through unchanged, and every `OrderLine` still resolved to a real `Order`. No work item was
provided with the request; attach one before merge so the record is traceable.

## Review & release

- **A dev lead must review this.** The whole `Order` table is rebuilt (every row copied into a shadow table)
  and **every foreign key touching it is dropped and recreated** — not only the incoming key from `OrderLine`
  but `Order`'s own outgoing keys to `Customer` and `Status`.
- **The schema change is a single production publish** (proven below), applied atomically inside one
  `SERIALIZABLE` transaction with `XACT_ABORT ON`: the shadow-table swap and the foreign-key drop/recreate are
  bracketed *inside that one deploy*, so a failure rolls the whole thing back and leaves `Order` exactly as it
  was. What sequences across the rollout is the **application-side Id handling** (below), not the schema.
- **The `.sql` edit is deceptive** — deleting `IDENTITY(1,1)` generates a full shadow-table swap. Preview the
  delta and confirm it is a rebuild (a `tmp_ms_xx_Order` shadow table and `sp_rename`) before promising
  anything; the danger, not the line count, drives the review need.
- Added scrutiny: this is the first time this rebuild is proven on the estate; at production row counts the
  row-by-row copy is the expensive part and may block writes or run long — schedule a window.

## Changes

| File | Change |
|---|---|
| `Tables/dbo.Order.sql` | Removes `IDENTITY(1,1)` from `[Id]` — `[Id] INT IDENTITY(1,1) NOT NULL` becomes `[Id] INT NOT NULL`. Every other column and constraint is unchanged. |

No renames (the refactorlog is unchanged). No column is dropped or retyped — but note the deploy is **not** an
`ALTER`: SSDT cannot alter a column out of IDENTITY, so the generated delta rebuilds the entire table and
re-creates `PK_Order`, `FK_Order_Customer`, `FK_Order_Status`, and `FK_OrderLine_Order`.

## Application cutover — the Id handling (the OutSystems half)

The schema rebuild is provable on a disposable copy; the application change is not, and it is part of the same
change. With Auto Number **off**, the database no longer generates `Order` Ids — the application must **supply
the `Id` itself** on every insert, or the insert fails. This is the exact mirror of turning Auto Number *on*
(where any insert that supplies an explicit Id fails unless it wraps the insert in `SET IDENTITY_INSERT`). The
application release that starts supplying Ids must be sequenced with the schema release; that every insert path
now provides an `Id` is confirmed by the application owner, not here.

## Deployment evidence — objective proof, production-faithful publish, live Twin (SQL Server 2022), 2026-07-22, Microsoft.SqlServer.DacFx 162.5.57

The proof is a green integration test that publishes this estate to a live Twin, materializes real-shaped
`Order` data (25 rows, `Id` auto-numbered 1–25) with `OrderLine` children pointing at it, **reads the generated
deploy delta**, then applies the IDENTITY removal under the production-faithful posture and consumes the data
to assert the outcome. The key set and the whole row set are each hashed (an order-sensitive `SHA2_256` over
the `FOR XML RAW` projection) so a re-minted or lost key would shift the digest. DacFx is the same publish
engine `sqlpackage` wraps.

**Test:** `Twin.Tests.Integration.SamplePrRebuildTests+SamplePrRebuildTests.identity-swap: removing IDENTITY from Order.Id is a table rebuild — the true outcome under the production gate, keys and incoming FK preserved`

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 51 s - Twin.Tests.Integration.dll (net9.0)
```

**Fact 1 — the one-line edit generates a shadow-table rebuild that drops and recreates every foreign key.**
Scripting the delta (without executing it) under the production posture shows the change is not an `ALTER` at
all — it is a full rebuild through a `tmp_ms_xx_Order` shadow table. Verbatim from the generated deploy script,
the load-bearing part:

```sql
ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Customer];
ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Status];
ALTER TABLE [dbo].[OrderLine] DROP CONSTRAINT [FK_OrderLine_Order];
-- Starting rebuilding table [dbo].[Order]...
BEGIN TRANSACTION;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
SET XACT_ABORT ON;
CREATE TABLE [dbo].[tmp_ms_xx_Order] (
    [Id]         INT             NOT NULL,
    [CustomerId] INT             NOT NULL,
    [StatusId]   INT             NOT NULL,
    [Channel]    NVARCHAR (20)   NOT NULL,
    [Total]      DECIMAL (18, 2) NOT NULL,
    [PlacedOn]   DATETIME2 (7)   NOT NULL,
    CONSTRAINT [tmp_ms_xx_constraint_PK_Order1] PRIMARY KEY CLUSTERED ([Id] ASC)
);
IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Order])
    BEGIN
        INSERT INTO [dbo].[tmp_ms_xx_Order] ([Id], [CustomerId], [StatusId], [Channel], [Total], [PlacedOn])
        SELECT [Id], [CustomerId], [StatusId], [Channel], [Total], [PlacedOn]
        FROM [dbo].[Order] ORDER BY [Id] ASC;
    END
DROP TABLE [dbo].[Order];
EXECUTE sp_rename N'[dbo].[tmp_ms_xx_Order]', N'Order';
EXECUTE sp_rename N'[dbo].[tmp_ms_xx_constraint_PK_Order1]', N'PK_Order', N'OBJECT';
COMMIT TRANSACTION;
-- ... then every foreign key is recreated and re-validated:
ALTER TABLE [dbo].[Order]     WITH NOCHECK ADD CONSTRAINT [FK_Order_Customer]   FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]);
ALTER TABLE [dbo].[Order]     WITH NOCHECK ADD CONSTRAINT [FK_Order_Status]     FOREIGN KEY ([StatusId])   REFERENCES [dbo].[Status] ([Id]);
ALTER TABLE [dbo].[OrderLine] WITH NOCHECK ADD CONSTRAINT [FK_OrderLine_Order]  FOREIGN KEY ([OrderId])    REFERENCES [dbo].[Order] ([Id]);
ALTER TABLE [dbo].[Order]     WITH CHECK CHECK CONSTRAINT [FK_Order_Customer];
ALTER TABLE [dbo].[Order]     WITH CHECK CHECK CONSTRAINT [FK_Order_Status];
ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order];
```

The copy is `INSERT ... SELECT ... ORDER BY [Id]` into a shadow column that is a plain `INT`, so the `Id` values
are carried across directly and **no `SET IDENTITY_INSERT` is needed** — that step is the mirror concern, load-
bearing only when Auto Number is turned *on* and keys must be forced into a *new* identity column (the
`temporal-convert` proof in this same run shows DacFx emitting exactly that `SET IDENTITY_INSERT ... ON` when it
rebuilds an identity table). The three `WITH CHECK CHECK CONSTRAINT` statements re-validate the recreated keys,
so they land **trusted**.

**Fact 2 — the production gate performed the rebuild; every key was preserved and every child still resolves.**
`Order` held **25 rows**, `Id` was `IDENTITY` (`IsIdentity = 1`), `MAX(Id) = 25`, and `OrderLine` held 25 rows
with **0 orphans**. The production-faithful publish of the IDENTITY removal was **accepted** — the rebuild is
data-preserving, so `BlockOnPossibleDataLoss` did not block it. After the apply: `Id` is a plain column
(`IsIdentity = 0`, off `sys.identity_columns`), the row count and key range are unchanged, the `Id` digest and
the whole-row digest are **byte-for-byte identical to before**, and every `OrderLine` still points at a real
`Order` (0 orphans) through a **trusted** recreated key. Verbatim from the run:

```
baseline: Order.Id IsIdentity=1 (1 = IDENTITY), sys.identity_columns=1, Order rows=25, MAX(Id)=25, OrderLine rows=25, OrderLine orphans=0, FK_OrderLine_Order exists=1
  Id digest=2E21CF06E0C13F9DBEF6F310553F578AEBD6D58770ED647391741523DD0479E0; whole-row digest=FE097CFBDCAA76494E86B10CFB63E6C2AC4E228DC302E89BB01C9FC526B4171E
production publish (BlockOnPossibleDataLoss=true) remove IDENTITY from [dbo].[Order].[Id]: APPLIED (Ok) — the gate performed the data-preserving rebuild
phase 1 result (declarative (production publish performed the rebuild)):
  Order.Id IsIdentity=0 (0 = plain column, no longer auto-numbered), sys.identity_columns=0
  Order rows=25 (was 25), MAX(Id)=25 (was 25), OrderLine rows=25 (was 25)
  keys preserved: Id digest=2E21CF06E0C13F9DBEF6F310553F578AEBD6D58770ED647391741523DD0479E0 (match=true); whole-row digest=FE097CFBDCAA76494E86B10CFB63E6C2AC4E228DC302E89BB01C9FC526B4171E (match=true)
  incoming reference intact: OrderLine orphans=0 (0 = every child still points at a real parent), FK_OrderLine_Order exists=1 (is_not_trusted=0)
```

The `Id` digest matching **byte-for-byte** is the load-bearing proof: the shadow-table copy preserved every key
value, so `OrderLine`'s references still resolve. Confirmed on `mcr.microsoft.com/mssql/server:2022-latest`.

## Verification — run in each environment after deployment

```sql
-- expect is_identity = 0: the Id column is no longer database-generated (Auto Number off)
SELECT name, is_identity FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.[Order]') AND name = 'Id';

-- expect 0 rows: every OrderLine still points at a real Order (no key was re-minted by the rebuild)
SELECT ol.Id FROM dbo.OrderLine ol
LEFT JOIN dbo.[Order] o ON o.Id = ol.OrderId WHERE o.Id IS NULL;

-- expect is_not_trusted = 0 for each recreated key: the rebuild re-validated them (WITH CHECK)
SELECT name, is_not_trusted FROM sys.foreign_keys
WHERE name IN ('FK_Order_Customer', 'FK_Order_Status', 'FK_OrderLine_Order');
```

## Rollback

Backing this out is **itself a table rebuild in the other direction** — re-adding `IDENTITY(1,1)` to `[Id]` is
the same shadow-table swap, except the reverse direction *does* need `SET IDENTITY_INSERT ON` on the shadow to
force the existing `Id` values into the new identity column (without it the keys would be re-minted from 1 and
every `OrderLine` would point at the wrong order), plus the same drop-and-recreate of every foreign key. It is
not a single `DROP CONSTRAINT` and it is not auto-reversible; it must be previewed and proven the same way. The
forward rebuild preserved every `Id` value, so there is no data-value change to undo — only the physical
rebuild to repeat. Backing the change out was not exercised.

## Not verified

- **Application impact.** With Auto Number off, the database no longer generates `Order` Ids: any insert that
  does not supply an `Id` fails. That every insert path now provides an `Id` is confirmed by the application
  owner, not here.
- **Other environments.** The rebuild and key preservation were proven on a disposable copy of Dev only; Test,
  UAT, and Prod hold row counts and `OrderLine` data this copy cannot see. Run the verification queries before
  promotion in each.
- **Production scale and timing.** The row-by-row copy is the expensive part of the rebuild; at production row
  counts it may block writes or run long, which the 25-row copy does not exercise. Schedule a window.
- **Reversibility.** Only the forward rebuild (removing IDENTITY) is proven; the inverse rebuild that re-adds
  IDENTITY — and the `SET IDENTITY_INSERT` step it requires to preserve keys — is not exercised here.
