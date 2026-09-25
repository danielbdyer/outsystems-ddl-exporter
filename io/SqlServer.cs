using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Estate.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnType = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType;

namespace Estate.Io;

/// <summary>
/// A live database, read whole and read only (V3_MILESTONES.md §2.2, WP 1.4): the target grammar; EnvironmentDatabase, the database of an
/// environment estate/posture.json names, and Copy, a database io/ScratchServer made, which alone publishes (§2.1 rule 3); Model through
/// LoadFromDatabase and io/Ssdt.Elements; Plan through DacServices.Script; and Measure, which runs an aggregate query its closed
/// allowlist admits, every answer an integer. A resolved connection is never printed, logged or put in an error, and a named environment's SQL Server messages
/// are withheld, since they can quote a row (§18). An error's code names what went wrong; cli/Contract.cs maps its category to the exit.
/// </summary>
public static class SqlServer
{
    /// <summary>A login, a database or a permission refused: the identity cannot read the target.</summary>
    private static readonly HashSet<int> Denials = [18456, 18452, 18470, 18486, 18487, 18488, 4060, 916, 229, 230, 262, 297, 300, 15247, 40532];

    /// <summary>No answer: the network, the instance or a timeout.</summary>
    private static readonly HashSet<int> Silences = [-2, -1, 2, 20, 26, 35, 40, 53, 64, 121, 232, 233, 258, 1225, 10053, 10054, 10060, 10061, 11001, 11004, 17142, 18401, 40613];

    /// <summary>A statement SQL Server refused on an open connection: its number, its message where a copy's may be kept, and whether the command ran past its timeout.</summary>
    internal sealed record StatementFailure(int Number, string? Message, bool TimedOut);

    /// <summary>
    /// The target an argument names (kernel/Target.cs), read for the argument <paramref name="subject"/>; a literal connection string, which
    /// only SqlClient's grammar tells from a target, is connection.literal at exit 6, and no part of the argument is quoted.
    /// </summary>
    public static Result<Target> Target(string text, string subject) =>
        ConnectionString.IsConnection(text) ? new Error("connection.literal", subject + " is a literal connection string, which no argument carries.",
            "Name the target as env:NAME, an environment whose connection estate/posture.json gives as env:VARIABLE or file:path.")
        : Kernel.Target.Parse(text, subject);

    /// <summary>A live database the tool reads: a named environment's or a copy's. Its resolved connection stays inside io, and it prints as its target.</summary>
    public abstract class Database
    {
        private protected Database(Target target, string connection) => (Target, Connection) = (target, connection);

        /// <summary>The target that names it: env:dev, copy:estate_host_4242_0a1b2c3d.</summary>
        public Target Target { get; }

        internal string Connection { get; }

        /// <summary>The database's name, as DacFx plans against it.</summary>
        internal string Catalog => ConnectionString.CatalogOf(Connection);

        /// <summary>Whether SQL Server's messages about a failed statement are withheld: a named environment's rows may be real (VALUES.md X2).</summary>
        internal abstract bool Withheld { get; }

        /// <summary>
        /// What a SQL Server error against this database becomes, by its number (M1 exit 7, X2): a login, a database or a permission
        /// refused is server.denied; no answer is server.unreachable; anything else is server.failed. SQL Server's message is kept only for a
        /// copy's failed statement, a copy's rows being minted; one about a connection can name the server or the login, and is withheld.
        /// </summary>
        public Error ErrorOf(int number, string message) => ErrorOf(number, message, fatal: false);

        /// <summary>
        /// The error a SqlClient or DacFx failure against a target becomes, the one boundary every failure against a server passes
        /// through. With a SqlException anywhere in the chain, by its number, a severity of 20 or more being a connection lost, and
        /// <paramref name="opened"/> saying the connection had opened. With none, DacFx's own failure: when its texts quote a SQL Server
        /// number (Msg 50000, the data-loss check; Msg 2627 inside SQL72014), by that number, since SQL Server's words, which can quote a
        /// row, are inside; else dacfx.failed, quoting what each exception of the chain says, DacFx's errors (SQL71501: …) among it, kept
        /// for a named environment too. Any other failure is server.failed with no number.
        /// </summary>
        public Error ErrorOf(Exception failure) => ErrorOf(failure, opened: false);

        internal Error ErrorOf(Exception failure, bool opened)
        {
            var chain = Chain(failure).ToList();
            var said = chain.SelectMany(Said).Distinct(StringComparer.Ordinal).ToList();
            return chain.OfType<SqlException>().FirstOrDefault() is { } sql ? ErrorOf(sql.Number, sql.Message, fatal: sql.Class >= 20, opened)
                : !chain.Any(x => x is DacServicesException or DacModelException) ? ErrorOf(0, failure.Message)
                : said.Select(s => SqlServerNumber.Match(s)).FirstOrDefault(m => m.Success) is { } number
                    ? ErrorOf(int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture), string.Join(' ', said))
                : new Error("dacfx.failed", "DacFx failed against " + Target + " with no SQL Server error inside: " + string.Join(' ', said),
                    "Correct what DacFx names in the project or the publish profile, then run the step again.");
        }

        private static readonly Regex SqlServerNumber = new(@"\bMsg (\d+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// What one exception of a DacFx failure says, on one line: its Message alone. DacFx writes each error and warning of the failure
        /// into Message as "Error SQL71501: …" (BuildPackage's SQL71501, AddObjects' SQL46010 and SQL71006, Publish's SQL72014 quoting
        /// Msg 50000, SQL72045), so the SQL Server number the error is routed by and every SQL7xxxx code are in it. A failed Publish's
        /// Messages also holds informational entries of number 0 that Message leaves out: PRINT output of a deployment script, "Altering
        /// Table [dbo].[T]...", "The statement has been terminated.", "An error occurred while the batch was being executed.". They carry
        /// no error and no code, and are not quoted.
        /// </summary>
        private static IEnumerable<string> Said(Exception x) =>
            Regex.Replace(x.Message, @"\s*\n\s*", " ", RegexOptions.CultureInvariant).Trim() is { Length: > 0 } text ? [text] : [];

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
                    + (this is EnvironmentDatabase ? "; a lead's prediction will appear on the pull request." : "."), this is EnvironmentDatabase
                    ? "Ask a lead to predict for " + Target + ", or ask its DBA for VIEW DEFINITION and db_datareader there."
                    : "Check the scratch server's login in ESTATE_SQL or ~/.estate/sql.env, then run estate doctor."),
                Category.Unreachable => new Error("server.unreachable", Target + " does not answer (" + msg + ", SQL Server's message withheld).", this is EnvironmentDatabase
                    ? "Check the network path to " + Target + "'s server and that it runs, then run estate doctor."
                    : "Start the scratch server with ci/sql.sh up, or ci/sql.ps1 up on Windows, then run estate doctor."),
                Category.TimedOut => new Error("server.timed-out", Target + " answered, and the statement ran past its timeout (" + msg + ", SQL Server's message withheld).",
                    "Run the step again when the server is less busy, or ask its DBA what holds the locks the statement waits on."),
                _ => new Error("server.failed", Target + " failed the statement: " + msg + (Withheld ? "; SQL Server's message is withheld, since it can quote a row." : ": " + message),
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
                ? new StatementFailure(sql.Number, Withheld ? null : sql.Message, Classify(sql.Number, fatal: false, opened) == Category.TimedOut)
                : null;

        /// <summary>
        /// What SQL Server's error number means for a statement estate sent, the one reading of SQL Server's numbers (R4 lifts it into the
        /// kernel's SqlServerError at M2): a login, a database or a permission refused (<see cref="Denials"/>) is Denied; no answer, a
        /// login timeout among them, or a severity of 20 or more, which closes the connection (<see cref="Silences"/>), is Unreachable; a
        /// command timeout (-2) on a connection that had opened is TimedOut, since the server answered; anything else is the statement's
        /// own Failed, a deadlock (1205) among them.
        /// </summary>
        private static Category Classify(int number, bool fatal, bool opened) =>
            number == -2 && opened && !fatal ? Category.TimedOut
            : Denials.Contains(number) ? Category.Denied
            : fatal || Silences.Contains(number) ? Category.Unreachable
            : Category.Failed;

        private static IEnumerable<Exception> Chain(Exception failure)
        {
            for (var x = failure; x is not null; x = x.InnerException)
            {
                yield return x;
            }
        }

        private enum Category
        {
            Denied,
            Unreachable,
            TimedOut,
            Failed,
        }

        public sealed override string ToString() => Target.ToString();
    }

