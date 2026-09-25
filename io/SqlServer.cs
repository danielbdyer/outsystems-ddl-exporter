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
/// A live database, read whole and read only (V3_MILESTONES.md §2.2, WP 1.4): the target grammar; Named, the database of an
/// environment estate/posture.json names, and Copy, a database io/Substrate made, which alone publishes (§2.1 rule 3); Model through
/// LoadFromDatabase and io/Ssdt's walk; Plan through DacServices.Script; and the probe executor, whose closed allowlist admits only
/// integer answers. A resolved connection is never printed, logged or put in a refusal, and a named environment's SQL Server messages
/// are withheld, since they can quote a row (§18). A refusal's code names what was refused; cli/Contract.cs maps its area to the exit.
/// </summary>
public static class SqlServer
{
    /// <summary>A login, a database or a permission refused: the identity cannot read the target.</summary>
    private static readonly HashSet<int> Denials = [18456, 18452, 18470, 18486, 18487, 18488, 4060, 916, 229, 230, 262, 297, 300, 15247, 40532];

    /// <summary>No answer: the network, the instance or a timeout.</summary>
    private static readonly HashSet<int> Silences = [-2, -1, 2, 20, 26, 35, 40, 53, 64, 121, 232, 233, 258, 1225, 10053, 10054, 10060, 10061, 11001, 11004, 17142, 18401, 40613];

    /// <summary>
    /// Where a verb reads or writes, as an argument writes it (WP 1.4): env:&lt;name&gt;, an environment estate/posture.json names;
    /// copy:&lt;name&gt;, a copy .estate/copies.json holds; twin; ref:&lt;git ref&gt;; dacpac:&lt;path&gt;. The cases are closed.
    /// </summary>
    public abstract record Target
    {
        private static readonly Regex Environment = new(@"\A[a-z][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant);
        private static readonly Regex Registered = new(@"\A[a-z0-9_]{1,128}\z", RegexOptions.CultureInvariant);

        private Target()
        {
        }

        /// <summary>A target from an argument: a literal connection string is exit 6, copy: before a name no copy can carry is exit 9 (M1 exit 5), and no part of the argument is quoted.</summary>
        public static Result<Target> Parse(string text, string subject = "--target") =>
            Profiles.IsConnection(text) ? new Refusal("connection.literal", subject + " is a literal connection string, which no argument carries.",
                "Name the target as env:NAME, an environment whose connection estate/posture.json gives as env:VARIABLE or file:path.")
            : text == "twin" ? new Twin()
            : After(text, "env:") is { } name && Environment.IsMatch(name) ? new Env(name)
            : After(text, "copy:") is { } copy ? (Registered.IsMatch(copy) ? new Copy(copy) : new Refusal("copy.unregistered",
                subject + " names a copy by a name no copy estate makes can carry, so " + Substrate.Registry + " holds none by it; a copy's name is estate_<host>_<pid>_<rand>, in lowercase letters, digits and '_'.",
                "Name a copy that " + Substrate.Registry + " holds on this machine."))
            : After(text, "ref:") is { Length: > 0 } reference && !reference.StartsWith('-') && !reference.Any(char.IsControl) ? new Ref(reference)
            : After(text, "dacpac:") is { Length: > 0 } path && !path.Any(char.IsControl) ? new Dacpac(path)
            : new Refusal("target.unknown", subject + " is none of env:<name>, copy:<name>, twin, ref:<git ref> and dacpac:<path>.",
                "Write the target in one of those forms, such as env:dev or ref:main.");

        public T Match<T>(Func<Env, T> env, Func<Copy, T> copy, Func<T> twin, Func<Ref, T> reference, Func<Dacpac, T> dacpac) => this switch
        {
            Env e => env(e),
            Copy c => copy(c),
            Twin => twin(),
            Ref r => reference(r),
            Dacpac d => dacpac(d),
            _ => throw new System.Diagnostics.UnreachableException(),
        };

        public sealed override string ToString() => Match(e => "env:" + e.Name, c => "copy:" + c.Name, () => "twin", r => "ref:" + r.Name, d => "dacpac:" + d.Path);

        private static string? After(string text, string prefix) => text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : null;

        public sealed record Env(string Name) : Target;

        public sealed record Copy(string Name) : Target;

        public sealed record Twin : Target;

        public sealed record Ref(string Name) : Target;

        public sealed record Dacpac(string Path) : Target;
    }

