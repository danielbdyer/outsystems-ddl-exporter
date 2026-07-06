namespace Projection.Tests

open Microsoft.Data.SqlClient
open System.Threading.Tasks
open Projection.Pipeline

/// A MOCK OUTSYSTEMS ENVIRONMENT (2026-07-06, the phase-2 program): one
/// ephemeral database carrying (a) the OSSYS metamodel + espace-prefixed
/// `OSUSR_*` physical tables (the edge-case estate, espace-parameterized via
/// `OssysSeedBuilder`), optionally (b) test-local row batches, and
/// optionally (c) a MANAGED-GRANT principal (`DmlPrincipal.createManaged`)
/// whose connection string is what the ENGINE drives — so a test proves the
/// transfer under the real cloud permission envelope, not as admin.
///
/// CPS form (the `WithEphemeralDatabase` idiom): the body receives the full
/// surface; login + database cleanup happen in `finally` regardless of
/// outcome. Two environments compose via `withMockEnvPair` — the peer shape.
[<RequireQualifiedAccess>]
module MockOutSystemsEnv =

    type GrantProfile =
        /// The fixture's admin connection drives the engine (the pre-phase-2
        /// posture — identity/mechanics tests that deliberately ignore grants).
        | AdminFullRights
        /// A `DmlPrincipal.ManagedGrants` login drives the engine — the
        /// managed-cloud envelope (no ALTER / CREATE TABLE / IDENTITY_INSERT).
        | ManagedDml

    type MockEnv =
        { /// Admin-scope connection (setup, DENY injection, assertions).
          Admin        : SqlConnection
          AdminConnStr : string
          /// What the ENGINE connects as (the restricted login under
          /// `ManagedDml`; the admin string under `AdminFullRights`).
          EngineConnStr : string
          Login        : string option
          EspaceKey    : string }

    /// One mock cell: deploy the espace-shifted edge-case estate + the given
    /// row batches, mint the grant profile, run the body, clean up.
    let withMockEnv
        (fixture: EphemeralContainerFixture)
        (label: string)
        (espaceKey: string)
        (rowBatches: string list)
        (grant: GrantProfile)
        (body: MockEnv -> Task<'a>)
        : Task<'a> =
        fixture.WithEphemeralDatabase label (fun admin adminConnStr ->
            task {
                let seed =
                    Projection.Adapters.OssysSql.MetadataExtractionSql.readEdgeCaseSeed ()
                    |> (if espaceKey = "" then OssysSeedBuilder.sameEspace else OssysSeedBuilder.withEspaceKey espaceKey)
                do! Deploy.executeBatch admin seed
                for batch in rowBatches do
                    do! Deploy.executeBatch admin batch
                match grant with
                | AdminFullRights ->
                    return! body { Admin = admin; AdminConnStr = adminConnStr; EngineConnStr = adminConnStr; Login = None; EspaceKey = espaceKey }
                | ManagedDml ->
                    let! login, restricted = DmlPrincipal.createManaged admin adminConnStr
                    try
                        return! body { Admin = admin; AdminConnStr = adminConnStr; EngineConnStr = restricted; Login = Some login; EspaceKey = espaceKey }
                    finally
                        DmlPrincipal.dropLogin admin login
            })

    /// Two mock cells in one test — the peer (A→A) shape. Nested lifecycles;
    /// the sink drops first (inner), then the source.
    let withMockEnvPair
        (fixture: EphemeralContainerFixture)
        (label: string)
        (sourceKey: string) (sourceRows: string list) (sourceGrant: GrantProfile)
        (sinkKey: string)   (sinkRows: string list)   (sinkGrant: GrantProfile)
        (body: MockEnv -> MockEnv -> Task<'a>)
        : Task<'a> =
        withMockEnv fixture (label + "Src") sourceKey sourceRows sourceGrant (fun src ->
            withMockEnv fixture (label + "Snk") sinkKey sinkRows sinkGrant (fun snk ->
                body src snk))
