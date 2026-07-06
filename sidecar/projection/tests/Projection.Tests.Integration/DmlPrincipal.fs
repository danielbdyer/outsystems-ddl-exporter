namespace Projection.Tests

open Microsoft.Data.SqlClient
open Projection.Pipeline

/// The ONE shared managed-grant principal helper (2026-07-06, promoted from
/// `ReverseLegFixtures.createDmlPrincipal` so the reverse-leg canaries, the
/// peer managed-grant suite, and future consumers share one login-naming /
/// grant-string / cleanup implementation).
///
/// The managed-cloud profile is reproduced with EXPLICIT database-scope
/// grants, never `db_datareader`/`db_datawriter`: fixed-role rights do NOT
/// surface in `sys.fn_my_permissions(NULL,'DATABASE')` — the probe
/// `Preflight.captureGrantEvidence` reads — so a role-based mock would
/// false-trip `transfer.insufficientGrant` (exit 7) on a sink that could
/// take the writes (and `db_datawriter` carries no SELECT, which the
/// reconcile leg and capture staging need). What is deliberately ABSENT
/// (absence, not DENY — the faithful cloud reproduction): ALTER, CREATE
/// TABLE, REFERENCES, EXECUTE, VIEW DEFINITION, CONTROL. Consequences the
/// engine relies on: `SET IDENTITY_INSERT` fails (1088-class), `CREATE
/// TABLE` fails (262), `ALTER TABLE … CHECK CONSTRAINT` fails (descends to
/// the named FK-trust tolerance), while `#temp` creation and `MERGE …
/// OUTPUT` succeed.
[<RequireQualifiedAccess>]
module DmlPrincipal =

    [<Literal>]
    let ManagedGrants = "SELECT, INSERT, UPDATE, DELETE"

    /// Create a login + database user carrying `grants` at database scope on
    /// the admin connection's database; returns `(login, connection string
    /// re-pointed at the restricted login)`. Caller drops via `dropLogin`.
    let create
        (admin: SqlConnection)
        (adminConnStr: string)
        (grants: string)
        : System.Threading.Tasks.Task<string * string> =
        task {
            let login = "mockdml_" + System.Guid.NewGuid().ToString("N").Substring(0, 8)
            let password = "Mock!dml#" + System.Guid.NewGuid().ToString("N").Substring(0, 12)
            do! Deploy.executeBatch admin
                    (sprintf "CREATE LOGIN [%s] WITH PASSWORD = N'%s'; CREATE USER [%s] FOR LOGIN [%s]; GRANT %s TO [%s];"
                        login password login login grants login)
            let builder = SqlConnectionStringBuilder(adminConnStr)
            builder.UserID <- login
            builder.Password <- password
            return login, builder.ConnectionString
        }

    /// The managed-cloud profile (`ManagedGrants` at database scope).
    let createManaged (admin: SqlConnection) (adminConnStr: string) =
        create admin adminConnStr ManagedGrants

    /// Best-effort login cleanup (the per-run database drops with the
    /// ephemeral fixture; the LOGIN is instance-scoped and must go too).
    let dropLogin (admin: SqlConnection) (login: string) : unit =
        try
            (Deploy.executeBatch admin (sprintf "DROP LOGIN [%s];" login)).GetAwaiter().GetResult()
        with _ -> ()

    /// The database-scope permission names `fn_my_permissions` reports for
    /// the CURRENT principal — the exact evidence `Preflight.captureGrantEvidence`
    /// reads. Run over a connection opened AS the principal under test.
    let selfPermissions (cnn: SqlConnection) : System.Threading.Tasks.Task<Set<string>> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- "SELECT permission_name FROM sys.fn_my_permissions(NULL, 'DATABASE');"
            let acc = System.Collections.Generic.List<string>()
            use! reader = cmd.ExecuteReaderAsync()
            let mutable go = true
            while go do
                let! more = reader.ReadAsync()
                if more then acc.Add(reader.GetString 0) else go <- false
            return Set.ofSeq acc
        }

    /// True when the exception is the SQL permission-denied class (229 /
    /// 262 / 1088 / 297 / 4902) — the classifier the permission-matrix
    /// assertions key on, so a test asserts WHICH capability was refused
    /// rather than "some SqlException happened".
    let isPermissionDenied (ex: exn) : bool =
        match ex with
        | :? SqlException as se ->
            se.Errors
            |> Seq.cast<SqlError>
            |> Seq.exists (fun e -> List.contains e.Number [ 229; 262; 297; 1088; 4902 ])
            || se.Message.ToLowerInvariant().Contains "permission"
        | _ -> false