    /// <summary>A live database the tool reads: a named environment's or a copy's. Its resolved connection stays inside io, and it prints as its target.</summary>
    public abstract class Database
    {
        private protected Database(string where, string connection) => (Where, Connection) = (where, connection);

        /// <summary>The target as an argument writes it: env:dev, copy:estate_host_4242_0a1b2c3d.</summary>
        public string Where { get; }

        internal string Connection { get; }

        /// <summary>The database's name, as DacFx plans against it.</summary>
        internal string Catalog => new SqlConnectionStringBuilder(Connection).InitialCatalog;

        /// <summary>Whether SQL Server's messages about a failed statement are withheld: a named environment's rows may be real (VALUES.md X2).</summary>
        internal abstract bool Withheld { get; }

        /// <summary>
        /// The refusal a SQL Server error against this database takes, by its number (M1 exit 7, X2): a login, a database or a permission
        /// refused is server.denied; no answer is server.unreachable; anything else is server.failed. SQL Server's message is kept only for a
        /// copy's failed statement, a copy's rows being minted; one about a connection can name the server or the login, and is withheld.
        /// </summary>
        public Refusal Refused(int number, string message) => Refused(number, message, fatal: false);

        /// <summary>
        /// The refusal a SqlClient or DacFx failure against a target takes. With a SqlException inside, by its number, a severity of 20 or
        /// more being a connection lost. With none, DacFx's own failure: when its texts quote a SQL Server number (Msg 50000, the guard;
        /// Msg 2627 inside SQL72014), by that number, since SQL Server's words, which can quote a row, are inside; else dacfx.failed,
        /// quoting what each exception of the chain says, DacFx's errors (SQL71501: …) among it, kept for a named environment too. Any
        /// other failure is refused with no number.
        /// </summary>
        public Refusal Refused(Exception failure)
        {
            var chain = new List<Exception>();
            for (var x = failure; x is not null; x = x.InnerException)
            {
                chain.Add(x);
            }

            var said = chain.SelectMany(Said).Distinct(StringComparer.Ordinal).ToList();
            return chain.OfType<SqlException>().FirstOrDefault() is { } sql ? Refused(sql.Number, sql.Message, fatal: sql.Class >= 20)
                : !chain.Any(x => x is DacServicesException or DacModelException) ? Refused(0, failure.Message)
                : said.Select(s => SqlServerNumber.Match(s)).FirstOrDefault(m => m.Success) is { } number
                    ? Refused(int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture), string.Join(' ', said))
                : new Refusal("dacfx.failed", "DacFx failed against " + Where + " with no SQL Server error inside: " + string.Join(' ', said),
                    "Correct what DacFx names in the project or the publish profile, then run the step again.");
        }

