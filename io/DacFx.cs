using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Estate.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;

namespace Estate.Io;

/// <summary>
/// The one adapter to DacFx's runtime surface (DacServices and DacProfile's options): the release estate runs, made once; a database
/// extracted into a package, in one read; the plan of one package against another, which connects to nothing; a publish to a copy; the
/// deploy report read into the kernel's DeployReport; and every DacFx failure mapped once, a SqlException or a quoted SQL Server number
/// handed to the SQL Server adapter's classification and the rest named here. DacFx's own connections and statements are DacFx's: estate
/// logs its own (io/SqlServer's query log), and the Extended Events test watches DacFx's.
/// </summary>
public static class DacFx
{
    /// <summary>How long DacFx may run one of its catalog queries while it extracts a database: its own default, named here.</summary>
    internal static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long DacFx may run an extract's long-running statements; 0 is DacFx's default, no limit, so the run's interruption is what stops one.</summary>
    internal static readonly TimeSpan LongRunningTimeout = TimeSpan.Zero;

    /// <summary>How long DacFx waits for a lock on the database it extracts: its own default, named here.</summary>
    internal static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The text DacFx's plan gives, in the exception's chain, when a package's platform is newer than the target's and the profile forbids it.</summary>
    private const string PlatformRefused = "as the target platform cannot be published to";

