using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.Data.SqlClient;

namespace DbChange.Io;

/// <summary>
/// A live database, read whole and read only (V3_MILESTONES.md §2.2, WP 1.4), and the one adapter to SQL Server and SqlClient: an
/// argument read as a target (kernel/Target.cs); EnvironmentDatabase, the database of an environment dbchange/environments.json names, and
/// Copy, a database io/LocalServer made, which alone publishes (§2.1 rule 3); Query, the one path for the statements dbchange sends itself;
/// Database.ErrorOf, the one boundary every SqlClient or DacFx failure passes through, reading SQL Server's numbers once; Reach, what
/// this identity may read there, asked before anything builds; and Measure, which runs an aggregate query its closed allowlist admits,
/// every answer an integer. io/DacFx reads a database and plans against it. A resolved connection is never printed, logged or put in an
/// error, and a named environment's SQL Server messages are withheld, since they can quote a row (§18). Objects are keyed by kernel/Name,
/// compared ordinally with case, so two databases whose collations fold case differently read the same schema alike; dbchange sets no
/// collation and no SET option of its own, and DacFx reads each database's own. An error's code names what went wrong; cli/Contract.cs maps
/// its category to the exit.
/// </summary>
public static partial class SqlServer
{
    /// <summary>A login, a database or a permission refused: the identity cannot read the target.</summary>
    private static readonly HashSet<int> Denials = [18456, 18452, 18470, 18486, 18487, 18488, 4060, 916, 229, 230, 262,
        297, 300, 15247, 40532];

    /// <summary>No answer: the network, the instance or a timeout.</summary>
    private static readonly HashSet<int> Silences = [-2, -1, 2, 20, 26, 35, 40, 53, 64, 121, 232, 233, 258, 1225, 10053, 10054, 10060,
        10061, 11001, 11004, 17142, 18401, 40613];

    /// <summary>
    /// A statement SQL Server refused on an open connection: its number, its message where a copy's may be kept, and whether the command
    /// ran past its timeout.
    /// </summary>
    internal sealed record StatementFailure(int Number, string? Message, bool TimedOut);

    /// <summary>An exception and each exception inside it, outermost first.</summary>
    private static IEnumerable<Exception> Chain(Exception failure)
    {
        for (var x = failure; x is not null; x = x.InnerException)
        {
            yield return x;
        }
    }

    /// <summary>Whether a failure carries a SqlException, however deep: what Database.ErrorOf reads by its number.</summary>
    private static bool Carries(Exception failure) => Chain(failure).Any(x => x is SqlException);

    /// <summary>
    /// The target an argument names (kernel/Target.cs), read for the argument <paramref name="subject"/>; a literal connection string,
    /// which only SqlClient's grammar tells from a target, is connection.literal at exit 6, and no part of the argument is quoted.
    /// </summary>
    public static Result<Target> Target(string text, string subject) =>
        ConnectionString.IsConnection(text)
            ? new Error("connection.literal", subject + " is a literal connection string, which no argument carries.",
            "Name the target as env:NAME, an environment whose connection dbchange/environments.json gives as env:VARIABLE or file:path.")
        : Kernel.Target.Parse(text, subject);

    /// <summary>
    /// A live database the tool reads: a named environment's or a copy's. Its resolved connection stays inside io, and it prints as its
    /// target.
    /// </summary>
    public abstract class Database
    {
        private protected Database(Target target, string connection) => (Target, Connection) = (target, connection);

        /// <summary>The target that names it: env:dev, copy:dbchange_host_4242_0a1b2c3d.</summary>
        public Target Target { get; }

        internal string Connection { get; }

        /// <summary>The database's name, as DacFx plans against it.</summary>
        internal string Catalog => ConnectionString.CatalogOf(Connection);

        /// <summary>
        /// Whether SQL Server's messages about a failed statement are withheld: a named environment's rows may be real (VALUES.md X2).
        /// </summary>
        internal abstract bool Withheld { get; }

        /// <summary>
        /// What a SQL Server error against this database becomes, by its number (M1 exit 7, X2): a login, a database or a permission
        /// refused is server.denied; no answer is server.unreachable; anything else is server.failed. SQL Server's message is kept only for
        /// a copy's failed statement, a copy's rows being generated; one about a connection can name the server or the login, and is
        /// withheld.
        /// </summary>
        public Error ErrorOf(int number, string message) => ErrorOf(number, message, fatal: false);