        private static readonly Regex SqlServerNumber = new(@"\bMsg (\d+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// What one exception of a DacFx failure says, on one line: its Message alone. DacFx writes each error and warning of the failure
        /// into Message as "Error SQL71501: …" (BuildPackage's SQL71501, AddObjects' SQL46010 and SQL71006, Publish's SQL72014 quoting
        /// Msg 50000, SQL72045), so the SQL Server number the refusal is routed by and every SQL7xxxx code are in it. A failed Publish's
        /// Messages also holds informational entries of number 0 that Message leaves out: PRINT output of a deployment script, "Altering
        /// Table [dbo].[T]...", "The statement has been terminated.", "An error occurred while the batch was being executed.". They carry
        /// no error and no code, and are not quoted.
        /// </summary>
        private static IEnumerable<string> Said(Exception x) =>
            Regex.Replace(x.Message, @"\s*\n\s*", " ", RegexOptions.CultureInvariant).Trim() is { Length: > 0 } text ? [text] : [];

        internal Refusal Refused(int number, string message, bool fatal)
        {
            var msg = number == 0 ? "no SQL Server number" : string.Create(CultureInfo.InvariantCulture, $"Msg {number}");
            return Denials.Contains(number) ? new Refusal("server.denied", Where + " refused this identity (" + msg + ", SQL Server's message withheld)"
                    + (this is Named ? "; a lead's prediction will appear on the pull request." : "."), this is Named
                    ? "Ask a lead to predict for " + Where + ", or ask its DBA for VIEW DEFINITION and db_datareader there."
                    : "Check the substrate's login in ESTATE_SQL or ~/.estate/sql.env, then run estate doctor.")
                : fatal || Silences.Contains(number) ? new Refusal("server.unreachable", Where + " does not answer (" + msg + ", SQL Server's message withheld).", this is Named
                    ? "Check the network path to " + Where + "'s server and that it runs, then run estate doctor."
                    : "Start the substrate with ci/sql.sh up, or ci/sql.ps1 up on Windows, then run estate doctor.")
                : new Refusal("server.failed", Where + " failed the statement: " + msg + (Withheld ? "; SQL Server's message is withheld, since it can quote a row." : ": " + message),
                    "Look the number up in SQL Server's error list, correct what it names, then run the step again.");
        }

        public sealed override string ToString() => Where;
    }

    /// <summary>The database of an environment estate/posture.json names: read only, and never published to (VALUES.md S7).</summary>
    public sealed class Named : Database
    {
        private Named(NamedEnvironment environment, string connection, string estateRoot)
            : base("env:" + environment.Name, connection) => (Environment, Root) = (environment, estateRoot);

        public NamedEnvironment Environment { get; }

        internal string Root { get; }

        internal override bool Withheld => true;

        internal static Result<Named> Of(NamedEnvironment environment, string estateRoot) =>
            Connect(Subject(environment), environment.Connection, estateRoot).Map(c => new Named(environment, c, estateRoot));

        /// <summary>How a refusal about an environment's connection names it: its environment and its reference, never what the reference resolves to.</summary>
        internal static string Subject(NamedEnvironment environment) => "env:" + environment.Name + "'s connection, " + environment.Connection + ",";
    }

    /// <summary>
    /// A database io/Substrate made on the local substrate and recorded in .estate/copies.json (§2.1 rule 3): the one target that
    /// publishes, and the one a Permissive profile is made for. Its constructor is io's, and only io/Substrate calls it.
    /// </summary>
    public sealed class Copy : Database
    {
        internal Copy(string name, string server, string estateRoot)
            : base("copy:" + name, new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString) => (Name, Root) = (name, estateRoot);

        public string Name { get; }

        /// <summary>The estate whose registry holds this copy.</summary>
        internal string Root { get; }

        internal override bool Withheld => false;

        /// <summary>The pipeline's profile with the guard off (§1 fact 10), made for this copy: the one maker of a Permissive profile.</summary>
        public PublishProfile.Permissive Permissive(PublishProfile.Strict strict) => PublishProfile.Permissive.Of(strict);

        /// <summary>The package published to this copy under the profile's options, Strict or this copy's Permissive, the package loaded from a stream.</summary>
        public Result<Copy> Publish(string dacpac, PublishProfile profile) => Reached(this, null).Bind(_ => Loaded(dacpac, this, package =>
        {
            new DacServices(Connection).Publish(package, Name, new PublishOptions { DeployOptions = profile.Options() });
            return Result.Ok(this);
        }));
    }

    /// <summary>
    /// A target as a database: env: through estate/posture.json and the environment's connection reference; copy: through
    /// .estate/copies.json alone (io/Substrate). A git ref and a package are read as packages, and the Twin arrives in M3.
    /// </summary>
    public static Result<Database> Resolve(Target target, string estateRoot) => target.Match<Result<Database>>(
        env => Profiles.Environments(estateRoot).Bind(environments => environments.FirstOrDefault(e => e.Name == env.Name) is { } named
            ? Named.Of(named, estateRoot).Map(n => (Database)n)
            : new Refusal("target.unnamed", env + " names no environment of " + Profiles.Posture + ".", environments.Count == 0
                ? "Add the environment to " + Profiles.Posture + " with its connection reference and profile."
                : "Name one it holds: " + string.Join(", ", environments.Select(e => "env:" + e.Name)) + ".")),
        copy => Substrate.Registered(estateRoot, copy.Name).Map(c => (Database)c),
        () => new Refusal("twin.not-built", "twin names the Twin, which arrives in M3 (Twin); this build reads env: and copy: databases.",
            "Name an env: or a copy: target; estate --help lists what this build runs."),
        reference => NotADatabase(reference),
        dacpac => NotADatabase(dacpac));