    private static readonly XNamespace Dac = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    /// <summary>
    /// The DacFx release estate runs, made once from Microsoft.SqlServer.Dac.dll: its file version as major.minor.build (170.5.96), or, where
    /// the assembly has no file on disk (a single-file or bundled host), its informational version before the '+'; toolchain.dacfx-version
    /// when it carries neither. doctor reports that error as an item, and every other verb answers it before any work.
    /// </summary>
    public static Result<DacFxVersion> Version { get; } = VersionOf(typeof(DacServices).Assembly.Location,
        typeof(DacServices).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// How a database is extracted, each option with its reason, so a database reads as a package built from it does. DacFx 170.5.96's
    /// defaults are noted; IgnorePermissions is the one this changes.
    /// </summary>
    internal static DacExtractOptions Extraction => new()
    {
        // A package keeps its GRANT, DENY and REVOKE statements and Ssdt.ReadModel keys each one; the default, true, drops every permission.
        IgnorePermissions = false,
        // A package keeps its sp_addextendedproperty values (MS_Description); the default, false, keeps them.
        IgnoreExtendedProperties = false,
        // A package keeps CREATE USER ... FOR LOGIN; the default, false, keeps a user's login.
        IgnoreUserLoginMappings = false,
        // A package holds database-scoped objects; the default, true, leaves out server-scoped ones a user does not reference.
        ExtractApplicationScopedObjectsOnly = true,
        // The default, true, reads the login a user maps to; SQL Server shows it only to a reader with permission on the login.
        ExtractReferencedServerScopedElements = true,
        // Table.RowCount, the data and index sizes and the page counts change with the rows and not the schema; the default is false.
        ExtractUsageProperties = false,
        // No row of any table is read; the default is false.
        ExtractAllTableData = false,
        // Verification validates the model as a package build would; Ssdt.ReadModel reads what the database holds, valid or not. Default false.
        VerifyExtraction = false,
        // The model is held in memory, as a package's is; the default is Memory.
        Storage = DacSchemaModelStorageType.Memory,
        // DacFx's log is not kept, so hashing the names in it changes nothing; the default is false.
        HashObjectNamesInLogs = false,
        CommandTimeout = (int)QueryTimeout.TotalSeconds,
        LongRunningCommandTimeout = (int)LongRunningTimeout.TotalSeconds,
        DatabaseLockTimeout = (int)LockTimeout.TotalSeconds,
    };

    /// <summary>The release from the assembly's path and its informational version, as <see cref="Version"/> reads them.</summary>
    internal static Result<DacFxVersion> VersionOf(string location, string? informational) =>
        location.Length > 0 && File.Exists(location) && ReleaseOf(location) is { } release ? release
        : informational?.Split('+')[0] is { Length: > 0 } text ? DacFxVersion.Of(text)
        : new Error("toolchain.dacfx-version", "Microsoft.SqlServer.Dac.dll carries no file version and no informational version, so estate cannot name the DacFx release it runs.",
            "Run estate from a tool folder ci/publish wrote, whose DacFx assemblies carry their versions.");

    /// <summary>The DacFx release an assembly on disk is, from its file version as major.minor.build; null when the file carries no file version.</summary>
    internal static Result<DacFxVersion>? ReleaseOf(string path) => FileVersionInfo.GetVersionInfo(path) is { FileVersion: not null } file
        ? DacFxVersion.Of(string.Create(CultureInfo.InvariantCulture, $"{file.FileMajorPart}.{file.FileMinorPart}.{file.FileBuildPart}"))
        : null;

    /// <summary>
    /// The database read once (R1, DECISIONS.md 2026-09-25): DacServices.Extract of its schema into a package in memory, as the target's
    /// identity, under <see cref="Extraction"/>, then opened as a package is, named for its target. The run's interruption and the caller's
    /// token stop it, and that stop reaches the caller as the interruption; any other failure is mapped as a failure against the target.
    /// </summary>
    public static Result<Ssdt.Package> Extract(SqlServer.Database target, CancellationToken cancel = default)
    {
        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(cancel, Interruption.RunToken);
        using var package = new MemoryStream();
        return Guard(() =>
            {
                new DacServices(target.Connection).Extract(package, target.Catalog, "estate", typeof(DacFx).Assembly.GetName().Version ?? new Version(1, 0), "", null, Extraction,
                    interrupted.Token);
                return package.ToArray();
            }, failure => Failed(target, failure), interrupted.Token)
            .Bind(bytes => Ssdt.Open(bytes, target.Target.ToString()));
    }

    /// <summary>
    /// The plan of <paramref name="source"/> against <paramref name="target"/>, package to package (DacServices.Script with two packages, which
    /// connects to nothing), under the profile's options and the SQLCMD values given, both outputs generated, planned as the database
    /// <paramref name="databaseName"/>. Each variable the source declares must have a value from the profile or <paramref name="values"/>,
    /// or it is sqlcmd.undefined before anything is planned (DF-9); a value for a variable the source does not declare is the note
    /// sqlcmd.undeclared. The script is kept without the :setvar line of each value a reference gave. A package whose collation ignores case
    /// against a target whose collation does not is plan.collation, the check DacFx makes of a live plan (<see cref="Verified"/>); a package of
    /// a newer platform than the target's under AllowIncompatiblePlatform False is plan.platform; every other failure is dacfx.failed.
    /// </summary>
    internal static Result<Io.Plan> Plan(Ssdt.Package source, Ssdt.Package target, string databaseName, PublishProfile.Strict profile, IReadOnlyList<SqlCmdValue> values)
    {
        var given = profile.SqlCmd.Select(v => v.Name).Concat(values.Select(v => v.Name)).ToHashSet();
        if (source.Declared.Where(name => !given.Contains(name)).ToList() is { Count: > 0 } undefined)
        {
            return new Error("sqlcmd.undefined", source.Source + " declares " + string.Join(", ", undefined.Select(n => SqlCmdVariable.Placeholder(n.ToString())))
                + ", and neither " + profile.Source + " nor the environment gives " + (undefined.Count == 1 ? "it" : "them") + " a value.",
                "Give " + string.Join(", ", undefined) + " a value in the environment's sqlcmd in " + Posture.Json + ", or in the pipeline's profile.");
        }

        var options = profile.Options();
        foreach (var value in values)
        {
            options.SqlCommandVariableValues[value.Name.ToString()] = value.Text;
        }

        var notes = given.Where(name => !source.Declared.Contains(name)).OrderBy(name => name).Select(name => Finding.Note("sqlcmd.undeclared", SqlCmdVariable.Placeholder(name.ToString()),
            source.Source + " declares no " + SqlCmdVariable.Placeholder(name.ToString()) + ", so the value given for it reaches no script.")).ToList();
        return source.Elements.Bind(from => target.Elements.Bind(to => Verified(source, target, from.Elements, to.Elements).Bind(_ =>
            Guard(() => DacServices.Script(source.Dac, target.Dac, databaseName,
                    new PublishOptions { GenerateDeploymentScript = true, GenerateDeploymentReport = true, DeployOptions = options }),
                failure => Unplanned(failure, source, target))
            .Bind(planned => Report(planned.DeploymentReport, from.Elements, to.Elements)
                .Map(report => new Io.Plan(report.Report, SqlCmdVariable.Unset(planned.DatabaseScript, values.Where(v => v.Referenced).Select(v => v.Name.ToString())),
                    [.. notes, .. report.Notes]))))));
    }

    /// <summary>
    /// The check DacFx makes of a live plan and skips package to package (measured on DacFx 170.5.96): a model whose collation ignores case,
    /// planned against a database whose collation does not, is refused with Error SQL72030, whether or not any name differs in case; package
    /// to package, the same plan comes back empty. Each package's collation is its DatabaseOptions.Collation.
    /// </summary>
    private static Result<Collation> Verified(Ssdt.Package source, Ssdt.Package target, SortedArray<Element> from, SortedArray<Element> to) =>
        Ssdt.CollationOf(from).Bind(model => Ssdt.CollationOf(to).Bind(database => !model.IsCaseSensitive && database.IsCaseSensitive
            ? new Error("plan.collation", source.Source + " compares names ignoring case (" + model + ") and " + target.Source + " compares them with case (" + database
                + "); DacFx refuses a live plan of such a model against such a database (Error SQL72030), so the pipeline's deploy stops there.",
                "Set the project's DefaultCollation to a collation that compares names with case, such as " + database + ", the database's.")
            : Result.Ok(database)));

    /// <summary>
    /// The package published to the copy under the profile's options, Strict or the copy's Permissive (§2.1 rule 3), both outputs generated:
    /// what DacFx deployed, its report and its script. DacFx's error and warning messages during the call are kept with its failure, and its
    /// informational ones (a deployment script's PRINT output, "Altering Table [dbo].[T]...") are not; a failed publish of a profile with
    /// IncludeTransactionalScripts False says the copy may hold part of the change. Only a Copy is taken, so a named environment is never
    /// published to.
    /// </summary>
    public static Result<Published> Publish(SqlServer.Copy copy, Ssdt.Package package, PublishProfile profile)
    {
        var services = new DacServices(copy.Connection);
        var messages = new List<DacMessage>();
        services.Message += (_, e) => messages.Add(e.Message);
        var options = profile.Options();
        return Guard(() => services.Publish(package.Dac, copy.Name.ToString(), new PublishOptions { GenerateDeploymentReport = true, GenerateDeploymentScript = true, DeployOptions = options }),
                failure => Failed(copy, failure with { Messages = SortedArray.Of(failure.Messages.Concat(Kept(messages)).Distinct()) }, options.IncludeTransactionalScripts))
            .Bind(published => package.Elements.Bind(elements => Report(published.DeploymentReport, elements.Elements, [])).Map(report => new Published(report.Report, published.DatabaseScript)));
    }

    /// <summary>
    /// A deploy report's XML, read once into the kernel's DeployReport (§1 fact 4): each Alert's Issue as a PlanAlert, and each Operation's
    /// Item as a PlanOperation keyed as io/Ssdt.ReadModel keys the element, against the two element sets the plan compared (a renamed column
    /// is in the source, a dropped table in the target); a serialized type the map does not hold keys as itself, with the note
    /// plan.unlisted-type. XML of another shape than DacFx 170.5.96 writes is plan.report-unread, naming what was not expected.
    /// </summary>
    internal static Result<(DeployReport Report, IReadOnlyList<Finding> Notes)> Report(string xml, SortedArray<Element> source, SortedArray<Element> target)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(xml).Root!;
        }
        catch (XmlException e)
        {
            return Unread("the report is not XML: " + e.Message);
        }