        /// <summary>
        /// The error a SqlClient failure against a target becomes, the one boundary every failure of a statement dbchange sends passes
        /// through: with a SqlException anywhere in the chain, by its number, a severity of 20 or more being a connection lost, and
        /// <paramref name="opened"/> saying the connection had opened; any other failure is server.failed with no number. A DacFx failure
        /// passes through io/DacFx.Failed, which hands this adapter the SqlException or the SQL Server number it finds.
        /// </summary>
        internal Error ErrorOf(Exception failure, bool opened) => Chain(failure).OfType<SqlException>().FirstOrDefault() is { } sql
            ? ErrorOf(sql.Number, sql.Message, fatal: sql.Class >= 20, opened)
            : ErrorOf(0, failure.Message);

        /// <summary>
        /// The error SQL Server's error <paramref name="number"/> against this database becomes, as <see cref="Classify"/> reads it:
        /// server.denied, server.unreachable, server.timed-out or server.failed. <paramref name="fatal"/> is a severity of 20 or more,
        /// which closes the connection; <paramref name="opened"/> says the connection had opened before the statement failed.
        /// </summary>
        internal Error ErrorOf(int number, string message, bool fatal, bool opened = false)
        {
            var msg = number == 0 ? "no SQL Server number" : string.Create(CultureInfo.InvariantCulture, $"Msg {number}");
            return Classify(number, fatal, opened) switch
            {
                Category.Denied => new Error("server.denied", Target + " refused this identity (" + msg + ", SQL Server's message withheld)"
                    + (this is EnvironmentDatabase ? "; a lead's prediction will appear on the pull request." : "."),
                    this is EnvironmentDatabase
                    ? "Ask a lead to predict for " + Target + ", or ask its DBA for VIEW DEFINITION and db_datareader there."
                    : "Check the local server's login in DBCHANGE_SQL or ~/.dbchange/sql.env, then run dbchange doctor."),
                Category.Unreachable => new Error("server.unreachable",
                    Target + " does not answer (" + msg + ", SQL Server's message withheld).",
                    this is EnvironmentDatabase
                    ? "Check the network path to " + Target + "'s server and that it runs, then run dbchange doctor."
                    : "Start the local server with ci/sql.sh up, or ci/sql.ps1 up on Windows, then run dbchange doctor."),
                Category.TimedOut => new Error("server.timed-out",
                    Target + " answered, and the statement ran past its timeout (" + msg + ", SQL Server's message withheld).",
                    "Run the step again when the server is less busy, or ask its DBA what holds the locks the statement waits on."),
                _ => new Error("server.failed",
                    Target + " failed the statement: " + msg
                    + (Withheld ? "; SQL Server's message is withheld, since it can quote a row." : ": " + message),
                    "Look the number up in SQL Server's error list, correct what it names, then run the step again."),
            };
        }

        /// <summary>
        /// The failure of a statement SQL Server refused on an open connection, below severity 20, which a caller records as the
        /// statement's outcome (Measure's failed measurement): its number, SQL Server's message for a copy alone, and whether the command
        /// ran past its timeout. Null for a failure of the connection or the identity, which <see cref="ErrorOf(Exception, bool)"/> maps.
        /// </summary>
        internal StatementFailure? FailedStatement(Exception failure, bool opened) =>
            opened && Chain(failure).OfType<SqlException>().FirstOrDefault() is { Class: < 20 } sql
                ? new StatementFailure(sql.Number, Withheld ? null : sql.Message,
                    Classify(sql.Number, fatal: false, opened) == Category.TimedOut)
                : null;

        /// <summary>
        /// What SQL Server's error number means for a statement dbchange sent, the one reading of SQL Server's numbers (R4 lifts it into
        /// the kernel's SqlServerError at M2): a login, a database or a permission refused (<see cref="Denials"/>) is Denied; no answer, a
        /// login timeout among them, or a severity of 20 or more, which closes the connection (<see cref="Silences"/>), is Unreachable; a
        /// command timeout (-2) on a connection that had opened is TimedOut, since the server answered; anything else is the statement's
        /// own Failed, a deadlock (1205) among them.
        /// </summary>
        private static Category Classify(int number, bool fatal, bool opened) =>
            number == -2 && opened && !fatal ? Category.TimedOut
            : Denials.Contains(number) ? Category.Denied
            : fatal || Silences.Contains(number) ? Category.Unreachable
            : Category.Failed;

        private enum Category
        {
            Denied,
            Unreachable,
            TimedOut,
            Failed,
        }