    /// <summary>A database read whole (§1 fact 5): TSqlModel.LoadFromDatabase as the target's identity, then io/Ssdt's walk; the run's log, when given, holds the statement estate sends first.</summary>
    public static Result<Seq<Element>> Model(Database target, QueryLog? log = null) => Reached(target, log).Bind(_ =>
    {
        TSqlModel model;
        try
        {
            model = TSqlModel.LoadFromDatabase(target.Connection, new ModelExtractOptions());
        }
        catch (Exception e) when (e is DacServicesException or DacModelException or SqlException or InvalidOperationException)
        {
            return target.Refused(e);
        }

        using (model)
        {
            return Ssdt.Walk(model);
        }
    });

    /// <summary>What DacFx would deploy: its script, kept with the SQLCMD variables whose values a reference holds unset, and its report.</summary>
    public sealed record Deployment(string Script, string Report)
    {
        private static readonly XNamespace Dac = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

        /// <summary>The report's operations; none is convergence (§1 fact 4).</summary>
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
    /// A statement the allowlist admitted (VALUES.md P2), as ScriptDom writes it back, so what runs is what was checked, with no comment
    /// and no batch separator; and the claim site it measures. Only Of makes one, and the executor runs nothing else.
    /// </summary>
    public sealed class Probe
    {
        private Probe(string statement, string site) => (Statement, Site) = (statement, site);

        public string Statement { get; }

        public string Site { get; }

        public static Result<Probe> Of(string text, string site) => Allowlist.Admitted(text).Map(statement => new Probe(statement, site));

        public override string ToString() => Site + ": " + Statement;
    }

    /// <summary>What a probe measured: its rows, every value an integer or null; or its failure, the number and the site, SQL Server's message kept for a copy alone.</summary>
    public abstract record Measurement
    {
        private Measurement()
        {
        }

        public sealed record Answered(string Site, IReadOnlyList<IReadOnlyList<long?>> Rows) : Measurement;

        public sealed record Failed(string Site, int Number, string? Message) : Measurement
        {
            public override string ToString() =>
                Site + ": probe failed: Msg " + Number.ToString(CultureInfo.InvariantCulture) + (Message is null ? "; message withheld" : ": " + Message);
        }
    }

    /// <summary>
    /// The probe executor (WP 1.4): one admitted statement against the target, read back as integers, and logged with its row count. A
    /// statement that fails is measured as failed, by its number; a connection that fails is a refusal.
    /// </summary>
    public static Result<Measurement> Measure(Database target, Probe probe, QueryLog log)
    {
        using var connection = new SqlConnection(target.Connection);
        try
        {
            connection.Open();
            using var command = new SqlCommand(probe.Statement, connection) { CommandTimeout = 30 };
            using var reader = command.ExecuteReader();
            var rows = new List<IReadOnlyList<long?>>();
            while (reader.Read())
            {
                rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? (long?)null : Integer(reader.GetValue(i)))]);
            }