    /// <summary>The database of an environment estate/posture.json names: read only, and never published to (VALUES.md S7).</summary>
    public sealed class EnvironmentDatabase : Database
    {
        private EnvironmentDatabase(NamedEnvironment environment, string connection, string estateRoot)
            : base(environment.Target, connection) => (Environment, Root) = (environment, estateRoot);

        public NamedEnvironment Environment { get; }

        internal string Root { get; }

        internal override bool Withheld => true;

        internal static Result<EnvironmentDatabase> Of(NamedEnvironment environment, string estateRoot) =>
            Connect(Subject(environment), environment.Connection, estateRoot).Map(c => new EnvironmentDatabase(environment, c, estateRoot));

        /// <summary>How an error about an environment's connection names it: its environment and its reference, never what the reference resolves to.</summary>
        internal static string Subject(NamedEnvironment environment) => environment.Target + "'s connection, " + environment.Connection + ",";
    }

    /// <summary>
    /// A database io/ScratchServer made on the scratch server and recorded in .estate/copies.json (§2.1 rule 3): the one target that
    /// publishes, and the one a Permissive profile is made for. Its constructor is io's, and only io/ScratchServer calls it.
    /// </summary>
    public sealed class Copy : Database
    {
        internal Copy(CopyName name, string server, string estateRoot)
            : base(new Target.RegisteredCopy(name), ConnectionString.WithCatalog(server, name.ToString())) => (Name, Root) = (name, estateRoot);

        public CopyName Name { get; }

        /// <summary>The estate whose registry holds this copy.</summary>
        internal string Root { get; }

        internal override bool Withheld => false;

        /// <summary>The pipeline's profile with the data-loss check off (§1 fact 10), made for this copy: the one maker of a Permissive profile.</summary>
        public PublishProfile.Permissive Permissive(PublishProfile.Strict strict) => PublishProfile.Permissive.Of(strict);

        /// <summary>The package published to this copy under the profile's options, Strict or this copy's Permissive, the package loaded from a stream.</summary>
        public Result<Copy> Publish(string dacpac, PublishProfile profile) => Reached(this, null).Bind(_ => Loaded(dacpac, this, package =>
        {
            new DacServices(Connection).Publish(package, Name.ToString(), new PublishOptions { DeployOptions = profile.Options() });
            return Result.Ok(this);
        }));
    }

    /// <summary>A target as a database, estate/posture.json read under the estate's root for it.</summary>
    public static Result<Database> Resolve(Target target, string estateRoot) => Resolve(target, Profiles.Environments(estateRoot), estateRoot);

    /// <summary>
    /// A target as a database, against estate/posture.json as the verb read it once (<paramref name="posture"/>, whose error counts only
    /// for a target that needs the posture): env: through the environment's connection reference; copy: through .estate/copies.json
    /// alone, on a server R15 clears against the posture (io/ScratchServer). A git ref and a package are read as packages, and the
    /// synthetic copy is not in this build.
    /// </summary>
    public static Result<Database> Resolve(Target target, Result<Environments> posture, string estateRoot) => target.Match<Result<Database>>(
        environment => posture.Bind(environments => environments.Named(environment.Name) is { } named
            ? EnvironmentDatabase.Of(named, estateRoot).Map(n => (Database)n)
            : new Error("target.unnamed", environment + " names no environment of " + Profiles.Posture + ".", environments.All.Count == 0
                ? "Add the environment to " + Profiles.Posture + " with its host, connection reference and profile."
                : "Name one it holds: " + string.Join(", ", environments.All.Select(e => e.Target)) + ".")),
        copy => ScratchServer.Registered(estateRoot, copy.Name, posture).Map(c => (Database)c),
        () => new Error("synthetic-copy.not-built", "synthetic-copy names the synthetic copy, which is not in this build; this build reads env: and copy: databases.",
            "Name an env: or a copy: target; estate --help lists what this build runs."),
        reference => NotADatabase(reference),
        dacpac => NotADatabase(dacpac));

    /// <summary>
    /// How LoadFromDatabase reads a database, each option set here with its reason, so a database reads as a package built from it
    /// does. DacFx 170.5.96's defaults are noted; IgnorePermissions is the one this changes.
    /// </summary>
    internal static ModelExtractOptions Extraction => new()
    {
        // A package keeps its GRANT, DENY and REVOKE statements and Ssdt.Elements keys each one; the default, true, drops every permission.
        IgnorePermissions = false,
        // A package keeps its sp_addextendedproperty values (MS_Description); the default, false, keeps them.
        IgnoreExtendedProperties = false,
        // A package keeps CREATE USER ... FOR LOGIN; the default, false, keeps a user's login.
        IgnoreUserLoginMappings = false,
        // A package holds database-scoped objects; the default, true, leaves out server-scoped ones a user does not reference.
        ExtractApplicationScopedObjectsOnly = true,
        // The default, true, reads the login a user maps to; SQL Server shows it only to a reader with permission on the login.
        ExtractReferencedServerScopedElements = true,
        // Table.RowCount, the data and index sizes and the page counts change with the rows, not the schema; the default is false.
        ExtractUsageProperties = false,
        // Ssdt.Elements reads properties and each module's script, which a model loaded from a database gives without a scripted copy of
        // every object; the default, false, skips that copy's one-time cost.
        LoadAsScriptBackedModel = false,
        // Verification validates the model as a package build would; Ssdt.Elements reads what the database holds, valid or not. Default false.
        VerifyExtraction = false,
        // The model is held in memory, as Ssdt.Load holds a package's; the default is Memory.
        Storage = DacSchemaModelStorageType.Memory,
        // DacFx's log is not kept, so hashing the names in it changes nothing; the default is false.
        HashObjectNamesInLogs = false,
    };