        public sealed override string ToString() => Target.ToString();
    }

    /// <summary>
    /// The database of an environment dbchange/environments.json names: read only, and never published to (VALUES.md S7).
    /// </summary>
    public sealed class EnvironmentDatabase : Database
    {
        private EnvironmentDatabase(NamedEnvironment environment, string connection, string repositoryRoot)
            : base(environment.Target, connection) => (Environment, Root) = (environment, repositoryRoot);

        public NamedEnvironment Environment { get; }

        internal string Root { get; }

        internal override bool Withheld => true;

        internal static Result<EnvironmentDatabase> Of(NamedEnvironment environment, string repositoryRoot) =>
            Connect(Subject(environment), environment.Connection, repositoryRoot)
                .Map(c => new EnvironmentDatabase(environment, c, repositoryRoot));

        /// <summary>
        /// How an error about an environment's connection names it: its environment and its reference, never what the reference resolves
        /// to.
        /// </summary>
        internal static string Subject(NamedEnvironment environment) =>
            environment.Target + "'s connection, " + environment.Connection + ",";
    }

    /// <summary>
    /// A database io/LocalServer made on the local server and recorded in .dbchange/copies.json (§2.1 rule 3): the one target that
    /// publishes, and the one a Permissive profile is made for. Its constructor is io's, and only io/LocalServer calls it.
    /// </summary>
    public sealed class Copy : Database
    {
        internal Copy(CopyName name, string server, string repositoryRoot)
            : base(new Target.RegisteredCopy(name), ConnectionString.OfDatabase(server, name.ToString())) =>
                (Name, Root) = (name, repositoryRoot);

        public CopyName Name { get; }

        /// <summary>The repository whose registry holds this copy.</summary>
        internal string Root { get; }

        internal override bool Withheld => false;

        /// <summary>
        /// The pipeline's profile with BlockOnPossibleDataLoss off (§1 fact 10), made for this copy: the one maker of a Permissive profile.
        /// </summary>
        public PublishProfile.Permissive Permissive(PublishProfile.Strict strict) => PublishProfile.Permissive.Of(strict);

        /// <summary>
        /// The package at <paramref name="dacpac"/> published to this copy under the profile's options, Strict or this copy's Permissive,
        /// through io/DacFx.Publish.
        /// </summary>
        public Result<Copy> Publish(string dacpac, PublishProfile profile, QueryLog log) =>
            Reached(this, log).Bind(_ => Ssdt.Open(dacpac).Bind(package =>
        {
            using (package)
            {
                return DacFx.Publish(this, package, profile).Map(_ => this);
            }
        }));
    }

    /// <summary>A target as a database, dbchange/environments.json read under the repository root for it.</summary>
    public static Result<Database> Resolve(Target target, string repositoryRoot) =>
        Resolve(target, EnvironmentsFile.Read(repositoryRoot), repositoryRoot);

    /// <summary>
    /// A target as a database, against dbchange/environments.json as the verb read it once (<paramref name="environmentsFile"/>, whose
    /// error counts only for a target that needs the environments file): env: through the environment's connection reference; copy: through
    /// .dbchange/copies.json alone, on a server R15 clears against the environments file (io/LocalServer). A git ref and a package are read
    /// as packages, and the synthetic copy is not in this build.
    /// </summary>
    public static Result<Database> Resolve(Target target, Result<Environments> environmentsFile, string repositoryRoot) =>
        target.Match<Result<Database>>(
        environment => environmentsFile.Bind(environments => environments.Named(environment.Name) is { } named
            ? EnvironmentDatabase.Of(named, repositoryRoot).Map(n => (Database)n)
            : new Error("target.unnamed", environment + " names no environment of " + EnvironmentsFile.Json + ".",
                environments.All.Count == 0
                ? "Add the environment to " + EnvironmentsFile.Json + " with its host, connection reference and profile."
                : "Name one it holds: " + string.Join(", ", environments.All.Select(e => e.Target)) + ".")),
        copy => LocalServer.Registered(repositoryRoot, copy.Name, environmentsFile).Map(c => (Database)c),
        () => new Error("synthetic-copy.not-built",
            "synthetic-copy names the synthetic copy, which is not in this build; this build reads env: and copy: databases.",
            "Name an env: or a copy: target; dbchange --help lists what this build runs."),
        reference => NotADatabase(reference),
        dacpac => NotADatabase(dacpac));