            log.Add(target, probe, rows.Count == 1 ? "1 row" : rows.Count.ToString(CultureInfo.InvariantCulture) + " rows");
            return new Measurement.Answered(probe.Site, rows);
        }
        catch (SqlException e) when (connection.State == System.Data.ConnectionState.Open && e.Class < 20)
        {
            log.Add(target, probe, "failed, Msg " + e.Number.ToString(CultureInfo.InvariantCulture));
            return new Measurement.Failed(probe.Site, e.Number, target.Withheld ? null : e.Message);
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            return target.Refused(e);
        }
    }

    /// <summary>
    /// A run's log of every statement estate sends, .estate/runs/&lt;id&gt;/queries.log: each probe, and the one statement Model and Plan send
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

        internal void Add(Database target, Probe probe, string outcome) => Add(target, probe.Site, probe.Statement, outcome);

        internal void Add(Database target, string site, string statement, string outcome)
        {
            lock (gate)
            {
                entries.Append("-- ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append(' ').Append(target.Where).Append(' ')
                    .Append(site).Append(": ").Append(outcome).Append('\n').Append(statement).Append("\nGO\n");
                Write.Text(Path, entries.ToString());
            }
        }
    }

    /// <summary>
    /// A reference's connection (§4 row 14, VALUES.md X1): env:NAME's variable or file:path's text, a relative path read from the estate's
    /// root, parsed by SqlClient's own grammar; the caller's integrated identity when it names no other; estate as the application unless
    /// it names one. A refusal names the reference and quotes nothing it read.
    /// </summary>
    internal static Result<string> Connect(string subject, SecretReference reference, string estateRoot) => Read(reference, estateRoot) is not { } text
        ? new Refusal("connection.unresolved", subject + " resolves to nothing here.", "Set the variable, or write the file outside git, that " + reference + " names.")
        : Parsed(subject, reference, text).Bind(connection => connection.InitialCatalog.Length == 0
            ? new Refusal("connection.malformed", subject + " names no database; Model, Plan and the executor read the database it names.",
                "Give the connection string an Initial Catalog, in the place " + reference + " names.")
            : Result.Ok(Identified(connection)));

    /// <summary>An environment's server as R15 reads it, a database named or not: null when its reference resolves to nothing here; refused when SqlClient reads nothing from it.</summary>
    internal static Result<string?> DataSource(NamedEnvironment environment, string estateRoot) => Read(environment.Connection, estateRoot) is not { } text
        ? Result.Ok<string?>(null)
        : Parsed(Named.Subject(environment), environment.Connection, text).Map(connection => (string?)connection.DataSource);

    /// <summary>A reference's text as SqlClient's own grammar reads it; the refusal names the reference and quotes nothing it read.</summary>
    private static Result<SqlConnectionStringBuilder> Parsed(string subject, SecretReference reference, string text)
    {
        try
        {
            return new SqlConnectionStringBuilder(text);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException)
        {
            return new Refusal("connection.malformed", subject + " is no connection string SqlClient reads; its text is withheld.",
                "Correct the connection string in the place " + reference + " names.");
        }
    }

    /// <summary>The connection as estate opens it: the caller's integrated identity when it names no other, and estate as the application unless it names one.</summary>
    private static string Identified(SqlConnectionStringBuilder connection)
    {
        if (!connection.ShouldSerialize("Integrated Security") && connection.UserID.Length == 0 && connection.Authentication == SqlAuthenticationMethod.NotSpecified)
        {
            connection.IntegratedSecurity = true;
        }

        if (!connection.ShouldSerialize("Application Name"))
        {
            connection.ApplicationName = "estate";
        }

        return connection.ConnectionString;
    }

    /// <summary>What a reference names: the variable's value, or the file's text trimmed; null when there is none. The attempt is recorded in the run's Reads.</summary>
    internal static string? Read(SecretReference reference, string estateRoot)
    {
        Reads.Record();
        try
        {
            return reference.Match(
                variable => System.Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : null,
                file => System.IO.Path.Combine(estateRoot, file) is var path && File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } text ? text : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether this run has read a connection or other reference of a named environment (VALUES.md X2), whose text an exception's message
    /// can then quote. SqlServer.Read records each read, and every one goes through it: Named.Of, which SqlServer.Resolve calls for env:;
    /// R15's read of each environment's connection in io/Substrate, which copy: and a new copy run; and a named environment's SQLCMD values.
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

    /// <summary>
    /// A server's host as R15 spells it: the data source with its protocol, port and instance set aside, in lower case; this machine,
    /// however spelled (localhost, 127.0.0.1, ::1, '.', (local), its own name, or none, SqlClient's local default), is localhost.
    /// </summary>
    public static string Host(string dataSource)
    {
        var host = Regex.Replace(dataSource.Trim(), @"\A(?:tcp|np|lpc|admin):", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        host = host.StartsWith(@"\\", StringComparison.Ordinal) ? host[2..].Split('\\')[0] : host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) ? "(localdb)" : host;
        host = host.Split(',')[0].Split('\\')[0].Trim().Trim('[', ']').ToLowerInvariant();
        return host is "" or "localhost" or "127.0.0.1" or "::1" or "." or "(local)" || host == System.Environment.MachineName.ToLowerInvariant() ? "localhost" : host;
    }

    private static Refusal NotADatabase(Target target) => new Refusal("target.not-a-database",
        target + " is read as a package; Model, Plan and the executor read a database, env:<name> or copy:<name>.", "Name the database as env:<name> or copy:<name>.");

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
            return held ? Result.Ok(target) : target.Refused(300, "");
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            return target.Refused(e);
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
            return new Refusal("package.unreadable", dacpac + " is not a package DacFx reads: " + e.Message, "Name a .dacpac a build wrote, or build its project again.");
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
                return target.Refused(e);
            }
        }
    }

    /// <summary>A named environment's own SQLCMD values, each literal as the posture gives it and each reference resolved in memory; a copy has none.</summary>
    private static Result<List<SqlCmdValue>> Values(Database target) => target is not Named named ? new List<SqlCmdValue>()
        : named.Environment.SqlCmd.Aggregate(Result.Ok(new List<SqlCmdValue>()), (all, variable) => all.Bind(list => variable.Match(
            literal => Result.Ok<List<SqlCmdValue>>([.. list, new(variable.Name, literal, false)]),
            reference => Read(reference, named.Root) is { } value ? Result.Ok<List<SqlCmdValue>>([.. list, new(variable.Name, value, true)])
                : new Refusal("sqlcmd.unresolved", named + "'s $(" + variable.Name + ") names " + reference + ", which resolves to nothing here.",
                    "Set the variable, or write the file outside git, that " + reference + " names."))));

    /// <summary>A value the allowlist admits the type of: an integer of any width. Anything else is a defect in the allowlist, named by its type alone.</summary>
    private static long Integer(object value) => value switch
    {
        int or long or short or byte => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException("A probe answered with a " + value.GetType().Name + ", a type no form of the allowlist yields."),
    };

    /// <summary>
    /// The probe allowlist, closed (WP 1.4): one SELECT whose outermost select list holds COUNT or COUNT_BIG of * or of DISTINCT a
    /// column, SUM(CASE WHEN … THEN 1 ELSE 0 END), MIN or MAX over LEN or DATALENGTH of a column, CASE WHEN EXISTS (…) THEN 1 ELSE 0 END
    /// or an integer literal; beneath it, names of one or two parts, TRY_ conversions, and the few functions held here. A boundary is a
    /// length or a literal, never read from the data: each predicate, in a join's ON as in a WHERE, reads at most one value from the data
    /// (a column, a length or an aggregate), save = or &lt;&gt; between two columns, an equi-join or an orphan check; and a subquery's
    /// select list carries only columns, literals and the answers above, so a derived column is never two values combined. Every other
    /// form is refused with where it stands and what it is, and none of the probe's literals is quoted.
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
                return new Refusal("probe.refused", string.Create(CultureInfo.InvariantCulture, $"The probe does not parse at line {errors[0].Line}, column {errors[0].Column}."),
                    "Correct the probe's syntax at that place.");
            }

            var statements = ((TSqlScript)parsed).Batches.SelectMany(b => b.Statements).ToList();
            if (statements.Count != 1)
            {
                return new Refusal("probe.refused", string.Create(CultureInfo.InvariantCulture, $"The probe holds {statements.Count} statements; the executor runs one statement at a time."),
                    "Split it into probes of one SELECT each.");
            }

            if ((statements[0] is SelectStatement select ? Statement(select) : new Offence(statements[0], Kind(statements[0]))) is { } offence)
            {
                return new Refusal("probe.refused", string.Create(CultureInfo.InvariantCulture, $"The probe is refused at line {offence.At.StartLine}, column {offence.At.StartColumn}: {offence.What}."),
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

        /// <summary>A literal of the kinds a probe may write: a number, a string, a binary value or NULL.</summary>
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