    /// <summary>A database read whole (§1 fact 5): TSqlModel.LoadFromDatabase as the target's identity under <see cref="Extraction"/>, then io/Ssdt.Elements; the run's log, when given, holds the statement estate sends first.</summary>
    public static Result<SortedArray<Element>> Model(Database target, QueryLog? log = null) => Reached(target, log).Bind(_ =>
    {
        TSqlModel model;
        try
        {
            model = TSqlModel.LoadFromDatabase(target.Connection, Extraction);
        }
        catch (Exception e) when (e is DacServicesException or DacModelException or SqlException or InvalidOperationException)
        {
            return target.ErrorOf(e);
        }

        using (model)
        {
            return Ssdt.Elements(model);
        }
    });

    /// <summary>What DacFx would deploy: its script, kept with the SQLCMD variables whose values a reference holds unset, and its report.</summary>
    public sealed record Deployment(string Script, string Report)
    {
        private static readonly XNamespace Dac = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

        /// <summary>The report's operations; none is the empty deploy plan, a database that matches the package (§1 fact 4).</summary>
        public int Operations => XDocument.Parse(Report).Descendants(Dac + "Operation").Count();

        /// <summary>Each object the report names, by the operation on it (Alter, Create, Drop, TableRebuild), its type as the model serializes it (SqlTable) and its name.</summary>
        public IReadOnlyList<(string Operation, string Type, string Name)> Items => [.. XDocument.Parse(Report).Descendants(Dac + "Operation")
            .SelectMany(o => o.Elements(Dac + "Item").Select(i => ((string?)o.Attribute("Name") ?? "", (string?)i.Attribute("Type") ?? "", (string?)i.Attribute("Value") ?? "")))];

        public bool IsEmpty => Operations == 0;
    }

    /// <summary>
    /// DacServices.Script of a package against the target under the profile's options (§1 facts 2 and 4), both outputs generated. A named
    /// environment's own SQLCMD values go over the profile's, a reference's resolved in memory and never kept in the script. The run's log,
    /// when given, holds the statement estate sends first.
    /// </summary>
    public static Result<Deployment> Plan(string dacpac, Database target, PublishProfile.Strict profile, QueryLog? log = null) => Values(target).Bind(values => Reached(target, log).Bind(_ =>
        Loaded(dacpac, target, package =>
        {
            var options = profile.Options();
            foreach (var value in values)
            {
                options.SqlCommandVariableValues[value.Name] = value.Text;
            }

            var plan = new DacServices(target.Connection).Script(package, target.Catalog,
                new PublishOptions { GenerateDeploymentScript = true, GenerateDeploymentReport = true, DeployOptions = options });
            var kept = values.Where(v => v.Referenced).Aggregate(plan.DatabaseScript, (script, v) =>
                Regex.Replace(script, @"^:setvar\s+" + Regex.Escape(v.Name) + @"\s.*(?:\r?\n|\z)", "", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            return Result.Ok(new Deployment(kept, plan.DeploymentReport));
        })));

    /// <summary>A SQLCMD value a plan sets: its variable, its text, and whether a reference gave it, so the kept script leaves it unset.</summary>
    private sealed record SqlCmdValue(string Name, string Text, bool Referenced);

    /// <summary>
    /// An aggregate query the allowlist admitted (VALUES.md P2): one statement, as ScriptDom writes it back, so what runs is what was
    /// checked, with no comment and no batch separator; and the site it measures. Only Of makes one, and Measure runs nothing else.
    /// </summary>
    public sealed class AggregateQuery
    {
        private AggregateQuery(string statement, string site) => (Statement, Site) = (statement, site);

        public string Statement { get; }

        public string Site { get; }

        public static Result<AggregateQuery> Of(string text, string site) => Allowlist.Admitted(text).Map(statement => new AggregateQuery(statement, site));

        public override string ToString() => Site + ": " + Statement;
    }

    /// <summary>
    /// What an aggregate query measured, a value: its rows, every value an integer or null, in the order of their values, so two
    /// measurements of the same rows are equal whatever order SQL Server returned them in; or its failure, the number and the site, SQL
    /// Server's message kept for a copy alone. The cases are closed, and Match reads each.
    /// </summary>
    public abstract record Measurement
    {
        private Measurement()
        {
        }

        public T Match<T>(Func<Answered, T> answered, Func<Failed, T> failed) => this switch
        {
            Answered a => answered(a),
            Failed f => failed(f),
            _ => throw new System.Diagnostics.UnreachableException(),
        };

        public sealed record Answered(string Site, SortedArray<Row> Rows) : Measurement;

        public sealed record Failed(string Site, int Number, string? Message) : Measurement
        {
            public override string ToString() =>
                Site + ": query failed: Msg " + Number.ToString(CultureInfo.InvariantCulture) + (Message is null ? "; message withheld" : ": " + Message);
        }
    }

    /// <summary>
    /// One row an aggregate query answered: the values of its select list, in order, each an integer or null. Two rows are equal when
    /// their values are, one by one; rows order by their values, a null before any integer and a shorter row before a longer one it begins.
    /// </summary>
    public sealed class Row : IEquatable<Row>, IComparable<Row>
    {
        private readonly long?[] values;

        private Row(long?[] values) => this.values = values;

        public IReadOnlyList<long?> Values => values;

        public static Row Of(params long?[] values) => new([.. values]);

        public bool Equals(Row? other) => other is not null && values.AsSpan().SequenceEqual(other.values);

        public override bool Equals(object? obj) => Equals(obj as Row);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }

        public int CompareTo(Row? other) => other is null ? 1
            : values.Zip(other.values, Nullable.Compare).FirstOrDefault(c => c != 0) is var byValue and not 0 ? byValue : values.Length.CompareTo(other.values.Length);

        public override string ToString() => string.Join(", ", values.Select(v => v?.ToString(CultureInfo.InvariantCulture) ?? "NULL"));
    }

    /// <summary>
    /// One admitted aggregate query against the target (WP 1.4), read back as integers and logged with its row count. A statement
    /// that fails is measured as failed, by its number; a connection that fails is an error.
    /// </summary>
    public static Result<Measurement> Measure(Database target, AggregateQuery query, QueryLog log)
    {
        using var connection = new SqlConnection(target.Connection);
        try
        {
            connection.Open();
            using var command = new SqlCommand(query.Statement, connection) { CommandTimeout = 30 };
            using var reader = command.ExecuteReader();
            var rows = new List<Row>();
            while (reader.Read())
            {
                rows.Add(Row.Of([.. Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? (long?)null : Integer(reader.GetValue(i)))]));
            }

            log.Add(target, query, rows.Count == 1 ? "1 row" : rows.Count.ToString(CultureInfo.InvariantCulture) + " rows");
            return new Measurement.Answered(query.Site, SortedArray.Of(rows));
        }
        catch (SqlException e) when (connection.State == System.Data.ConnectionState.Open && e.Class < 20)
        {
            log.Add(target, query, "failed, Msg " + e.Number.ToString(CultureInfo.InvariantCulture));
            return new Measurement.Failed(query.Site, e.Number, target.Withheld ? null : e.Message);
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            return target.ErrorOf(e);
        }
    }