    private static Error NotADatabase(Target target) => new Error("target.not-a-database",
        target + " is read as a package, and a database is asked for here: env:<name> or copy:<name>.",
        "Name the database as env:<name> or copy:<name>.");

    /// <summary>
    /// What this identity may read of a database it reached: the database, where it holds VIEW DEFINITION, and whether it also holds VIEW
    /// ANY DEFINITION on the server. SQL Server hides from an identity without it every login but its own, and the other server-scoped
    /// objects, so the model read from the database holds none of them, which the note read.database-scope says.
    /// </summary>
    public sealed record Readable(Database Database, bool ServerScope)
    {
        /// <summary>
        /// The note reading the database as a database-scoped identity carries; none for an identity that holds VIEW ANY DEFINITION.
        /// </summary>
        public IEnumerable<Finding> Notes =>
            ServerScope ? [] : [Finding.Note("read.database-scope", Database.Target.ToString(), Database.Target
                + " is read as an identity without VIEW ANY DEFINITION on the server, from which SQL Server hides the logins users map to "
                + "and other server-scoped objects, so the model holds none of them.")];
    }

    /// <summary>
    /// The target, when it answers this identity with VIEW DEFINITION, and whether the identity reads the server's scope too: what a verb
    /// asks before it builds anything, so a denial arrives first.
    /// </summary>
    public static Result<Readable> Reach(Database target, QueryLog log) => Reached(target, log);

    /// <summary>
    /// A copy's SQL Server (R1), which a claim on the copy records: the product version and the copy's compatibility level, read in one
    /// statement through <see cref="Query{T}"/>, and the digest of the image the dbchange-sql container runs, which Docker reports
    /// (LocalServer.Image). A named environment's server is read by S8, and nothing here reads it.
    /// </summary>
    public static Result<Server> ServerOf(Copy copy, QueryLog log) =>
        Query(copy, new Statement("SQL Server",
            "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), compatibility_level "
                + "FROM sys.databases WHERE database_id = DB_ID();"), log,
                rows => rows is [[string version, byte level]] ? (Version: version, Level: (int)level) : (Version: (string?)null, Level: 0))
            .Bind(read => Server.Of(read.Version, read.Level, LocalServer.Image(copy)));

    /// <summary>
    /// Whether the target answers this identity with what reading it takes, before DacFx's own retries begin: a connection opens, and
    /// the identity holds VIEW DEFINITION there (§1 fact 2), whose absence is a denial (Msg 300, SQL Server's number for it); and, in the
    /// same statement, whether it holds VIEW ANY DEFINITION on the server.
    /// </summary>
    private static Result<Readable> Reached(Database target, QueryLog log) =>
        Query(target, new Statement("VIEW DEFINITION",
            "SELECT HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DEFINITION'), HAS_PERMS_BY_NAME(NULL, NULL, N'VIEW ANY DEFINITION');"), log,
                rows => rows is [[{ } database, var server]]
                    ? (Database: Convert.ToInt32(database, CultureInfo.InvariantCulture) == 1,
                        Server: server is not null && Convert.ToInt32(server, CultureInfo.InvariantCulture) == 1)
                    : (Database: false, Server: false))
            .Bind(held => held.Database ? Result.Ok(new Readable(target, held.Server)) : target.ErrorOf(300, ""));

    /// <summary>
    /// A named environment's own SQLCMD values, which a plan against it sets over the profile's: each literal as the environments file
    /// gives it and each reference resolved in memory, recorded as a read of the environment's; a copy has none.
    /// </summary>
    internal static Result<IReadOnlyList<SqlCmdValue>> SqlCmdValues(Database target) =>
        target is not EnvironmentDatabase named ? Result.Ok<IReadOnlyList<SqlCmdValue>>([])
        : Result.All(named.Environment.SqlCmd.Select(variable => variable.Match(
            literal => Result.Ok(new SqlCmdValue(variable.Name, literal, false)),
            reference => Read(named + "'s " + SqlCmdVariable.Placeholder(variable.Name.ToString()) + ", " + reference + ",",
                reference, named.Root).Bind(read => read is { } value
                ? Result.Ok(new SqlCmdValue(variable.Name, value, true))
                : new Error("sqlcmd.unresolved",
                    named + "'s " + SqlCmdVariable.Placeholder(variable.Name.ToString()) + " names " + reference
                    + ", which resolves to nothing here.",
                    "Set the variable, or write the file outside git, that " + reference + " names.")))));
}