        var keys = new Keys(source, target);
        var unexpected = root.Name != Dac + "DeploymentReport" ? root.Name.LocalName
            : root.Elements().FirstOrDefault(e => e.Name != Dac + "Alerts" && e.Name != Dac + "Operations")?.Name.LocalName
            ?? root.Elements(Dac + "Alerts").Elements().FirstOrDefault(e => e.Name != Dac + "Alert" || e.Elements().Any(i => i.Name != Dac + "Issue"))?.Name.LocalName
            ?? root.Elements(Dac + "Operations").Elements().FirstOrDefault(e => e.Name != Dac + "Operation" || e.Elements().Any(i => i.Name != Dac + "Item"
                || i.Elements().Any(issue => issue.Name != Dac + "Issue")))?.Name.LocalName;
        if (unexpected is not null)
        {
            return Unread("it holds the element " + unexpected + " where DacFx 170.5.96 writes none");
        }

        var alerts = root.Elements(Dac + "Alerts").Elements(Dac + "Alert").SelectMany(alert => alert.Elements(Dac + "Issue").Select(issue =>
            new PlanAlert(PlanAlertKind.Of((string?)alert.Attribute("Name") ?? ""), Number((string?)issue.Attribute("Id")), (string?)issue.Attribute("Value") ?? "")));
        var items = root.Elements(Dac + "Operations").Elements(Dac + "Operation").SelectMany(operation => operation.Elements(Dac + "Item").Select(item =>
            (Kind: PlanOperationKind.Of((string?)operation.Attribute("Name") ?? ""), Type: (string?)item.Attribute("Type") ?? "", Name: (string?)item.Attribute("Value") ?? "",
                Issues: SortedArray.Of(item.Elements(Dac + "Issue").Select(i => Number((string?)i.Attribute("Id"))).OfType<int>())))).ToList();
        return Result.All(items.Select(i => keys.Of(i.Type, i.Name).Map(key => new PlanOperation(i.Kind, key, i.Issues))))
            .Map(operations => (new DeployReport(SortedArray.Of(operations), SortedArray.Of(alerts)), (IReadOnlyList<Finding>)[.. items.Select(i => i.Type).Distinct()
                .Where(type => ModelTypes.Element(type) is null).Order(StringComparer.Ordinal)
                .Select(type => Finding.Note("plan.unlisted-type", type, "The deploy report names the type " + type + ", which estate's map of DacFx's types does not hold; its items are keyed by that name."))]));