    /// <summary>
    /// A run's log of every statement estate sends, .estate/runs/&lt;id&gt;/queries.log: each aggregate query, and the one statement Model and Plan send
    /// before DacFx's own catalog queries, which are DacFx's to answer for. Per statement: the time, the target, the site and the row count
    /// or the failure's number, then the statement and GO, so the log runs as a script. It holds no value a statement read.
    /// </summary>
    public sealed class QueryLog
    {
        private readonly Lock gate = new();
        private readonly StringBuilder entries = new();

        private QueryLog(string path) => Path = path;

        public string Path { get; }

        /// <summary>A new run's log under the estate's root, named for the time, the process and a random suffix, so two runs never share one.</summary>
        public static QueryLog Start(string estateRoot) => new(System.IO.Path.Combine(estateRoot, ".estate", "runs",
            DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
            + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant(), "queries.log"));

        internal void Add(Database target, AggregateQuery query, string outcome) => Add(target, query.Site, query.Statement, outcome);

        internal void Add(Database target, string site, string statement, string outcome)
        {
            lock (gate)
            {
                entries.Append("-- ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append(' ').Append(target.Target).Append(' ')
                    .Append(site).Append(": ").Append(outcome).Append('\n').Append(statement).Append("\nGO\n");
                Write.Text(Path, entries.ToString());
            }
        }
    }

    /// <summary>
    /// A reference's connection (§4 row 14, VALUES.md X1): env:NAME's variable or file:path's text, a relative path read from the estate's
    /// root, parsed by SqlClient's own grammar (io/ConnectionString.cs); the caller's integrated identity when it names no other; estate
    /// as the application unless it names one. An error names the reference and quotes nothing it read.
    /// </summary>
    internal static Result<string> Connect(string subject, SecretReference reference, string estateRoot) => Read(subject, reference, estateRoot).Bind(read => read is not { } text
        ? new Error("connection.unresolved", subject + " resolves to nothing here.", "Set the variable, or write the file outside git, that " + reference + " names.")
        : Parsed(subject, reference, text).Bind(connection => connection.InitialCatalog.Length == 0
            ? new Error("connection.malformed", subject + " names no database; every read reads the database the connection names.",
                "Give the connection string an Initial Catalog, in the place " + reference + " names.")
            : Result.Ok(ConnectionString.WithDefaults(connection))));

    /// <summary>An environment's server as R15 reads it, a database named or not: null when its reference resolves to nothing here; an error when SqlClient reads nothing from it.</summary>
    internal static Result<ServerName?> DataSource(NamedEnvironment environment, string estateRoot) => Read(EnvironmentDatabase.Subject(environment), environment.Connection, estateRoot).Bind(read => read is not { } text
        ? Result.Ok<ServerName?>(null)
        : Parsed(EnvironmentDatabase.Subject(environment), environment.Connection, text).Map(connection => (ServerName?)ConnectionString.ServerOf(connection)));

    /// <summary>A reference's text as SqlClient's own grammar reads it; the error names the reference and quotes nothing it read.</summary>
    private static Result<SqlConnectionStringBuilder> Parsed(string subject, SecretReference reference, string text) =>
        ConnectionString.Parse(subject, text, "Correct the connection string in the place " + reference + " names.");

    /// <summary>
    /// What a reference names: the variable's value, or the file's text trimmed; null when the variable is unset or empty, when the
    /// file system reports that no file or folder is at the path, that a folder is, or that no file can have the path's name
    /// (<see cref="Absent"/>), or when the file holds only white space. A file,
    /// a relative path read from the estate's root, is read only when git keeps it out of every commit, ignored or in no repository
    /// while the estate's root is in one, and, where files carry a Unix mode, when its owner alone can read it; an error leads with
    /// <paramref name="subject"/>. git is asked about the file by the name its folder lists (<see cref="Listed"/>), and that name is the
    /// one read. Whatever the file system withholds is an error, never null, since the host of a file that may be there is unknown
    /// here and R15 must not leave the environment uncompared as it does one that resolves to nothing: a path whose attributes this
    /// identity cannot read, where File.Exists answers false as it does where no file is, is reference.inaccessible; a file whose
    /// folder it cannot list is reference.unlistable; and one it cannot read is reference.unreadable. The check covers the path the
    /// reference names, since git tracks paths: a hard link to a committed file, or a plain copy of one, under a folder .gitignore
    /// lists such as .estate/ is read, though the commit holds what it holds. The attempt, a refused one too, is recorded in the run's
    /// Reads.
    /// </summary>
    internal static Result<string?> Read(string subject, SecretReference reference, string estateRoot)
    {
        Reads.Record();
        return reference.Match(
            variable => Result.Ok(System.Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : null),
            file => System.IO.Path.Combine(estateRoot, file) is var path && File.Exists(path)
                ? Opened(() => Listed(subject, path), () => Unlistable(subject))
                    .Bind(listed => Opened(() => Kept(subject, estateRoot, listed).Map(kept => File.ReadAllText(kept).Trim() is { Length: > 0 } text ? text : null), () => Unreadable(subject)))
                : Absent(subject, path));
    }

