# 30. Glossary

---

| Term | Definition | OutSystems Equivalent |
|------|------------|----------------------|
| **ALTER TABLE** | SQL command to modify table structure | (Hidden — happens on Publish) |
| **Attribute** | A column in a table | Entity Attribute |
| **Backup** | Point-in-time copy of database | (Platform managed) |
| **BlockOnPossibleDataLoss** | SSDT setting that prevents destructive deployments | (Platform guardrails) |
| **Capture Instance** | CDC's record of a table's schema at a point in time | (N/A) |
| **CASCADE** | Automatic propagation of delete/update to child records | Delete Rule = Delete |
| **CDC (Change Data Capture)** | SQL Server feature tracking row-level changes | (N/A) |
| **CHECK constraint** | Rule validating column values | (N/A — enforced in app logic) |
| **Clustered index** | Index defining physical row order | (Hidden) |
| **Column** | A field in a table | Entity Attribute |
| **Computed column** | Column whose value is calculated from other columns | Calculated Attribute |
| **Constraint** | Rule enforced by the database | (Partially — some in platform) |
| **dacpac** | Compiled SSDT project; portable schema package | (N/A) |
| **DDL** | Data Definition Language (CREATE, ALTER, DROP) | (Hidden) |
| **Declarative** | Describing desired end state, not steps to get there | How Service Studio works |
| **DEFAULT constraint** | Value assigned when none provided | Default Value |
| **DML** | Data Manipulation Language (INSERT, UPDATE, DELETE) | (Hidden) |
| **Entity** | A table (OutSystems terminology) | Entity |
| **External Entity** | OutSystems entity pointing to external table | External Entity |
| **FK (Foreign Key)** | Constraint linking to parent table | Reference Attribute |
| **IDENTITY** | Auto-incrementing column property | Auto Number |
| **Idempotent** | Safe to run multiple times with same result | (N/A) |
| **Index** | Structure speeding up queries | Index (in Entity properties) |
| **Integration Studio** | Tool for managing external integrations | Integration Studio |
| **Is Mandatory** | OutSystems term for required field | NOT NULL |
| **JOIN** | Query combining rows from multiple tables | (Query equivalent) |
| **Lookup table** | Reference/code table with fixed values | Static Entity |
| **Migration** | Script moving data between states | (N/A) |
| **Multi-phase** | Change requiring multiple sequential releases | (N/A — Publish is atomic) |
| **NOT NULL** | Column requires a value | Is Mandatory = Yes |
| **NULL** | Absence of value | Is Mandatory = No |
| **Orphan data** | Child records with no parent | (Platform prevents) |
| **PK (Primary Key)** | Unique identifier for a row | Entity Identifier |
| **Post-deployment script** | SQL running after schema changes | (N/A) |
| **Pre-deployment script** | SQL running before schema changes | (N/A) |
| **Publish** | Deploy changes to database | Publish |
| **Publish profile** | Deployment configuration file | (N/A) |
| **Refactorlog** | XML tracking renames | (N/A — platform handles) |
| **Row** | Single record in a table | Entity Record |
| **Schema** | Namespace for database objects (dbo, audit) | (N/A — all in same space) |
| **Service Studio** | OutSystems IDE for application development | Service Studio |
| **sp_rename** | SQL Server command to rename objects | (Hidden) |
| **SSDT** | SQL Server Data Tools | (N/A) |
| **SSMS** | SQL Server Management Studio | Service Center (sort of) |
| **Stored procedure** | Named SQL routine | Server Action (sort of) |
| **Table** | Structure storing rows of data | Entity |
| **Temporal table** | System-versioned table tracking history | (N/A) |
| **Tier** | Risk classification for changes (1-4) | (N/A) |
| **Trigger** | Code executing on data events | (N/A) |
| **Unique constraint** | Enforces distinct values in column | (N/A — enforced in app) |
| **View** | Saved query appearing as table | (N/A) |
| **WITH NOCHECK** | Add constraint without validating existing data | (N/A) |

---