        static Error Unread(string why) => new Error("plan.report-unread", "DacFx's deploy report is not one estate reads: " + why + ".",
            "Report this error with the DacFx release estate runs; the report's shape is DacFx's.");
    }

    /// <summary>A DacFx failure: the error and warning messages it carries, the SqlException inside it, if any, and the chain's text on one line, each exception's once.</summary>
    internal sealed record DacFxFailure(SortedArray<DacFxMessage> Messages, SqlException? Sql, string Text);

    /// <summary>A failure's messages, as DacServicesException and DacModelException carry them, else parsed from the text of each exception of the chain.</summary>
    internal static DacFxFailure Failure(Exception failure) =>
        Failure(Chain(failure).OfType<DacServicesException>().SelectMany(x => x.Messages), failure, Chain(failure).OfType<DacModelException>().SelectMany(x => x.Messages)
            .Select(m => new DacFxMessage(Type(m.MessageType), m.Prefix, m.Number, m.Message, null)));

    /// <summary>A failure whose messages DacFx gave as <paramref name="messages"/>; a message of type Message (a status line or PRINT output) is left out.</summary>
    internal static DacFxFailure Failure(IEnumerable<DacMessage> messages, Exception chain) => Failure(messages, chain, []);

    private static DacFxFailure Failure(IEnumerable<DacMessage> messages, Exception chain, IEnumerable<DacFxMessage> model)
    {
        var said = Chain(chain).Select(x => Regex.Replace(x.Message, @"\s*\n\s*", " ", RegexOptions.CultureInvariant).Trim()).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var given = Kept(messages).Concat(model.Where(m => m.MessageType != DacFxMessageType.Message)).ToList();
        var parsed = given.Count > 0 ? given : Chain(chain).SelectMany(x => x.Message.Split('\n')).Select(line => DacFxMessage.Parse(line.Trim())).OfType<DacFxMessage>()
            .Where(m => m.MessageType != DacFxMessageType.Message);
        return new DacFxFailure(SortedArray.Of(parsed.Distinct()), Chain(chain).OfType<SqlException>().FirstOrDefault(), string.Join(' ', said));
    }

    /// <summary>
    /// The error a DacFx failure against a database becomes: a SqlException inside it, or a SQL Server number one of its messages quotes (SQL72014's
    /// Msg 50000), is that number's, classified by the SQL Server adapter, SQL Server's words withheld for a named environment; anything else is
    /// dacfx.failed quoting DacFx's messages, or the chain's text where it gives none.
    /// </summary>
    internal static Error Failed(SqlServer.Database target, DacFxFailure failure, bool transactional = true) =>
        failure.Sql is { } sql ? target.ErrorOf(sql.Number, sql.Message, fatal: sql.Class >= 20)
        : failure.Messages.FirstOrDefault(m => m.SqlServerNumber is not null) is { SqlServerNumber: { } number } ? target.ErrorOf(number, Quoted(failure) + (transactional ? "" : Partial), fatal: false)
        : new Error("dacfx.failed", "DacFx failed against " + target.Target + " with no SQL Server error inside: " + Quoted(failure) + (transactional ? "" : Partial),
            "Correct what DacFx names in the project or the publish profile, then run the step again.");

    /// <summary>The error of a DacFx failure against a database, for the exception DacFx threw.</summary>
    public static Error Failed(SqlServer.Database target, Exception failure) => Failed(target, Failure(failure));

    /// <summary>
    /// A DacFx call, its failure mapped by <paramref name="failed"/>: the one list of what DacFx throws, written once. A cancellation is not
    /// caught, so the interruption reaches the caller, and a failure DacFx raises while the token given is cancelled is that cancellation.
    /// </summary>
    internal static Result<T> Guard<T>(Func<T> call, Func<DacFxFailure, Error> failed, CancellationToken cancel = default)
    {
        try
        {
            return call();
        }
        catch (Exception e) when (e is DacServicesException or DacModelException or SqlException or IOException or UnauthorizedAccessException or InvalidDataException
            or XmlException or ArgumentException or FormatException or InvalidOperationException)
        {
            cancel.ThrowIfCancellationRequested();
            return failed(Failure(e));
        }
    }

    /// <summary>
    /// The error of a plan DacFx failed to make, package to package: plan.platform when the chain's text says the source's platform cannot
    /// be published to the target's (DacFx gives that reason in no message, measured); else dacfx.failed, quoting DacFx's messages.
    /// </summary>
    internal static Error Unplanned(DacFxFailure failure, Ssdt.Package source, Ssdt.Package target) => failure.Text.Contains(PlatformRefused, StringComparison.Ordinal)
        ? new Error("plan.platform", "The package targets " + source.Platform + " and " + target.Source + " is " + target.Platform + "; the profile sets AllowIncompatiblePlatform False.",
            "Set the project's target platform (its DSP) to " + target.Platform + ", the platform " + target.Source + " runs.")
        : Unsourced(target.Source, failure);

    /// <summary>What a publish to a copy leaves when its profile runs no transaction around the change.</summary>
    private const string Partial = " The copy may hold part of the change, since the profile sets IncludeTransactionalScripts False.";

    /// <summary>DacFx's failure against what no database names: dacfx.failed, quoting its messages.</summary>
    private static Error Unsourced(string target, DacFxFailure failure) => new Error("dacfx.failed", "DacFx failed against " + target + " with no SQL Server error inside: " + Quoted(failure),
        "Correct what DacFx names in the project or the publish profile, then run the step again.");

    /// <summary>A failure as its error quotes it: each message once, on one line; the chain's text where it has none.</summary>
    private static string Quoted(DacFxFailure failure) => failure.Messages.Count > 0 ? string.Join(' ', failure.Messages.Select(m => m.ToString())) : failure.Text;

    /// <summary>DacFx's error and warning messages, as the kernel's.</summary>
    private static IEnumerable<DacFxMessage> Kept(IEnumerable<DacMessage> messages) => messages.Where(m => m.MessageType != DacMessageType.Message)
        .Select(m => new DacFxMessage(Type(m.MessageType), m.Prefix, m.Number, Regex.Replace(m.Message, @"\s*\n\s*", " ", RegexOptions.CultureInvariant).Trim(), null));

    private static DacFxMessageType Type(DacMessageType type) => type switch
    {
        DacMessageType.Error => DacFxMessageType.Error,
        DacMessageType.Warning => DacFxMessageType.Warning,
        _ => DacFxMessageType.Message,
    };

    /// <summary>An exception and each exception inside it, outermost first.</summary>
    private static IEnumerable<Exception> Chain(Exception failure)
    {
        for (var x = failure; x is not null; x = x.InnerException)
        {
            yield return x;
        }
    }

    private static int? Number(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>
    /// A report item's key, found against the two element sets the plan compared: the element of either set with the item's type and name;
    /// failing that, a name of three or more parts whose first two parts are an element's path, keyed under that element with the rest, as
    /// io/Ssdt.ReadModel keys a composed object; failing that, the first two parts at the top and each further part a level down.
    /// </summary>
    private sealed class Keys(SortedArray<Element> source, SortedArray<Element> target)
    {
        private readonly Dictionary<(string Type, string Path), ElementKey> named = source.Concat(target).Select(e => e.Key)
            .GroupBy(k => (k.Type, Path(k))).ToDictionary(g => g.Key, g => g.First());

        private readonly Dictionary<string, ElementKey> top = source.Concat(target).Select(e => e.Key).Where(k => k.Parent is null && k.Name.Schema is not null)
            .GroupBy(Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(k => k.Type, StringComparer.Ordinal).First(), StringComparer.Ordinal);

        public Result<ElementKey> Of(string serialized, string name)
        {
            var type = ModelTypes.Element(serialized) ?? serialized;
            string[] parts = TSql.NameParts(name) ?? [name];
            var path = string.Join('\u0001', parts);
            return named.TryGetValue((type, path), out var key) ? key
                : parts.Length >= 3 && top.TryGetValue(string.Join('\u0001', parts[..2]), out var home) ? ModelTypes.Key(type, parts[2..], home)
                : ModelTypes.Key(type, parts, null);
        }

        /// <summary>A key's name parts, one level after another, joined by a character no name part holds.</summary>
        private static string Path(ElementKey key) => (key.Parent is { } parent ? Path(parent) + '\u0001' : "") + (key.Name.Schema is { } schema ? schema + '\u0001' : "") + key.Name.Base;
    }
}

/// <summary>What a plan of one package against another holds: the deploy report, the script as kept, and the notes the plan raises.</summary>
public sealed record Plan(DeployReport Report, string Script, IReadOnlyList<Finding> Notes);

/// <summary>What DacFx deployed to a copy: its deploy report and its script (the record M4's proof keeps).</summary>
public sealed record Published(DeployReport Report, string Script);

/// <summary>
/// A SQLCMD value a plan sets: its variable, its text, and whether a reference gave it, so the kept script leaves its :setvar out. The text
/// may be what a reference resolved to, so the value prints its name alone.
/// </summary>
internal sealed record SqlCmdValue(SqlCmdName Name, string Text, bool Referenced)
{
    public override string ToString() => SqlCmdVariable.Placeholder(Name.ToString()) + (Referenced ? " from a reference" : ", a literal");
}