    /// <summary>
    /// Null for a path File.Exists does not open, when File.GetAttributes says why: no such file (FileNotFoundException), no such
    /// folder on the way (DirectoryNotFoundException), a name Windows forbids in a file name, such as one holding ? * &lt; &gt; or |
    /// (IOException for ERROR_INVALID_NAME, HResult 0x8007007B), or a folder at the path. File.Exists also answers false for a file
    /// whose attributes this identity cannot read (on Windows, RA denied on the file and RD on its folder; on Linux and macOS, a folder
    /// on the path without search permission); File.GetAttributes then throws UnauthorizedAccessException, or another IOException for
    /// a path it cannot reach, and that is reference.inaccessible.
    /// </summary>
    private static Result<string?> Absent(string subject, string path)
    {
        try
        {
            File.GetAttributes(path);
            return Result.Ok<string?>(null);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException || (e is IOException && e.HResult == InvalidName))
        {
            return Result.Ok<string?>(null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("reference.inaccessible", subject + " names a path whose attributes this identity cannot read, or that it cannot reach,"
                + " so whether a file is there, and what it holds, is unknown here; it is not read.",
                "Grant this identity the right to list the file's folder and read the file's attributes (on Linux and macOS, search permission on every folder of the path), or move the file under a folder it can list, such as .estate/.");
        }
    }

    /// <summary>The HResult of the IOException .NET throws for Windows' ERROR_INVALID_NAME (123), a name no file on Windows can have.</summary>
    private const int InvalidName = unchecked((int)0x8007007B);

    /// <summary>A step on a file that exists; the error given when the file system refuses the step (IOException, UnauthorizedAccessException).</summary>
    private static Result<T> Opened<T>(Func<Result<T>> step, Func<Error> refused)
    {
        try
        {
            return step();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return refused();
        }
    }

    private static Error Unlistable(string subject) => new Error("reference.unlistable", subject + " is a file whose folder this identity cannot list,"
        + " so git cannot be asked about the file by the name the folder lists, and the file is not read.",
        "Grant this identity the right to list the file's folder, or move the file under a folder it can list, such as .estate/.");

    private static Error Unreadable(string subject) => new Error("reference.unreadable", subject + " is a file this identity cannot open for reading,"
        + " for want of the right to read it or while another program holds it open, so what it holds is unknown here.",
        "Grant this identity the right to read the file, and close any program that holds it open.");

    /// <summary>
    /// The refusal of a file a reference names whose Unix mode lets its group or other users read it, or null: Read asks it of the
    /// file's mode on Linux and macOS, and Windows keeps no such mode.
    /// </summary>
    public static Error? ReadableByOthers(string subject, UnixFileMode mode) => (mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0 ? null
        : new Error("reference.readable-by-others", subject + " is a file its group or other users can read (mode "
            + Convert.ToString((int)mode & 0b111_111_111, 8).PadLeft(4, '0') + "); a file holding a connection string is read by its owner alone.",
            "Run chmod 600 on the file, so its owner alone reads it.");

    /// <summary>
    /// The full path of the file <paramref name="path"/> opens, spelled as its folder lists it, following a symbolic link to its final
    /// target: git matches .gitignore and its index against that name, and Windows opens a file under other spellings too. On Windows,
    /// Path.GetFullPath drops trailing dots and spaces and expands an 8.3 short name, and the folder's listing gives the name's case
    /// on Windows and macOS. A spelling that names no entry of its folder, such as Windows' name::$DATA for a file's default data
    /// stream, is reference.unlisted, the file unread. Read asks it only of a path File.Exists opens; it is public because the register's
    /// refusal paths ask it directly on Linux and macOS, where name::$DATA opens no file.
    /// </summary>
    public static Result<string> Listed(string subject, string path) => Entry(path) is not { } entry ? Unlisted(subject)
        : entry.ResolveLinkTarget(returnFinalTarget: true) is not { } target ? entry.FullName
        : Entry(target.FullName) is { } final ? final.FullName
        : Unlisted(subject);

    /// <summary>The entry of the path's folder that the path names: the one of its exact name, else, where the file system opens a name whatever its case, the one of its name in another case.</summary>
    private static FileInfo? Entry(string path)
    {
        var named = new FileInfo(System.IO.Path.GetFullPath(path));
        var entries = named.Directory is { Exists: true } folder ? folder.GetFiles() : [];
        return entries.FirstOrDefault(e => e.Name == named.Name)
            ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? entries.FirstOrDefault(e => string.Equals(e.Name, named.Name, StringComparison.OrdinalIgnoreCase)) : null);
    }

    private static Error Unlisted(string subject) => new Error("reference.unlisted", subject + " opens a file by a name its folder does not list, such as name::$DATA, a data stream;"
        + " git matches .gitignore and its index against the name the folder lists, so it cannot say whether a commit holds the file, and the file is not read.",
        "Write the path as dir or ls lists the file, in estate/posture.json.");

    /// <summary>
    /// The path of a file a file: reference names, when git keeps it out of every commit and no other user can read it; else the
    /// error, the file unread. git.failed and git.missing lead with <paramref name="subject"/>, then quote io/Git's own message.
    /// </summary>
    private static Result<string> Kept(string subject, string estateRoot, string path) => Git.HoldingOf(estateRoot, path).Match<Result<string>>(holding => holding switch
    {
        Git.Holding.Tracked => new Error("reference.tracked", subject + " is a file git tracks, so every clone of the repository holds what it holds; a file: reference names a file git keeps out of every commit.",
            "Run git rm --cached on the file, list it in .gitignore, and change the password it held, since the history keeps the commit."),
        Git.Holding.NotIgnored => new Error("reference.not-ignored", subject + " is a file git does not ignore, so the next git add commits it; a file: reference names a file git keeps out of every commit.",
            "List the file in .gitignore, or move it under a folder .gitignore lists, such as .estate/."),
        Git.Holding.EstateInNoRepository => new Error("reference.no-repository", subject + " names a file, and the estate's root " + estateRoot
            + " is in no git repository, so git cannot say whether a clone would commit the file; it is not read.",
            "Run estate in a clone of the estate's repository, or give the reference as env:NAME."),
        Git.Holding.Ignored => OwnerOnly(subject, path),
        Git.Holding.InNoRepository => OwnerOnly(subject, path),
        _ => throw new System.Diagnostics.UnreachableException(),
    }, error => new Error(error.Code, subject + " cannot be checked against git: " + error.Message, error.Remedy));

    /// <summary>The path of a file git keeps out of every commit, when no other user can read it: on Linux and macOS by its mode; Windows keeps no such mode.</summary>
    private static Result<string> OwnerOnly(string subject, string path) =>
        !OperatingSystem.IsWindows() && ReadableByOthers(subject, File.GetUnixFileMode(path)) is { } readable ? readable : path;

    /// <summary>
    /// Whether this run has read a connection or other reference of a named environment (VALUES.md X2), whose text an exception's message
    /// can then quote. SqlServer.Read records each read, and every one goes through it: EnvironmentDatabase.Of, which SqlServer.Resolve calls for env:;
    /// R15's read of each environment's connection in io/ScratchServer, which copy: and a new copy run; and a named environment's SQLCMD values.
    /// cli/Program.cs begins a run around each command and withholds an unexpected exception's message when the run holds a read. The
    /// record reaches the threads the run's work starts (it is an AsyncLocal); a read outside any run is recorded nowhere, no catch
    /// reading it.
    /// </summary>
    public static class Reads
    {
        private static readonly AsyncLocal<Run?> Current = new();

        /// <summary>A run begun on this thread, the current one until it is disposed, when the run it began inside is current again.</summary>
        public static Run Begin() => Current.Value = new Run(Current.Value);

        internal static void Record() => Current.Value?.Record();

        /// <summary>One command's record: whether it has read a named environment's reference.</summary>
        public sealed class Run : IDisposable
        {
            private readonly Run? outer;
            private int read;

            internal Run(Run? outer) => this.outer = outer;

            /// <summary>Whether a named environment's connection or other reference was read while this run was current.</summary>
            public bool NamedEnvironment => Volatile.Read(ref read) == 1;

            /// <summary>Recorded here and in each run this one began inside.</summary>
            internal void Record()
            {
                Interlocked.Exchange(ref read, 1);
                outer?.Record();
            }

            public void Dispose() => Current.Value = outer;
        }
    }

    private static Error NotADatabase(Target target) => new Error("target.not-a-database",
        target + " is read as a package, and a database is asked for here: env:<name> or copy:<name>.", "Name the database as env:<name> or copy:<name>.");

    /// <summary>The target, when it answers this identity with VIEW DEFINITION: what a verb asks before it builds anything, so a denial arrives first.</summary>
    public static Result<Database> Reach(Database target, QueryLog? log = null) => Reached(target, log);

    /// <summary>
    /// Whether the target answers this identity with what reading it takes, before DacFx's own retries begin: a connection opens, and
    /// the identity holds VIEW DEFINITION there (§1 fact 2), whose absence is a denial (Msg 300, SQL Server's number for it).
    /// </summary>
    private static Result<Database> Reached(Database target, QueryLog? log)
    {
        const string Statement = "SELECT HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DEFINITION');";
        try
        {
            using var connection = new SqlConnection(target.Connection);
            connection.Open();
            using var command = new SqlCommand(Statement, connection);
            var held = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
            log?.Add(target, "VIEW DEFINITION", Statement, "1 row");
            return held ? Result.Ok(target) : target.ErrorOf(300, "");
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            return target.ErrorOf(e);
        }
    }

    /// <summary>A package loaded from a stream, handed to use, then released (a package loaded by path holds the assemblies beside it in this process).</summary>
    private static Result<T> Loaded<T>(string dacpac, Database target, Func<DacPackage, Result<T>> use)
    {
        DacPackage package;
        FileStream? stream = null;
        try
        {
            stream = File.OpenRead(dacpac);
            package = DacPackage.Load(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DacServicesException or DacModelException or InvalidDataException or ArgumentException or System.Xml.XmlException)
        {
            stream?.Dispose();
            return new Error("package.unreadable", dacpac + " is not a package DacFx reads: " + e.Message, "Name a .dacpac a build wrote, or build its project again.");
        }

        using (stream)
        using (package)
        {
            try
            {
                return use(package);
            }
            catch (Exception e) when (e is DacServicesException or SqlException or InvalidOperationException)
            {
                return target.ErrorOf(e);
            }
        }
    }

    /// <summary>A named environment's own SQLCMD values, each literal as the posture gives it and each reference resolved in memory; a copy has none.</summary>
    private static Result<IReadOnlyList<SqlCmdValue>> Values(Database target) => target is not EnvironmentDatabase named ? Result.Ok<IReadOnlyList<SqlCmdValue>>([])
        : Result.All(named.Environment.SqlCmd.Select(variable => variable.Match(
            literal => Result.Ok(new SqlCmdValue(variable.Name, literal, false)),
            reference => Read(named + "'s " + SqlCmdVariable.Placeholder(variable.Name) + ", " + reference + ",", reference, named.Root).Bind(read => read is { } value
                ? Result.Ok(new SqlCmdValue(variable.Name, value, true))
                : new Error("sqlcmd.unresolved", named + "'s " + SqlCmdVariable.Placeholder(variable.Name) + " names " + reference + ", which resolves to nothing here.",
                    "Set the variable, or write the file outside git, that " + reference + " names.")))));

    /// <summary>A value the allowlist admits the type of: an integer of any width. Anything else is a defect in the allowlist, named by its type alone.</summary>
    private static long Integer(object value) => value switch
    {
        int or long or short or byte => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException("An aggregate query answered with a " + value.GetType().Name + ", a type no form of the allowlist yields."),
    };

    /// <summary>
    /// The aggregate-query allowlist, closed (WP 1.4): one SELECT whose outermost select list holds COUNT or COUNT_BIG of * or of DISTINCT a
    /// column, SUM(CASE WHEN … THEN 1 ELSE 0 END), MIN or MAX over LEN or DATALENGTH of a column, CASE WHEN EXISTS (…) THEN 1 ELSE 0 END
    /// or an integer literal; beneath it, names of one or two parts, TRY_ conversions, and the few functions held here. A boundary is a
    /// length or a literal, never read from the data: each predicate, in a join's ON as in a WHERE, reads at most one value from the data
    /// (a column, a length or an aggregate), save = or &lt;&gt; between two columns, an equi-join or an orphan check; and a subquery's
    /// select list carries only columns, literals and the answers above, so a derived column is never two values combined. Every other
    /// form is refused with where it stands and what it is, and none of the query's literals is quoted.
    /// </summary>
    private static class Allowlist
    {
        private const string Forms = "COUNT, COUNT_BIG, SUM(CASE WHEN … THEN 1 ELSE 0 END), MIN or MAX over LEN or DATALENGTH of a column, CASE WHEN EXISTS or an integer literal";

        private const string Boundary = "a boundary read from the data, two values the data holds set against each other; = or <> between two columns is the one comparison of two";

        private static readonly HashSet<string> Aggregates = new(StringComparer.OrdinalIgnoreCase) { "COUNT", "COUNT_BIG", "SUM", "MIN", "MAX", "AVG" };

        private static readonly HashSet<string> Scalars = new(StringComparer.OrdinalIgnoreCase) { "LEN", "DATALENGTH", "UPPER", "LOWER", "LTRIM", "RTRIM", "ISNULL", "ABS" };

        /// <summary>The first form the allowlist does not hold: where it stands, and what it is.</summary>
        private sealed record Offence(TSqlFragment At, string What);

        public static Result<string> Admitted(string text)
        {
            var parsed = new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(text), out var errors);
            if (errors.Count > 0)
            {
                return new Error("aggregate-query.refused", string.Create(CultureInfo.InvariantCulture, $"The query does not parse at line {errors[0].Line}, column {errors[0].Column}."),
                    "Correct the query's syntax at that place.");
            }

            var statements = ((TSqlScript)parsed).Batches.SelectMany(b => b.Statements).ToList();
            if (statements.Count != 1)
            {
                return new Error("aggregate-query.refused", string.Create(CultureInfo.InvariantCulture, $"The query holds {statements.Count} statements; estate runs one statement at a time."),
                    "Split it into queries of one SELECT each.");
            }

            if ((statements[0] is SelectStatement select ? Statement(select) : new Offence(statements[0], Kind(statements[0]))) is { } offence)
            {
                return new Error("aggregate-query.refused", string.Create(CultureInfo.InvariantCulture, $"The query is refused at line {offence.At.StartLine}, column {offence.At.StartColumn}: {offence.What}."),
                    "Rewrite it so its select list holds only " + Forms + ".");
            }

            new Sql160ScriptGenerator().GenerateScript(statements[0], out var script);
            return script.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        }

        private static Offence? Statement(SelectStatement s) =>
            s.Into is not null ? new Offence(s.Into, "SELECT INTO")
            : s.WithCtesAndXmlNamespaces is not null ? new Offence(s.WithCtesAndXmlNamespaces, "a WITH clause")
            : s.ComputeClauses.Count > 0 ? new Offence(s.ComputeClauses[0], "COMPUTE")
            : s.OptimizerHints.Count > 0 ? new Offence(s.OptimizerHints[0], "a query hint")
            : s.On is not null ? new Offence(s.On, "ON a filegroup")
            : s.QueryExpression is QuerySpecification q ? Query(q, outermost: true)
            : new Offence(s.QueryExpression, Kind(s.QueryExpression));

        private static Offence? Query(QuerySpecification q, bool outermost) =>
            q.ForClause is not null ? new Offence(q.ForClause, "FOR XML, FOR JSON or FOR BROWSE")
            : q.OrderByClause is not null ? new Offence(q.OrderByClause, "ORDER BY")
            : q.OffsetClause is not null ? new Offence(q.OffsetClause, "OFFSET")
            : q.WindowClause is not null ? new Offence(q.WindowClause, "a WINDOW clause")
            : q.TopRowFilter is { } top && (outermost || top.Percent || top.WithTies || !Literal(top.Expression))
                ? new Offence(top, outermost ? "TOP in the outermost select" : "TOP of other than an integer literal")
            : outermost && q.UniqueRowFilter == UniqueRowFilter.Distinct ? new Offence(q, "DISTINCT in the outermost select")
            : First(q.SelectElements, e => (e, outermost) switch
            {
                (SelectScalarExpression x, true) => Answer(x.Expression),
                (SelectScalarExpression x, false) => Carried(x.Expression),
                (SelectStarExpression star, false) => star.Qualifier is null || star.Qualifier.Count <= 2 ? null : Parts(star, star.Qualifier.Count),
                (SelectStarExpression star, true) => new Offence(star, "a star in the outermost select"),
                _ => new Offence(e, "a variable assigned"),
            })
            ?? (q.FromClause is null ? null : First(q.FromClause.TableReferences, Table))
            ?? (q.WhereClause is not { } where ? null : where.Cursor is not null ? new Offence(where, "CURRENT OF") : Predicate(where.SearchCondition))
            ?? (q.GroupByClause is not { } group ? null : group.GroupByOption != GroupByOption.None || group.All ? new Offence(group, "GROUP BY ALL, ROLLUP or CUBE")
                : First(group.GroupingSpecifications, g => g is ExpressionGroupingSpecification { Expression: var x } && (Length(x) || (!outermost && x is ColumnReferenceExpression c && Column(c) is null))
                    ? null : new Offence(g, outermost ? "GROUP BY other than LEN or DATALENGTH of a column, which draws its groups from the data" : "GROUP BY other than a column or its length")))
            ?? (q.HavingClause is null ? null : Predicate(q.HavingClause.SearchCondition));

        /// <summary>An item of the outermost select list: one of the forms, each answering an integer.</summary>
        private static Offence? Answer(ScalarExpression x) => x switch
        {
            ParenthesisExpression p => Answer(p.Expression),
            IntegerLiteral or UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative, Expression: IntegerLiteral } =>
                long.TryParse(((x as UnaryExpression)?.Expression as IntegerLiteral ?? (IntegerLiteral)x).Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= int.MaxValue
                    ? null : new Offence(x, "an integer literal SQL Server types as numeric"),
            SearchedCaseExpression c when c.WhenClauses is [{ WhenExpression: ExistsPredicate exists }] => OneOrZero(c) ?? Subquery(exists.Subquery.QueryExpression),
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => Aggregate(f),
            FunctionCall f => new Offence(f, f.FunctionName.Value.ToUpperInvariant() + ", which the outermost select list does not hold"),
            ColumnReferenceExpression { ColumnType: ColumnType.Regular } => new Offence(x, "a bare column"),
            _ => new Offence(x, Kind(x) + ", which answers other than an integer"),
        };

        /// <summary>The aggregates the allowlist holds: COUNT or COUNT_BIG of * or of DISTINCT a column; SUM(CASE WHEN … THEN 1 ELSE 0 END); MIN or MAX over LEN or DATALENGTH of a column.</summary>
        private static Offence? Aggregate(FunctionCall f)
        {
            var name = f.FunctionName.Value.ToUpperInvariant();
            return f.CallTarget is not null || f.OverClause is not null || f.WithinGroupClause is not null
                ? new Offence(f, name + " with OVER, WITHIN GROUP or a target")
                : (name, f.UniqueRowFilter, f.Parameters) switch
                {
                    ("COUNT" or "COUNT_BIG", UniqueRowFilter.NotSpecified, [ColumnReferenceExpression { ColumnType: ColumnType.Wildcard }]) => null,
                    ("COUNT" or "COUNT_BIG", UniqueRowFilter.Distinct, [ColumnReferenceExpression { ColumnType: ColumnType.Regular } c]) => Column(c),
                    ("COUNT" or "COUNT_BIG", _, _) => new Offence(f, name + " of other than * or DISTINCT a column"),
                    ("SUM", UniqueRowFilter.NotSpecified, [SearchedCaseExpression { WhenClauses: [var when] } c]) => OneOrZero(c) ?? Predicate(when.WhenExpression),
                    ("SUM", _, _) => new Offence(f, "SUM of other than CASE WHEN … THEN 1 ELSE 0 END"),
                    ("MIN" or "MAX", UniqueRowFilter.NotSpecified, [var length]) when Length(length) => null,
                    (_, _, [ColumnReferenceExpression { ColumnType: ColumnType.Regular }]) => new Offence(f, name + " over a bare column"),
                    _ => new Offence(f, name + " over other than LEN or DATALENGTH of a column"),
                };
        }

        /// <summary>A CASE whose one WHEN answers 1 and whose ELSE answers 0.</summary>
        private static Offence? OneOrZero(SearchedCaseExpression c) =>
            c.WhenClauses is [{ ThenExpression: IntegerLiteral { Value: "1" } }] && c.ElseExpression is IntegerLiteral { Value: "0" } ? null : new Offence(c, "a CASE answering other than 1 or 0");

        /// <summary>A predicate whose comparisons each read one value from the data, save = or &lt;&gt; of two columns: no row's value bounds another's, whether a join, a correlated EXISTS or arithmetic (ABS(x - y) + x - y = 0 is x &lt;= y) meets them.</summary>
        private static Offence? Predicate(BooleanExpression b) => b switch
        {
            BooleanBinaryExpression x => Predicate(x.FirstExpression) ?? Predicate(x.SecondExpression),
            BooleanNotExpression x => Predicate(x.Expression),
            BooleanParenthesisExpression x => Predicate(x.Expression),
            BooleanComparisonExpression x => Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression)
                ?? (x.ComparisonType is BooleanComparisonType.Equals or BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation
                    && Bare(x.FirstExpression) && Bare(x.SecondExpression) ? null : Bounded(x, x.FirstExpression, x.SecondExpression)),
            BooleanIsNullExpression x => Scalar(x.Expression) ?? Bounded(x, x.Expression),
            BooleanTernaryExpression x => Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression) ?? Scalar(x.ThirdExpression) ?? Bounded(x, x.FirstExpression, x.SecondExpression, x.ThirdExpression),
            LikePredicate x => Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression) ?? (x.EscapeExpression is null ? null : Scalar(x.EscapeExpression))
                ?? Bounded(x, x.FirstExpression, x.SecondExpression, x.EscapeExpression),
            InPredicate x => Scalar(x.Expression) ?? (x.Subquery is { } s
                ? Subquery(s.QueryExpression) ?? (Bare(x.Expression) || !Data(x.Expression).Any() ? null : new Offence(x, Boundary))
                : First(x.Values, Scalar) ?? Bounded(x, [x.Expression, .. x.Values])),
            ExistsPredicate x => Subquery(x.Subquery.QueryExpression),
            _ => new Offence(b, Kind(b)),
        };

        /// <summary>A comparison's operands, which may read one value from the data between them and set it against literals alone.</summary>
        private static Offence? Bounded(TSqlFragment at, params ScalarExpression?[] operands) => operands.OfType<ScalarExpression>().SelectMany(Data).Skip(1).Any() ? new Offence(at, Boundary) : null;

        /// <summary>
        /// What an operand reads from the data: each column outside a length or an aggregate, each length, each aggregate; a CASE, what its
        /// THEN and ELSE read, its WHEN being a predicate of its own. A literal reads nothing, and any form not named here counts as one read.
        /// </summary>
        private static IEnumerable<ScalarExpression> Data(ScalarExpression x) => x switch
        {
            _ when Constant(x) => [],
            _ when Length(x) => [x],
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => [x],
            UnaryExpression u => Data(u.Expression),
            BinaryExpression y => [.. Data(y.FirstExpression), .. Data(y.SecondExpression)],
            ParenthesisExpression p => Data(p.Expression),
            TryConvertCall t => [.. Data(t.Parameter), .. t.Style is null ? [] : Data(t.Style)],
            TryCastCall t => Data(t.Parameter),
            CoalesceExpression c => c.Expressions.SelectMany(Data),
            NullIfExpression n => [.. Data(n.FirstExpression), .. Data(n.SecondExpression)],
            SearchedCaseExpression s => [.. s.WhenClauses.SelectMany(w => Data(w.ThenExpression)), .. s.ElseExpression is null ? [] : Data(s.ElseExpression)],
            SimpleCaseExpression s => [.. Data(s.InputExpression), .. s.WhenClauses.SelectMany(w => Data(w.WhenExpression).Concat(Data(w.ThenExpression))),
                .. s.ElseExpression is null ? [] : Data(s.ElseExpression)],
            FunctionCall f => f.Parameters.SelectMany(Data),
            _ => [x],
        };

        /// <summary>An item of a subquery's select list: a column, a literal or one of the answers, so what an outer predicate reads as a column is a column's own value, a length or a count.</summary>
        private static Offence? Carried(ScalarExpression x) => x is ParenthesisExpression p ? Carried(p.Expression) : x is ColumnReferenceExpression c ? Column(c)
            : Constant(x) || Answer(x) is null ? null : new Offence(x, "a value computed in a subquery's select list, which an outer predicate would read as a column");

        /// <summary>A column as it stands, perhaps in parentheses: one side of an equi-join.</summary>
        private static bool Bare(ScalarExpression x) => x is ColumnReferenceExpression { ColumnType: ColumnType.Regular } || (x is ParenthesisExpression p && Bare(p.Expression));

        /// <summary>A literal of the kinds an aggregate query may write: a number, a string, a binary value or NULL.</summary>
        private static bool Constant(ScalarExpression x) => x is IntegerLiteral or NumericLiteral or RealLiteral or MoneyLiteral or StringLiteral or BinaryLiteral or NullLiteral;

        private static Offence? Scalar(ScalarExpression x) => x switch
        {
            ColumnReferenceExpression c => Column(c),
            _ when Constant(x) => null,
            UnaryExpression u => Scalar(u.Expression),
            BinaryExpression y => Scalar(y.FirstExpression) ?? Scalar(y.SecondExpression),
            ParenthesisExpression p => Scalar(p.Expression),
            TryConvertCall t => Scalar(t.Parameter) ?? (t.Style is null ? null : Scalar(t.Style)),
            TryCastCall t => Scalar(t.Parameter),
            CoalesceExpression c => First(c.Expressions, Scalar),
            NullIfExpression n => Scalar(n.FirstExpression) ?? Scalar(n.SecondExpression),
            SearchedCaseExpression s => First(s.WhenClauses, w => Predicate(w.WhenExpression) ?? Scalar(w.ThenExpression)) ?? (s.ElseExpression is null ? null : Scalar(s.ElseExpression)),
            SimpleCaseExpression s => Scalar(s.InputExpression) ?? First(s.WhenClauses, w => Scalar(w.WhenExpression) ?? Scalar(w.ThenExpression)) ?? (s.ElseExpression is null ? null : Scalar(s.ElseExpression)),
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => Aggregate(f),
            FunctionCall f when Scalars.Contains(f.FunctionName.Value) && f.CallTarget is null && f.OverClause is null && f.UniqueRowFilter == UniqueRowFilter.NotSpecified => First(f.Parameters, Scalar),
            FunctionCall f => new Offence(f, (f.CallTarget is null ? f.FunctionName.Value.ToUpperInvariant() : "a function of the database's own") + ", which the allowlist does not hold"),
            ConvertCall or CastCall => new Offence(x, "CONVERT or CAST, which quotes the value it fails on; TRY_CONVERT and TRY_CAST do not"),
            ScalarSubquery => new Offence(x, "a subquery's value, a boundary read from the data"),
            VariableReference or GlobalVariableExpression => new Offence(x, "a variable"),
            _ => new Offence(x, Kind(x)),
        };

        private static Offence? Table(TableReference t) => t switch
        {
            NamedTableReference n => n.SchemaObject.ServerIdentifier is not null || n.SchemaObject.DatabaseIdentifier is not null ? Parts(n, n.SchemaObject.Count)
                : n.TableHints.Count > 0 ? new Offence(n.TableHints[0], "a table hint")
                : n.TableSampleClause is not null ? new Offence(n.TableSampleClause, "TABLESAMPLE")
                : n.TemporalClause is not null ? new Offence(n.TemporalClause, "FOR SYSTEM_TIME")
                : null,
            QualifiedJoin j => j.JoinHint != JoinHint.None ? new Offence(j, "a join hint") : Table(j.FirstTableReference) ?? Table(j.SecondTableReference) ?? Predicate(j.SearchCondition),
            UnqualifiedJoin j => j.UnqualifiedJoinType == UnqualifiedJoinType.CrossJoin ? Table(j.FirstTableReference) ?? Table(j.SecondTableReference) : new Offence(j, "CROSS APPLY or OUTER APPLY"),
            JoinParenthesisTableReference p => Table(p.Join),
            QueryDerivedTable d => Subquery(d.QueryExpression),
            _ => new Offence(t, Kind(t)),
        };

        private static Offence? Subquery(QueryExpression q) => q switch
        {
            QuerySpecification s => Query(s, outermost: false),
            QueryParenthesisExpression p => Subquery(p.QueryExpression),
            _ => new Offence(q, Kind(q)),
        };

        private static Offence? Column(ColumnReferenceExpression c) =>
            c.ColumnType != ColumnType.Regular ? new Offence(c, c.ColumnType == ColumnType.Wildcard ? "a star" : "a pseudo-column")
            : c.MultiPartIdentifier.Count <= 2 ? null : Parts(c, c.MultiPartIdentifier.Count);

        /// <summary>LEN or DATALENGTH of a column: a length, the one boundary the allowlist reads from a value.</summary>
        private static bool Length(ScalarExpression x) =>
            x is FunctionCall { CallTarget: null, OverClause: null, UniqueRowFilter: UniqueRowFilter.NotSpecified, Parameters: [ColumnReferenceExpression c] } f
            && (string.Equals(f.FunctionName.Value, "LEN", StringComparison.OrdinalIgnoreCase) || string.Equals(f.FunctionName.Value, "DATALENGTH", StringComparison.OrdinalIgnoreCase))
            && Column(c) is null;

        private static bool Literal(ScalarExpression x) => x is IntegerLiteral || x is ParenthesisExpression { Expression: IntegerLiteral };

        private static Offence Parts(TSqlFragment at, int parts) => new(at, string.Create(CultureInfo.InvariantCulture, $"a name of {parts} parts"));

        private static Offence? First<T>(IEnumerable<T> items, Func<T, Offence?> check) => items.Select(check).FirstOrDefault(o => o is not null);

        private static string Kind(TSqlFragment fragment) => fragment.GetType().Name;
    }
}
