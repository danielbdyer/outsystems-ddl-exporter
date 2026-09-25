using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Permission = Microsoft.SqlServer.Dac.Model.Permission;

namespace Estate.Io;

/// <summary>
/// The SSDT project and its package, read whole (V3_MILESTONES.md §2.2): Build runs the project's own build against the
/// published tool folder's DacFx targets (§1 fact 1), Load reads what the build wrote, RefactorLog reads a refactorlog, and Walk
/// reads a package or a model into kernel Elements. A refusal's code names what was refused; cli/Contract.cs maps its area to the exit.
/// </summary>
/// <remarks>
/// No Visual Studio fallback: S1's windows-latest half answered that the committed route builds a classic project there.
/// Visual Studio's MSBuild with its own SSDT targets, found through vswhere and stamped as that engine, lands only if S1's
/// laptop half finds the estate's project cannot build this way (WP 1.1).
/// </remarks>
public static class Ssdt
{
    /// <summary>What a build wrote: the package, and the fingerprint of the inputs whose folder holds it.</summary>
    public sealed record Dacpac(string Path, Fingerprint Inputs);

    /// <summary>
    /// A package as DacFx reads it: the model; the pre- and post-deploy scripts as the build inlined them, null when the
    /// project has none; its refactorlog's entries in file order; and, for each name model.xml gives exactly one element,
    /// that element's serialized type (SqlTable, SqlSimpleColumn), the vocabulary a refactorlog entry names its element in.
    /// Disposing it releases the model.
    /// </summary>
    public sealed record Package(TSqlModel Model, string? PreDeploy, string? PostDeploy, IReadOnlyList<RefactorEntry> Refactors, IReadOnlyDictionary<string, string> Serialized) : IDisposable
    {
        public void Dispose() => Model.Dispose();
    }

    /// <summary>
    /// One refactorlog operation as SSDT wrote it: its key; its name (Rename Refactor, Move Schema); its ChangeDateTime as
    /// written; the element it acts on and that element's type; the parent and its type, where the element has one; and the
    /// new name of a rename or the new schema of a move.
    /// </summary>
    public sealed record RefactorEntry(
        string Key, string Operation, string? ChangeDateTime, string ElementName, string? ElementType, string? ParentName, string? ParentType, string? NewName, string? NewSchema);

    private static readonly XNamespace Dac = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    /// <summary>The line in which the SqlTasks targets name the package they wrote; minimal verbosity prints it.</summary>
    private static readonly Regex Wrote = new(@" -> (?<path>.+\.dacpac)\r?$", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>An MSBuild error: its origin (a file and its position, or a tool), an optional subcategory, the code and the text, then the project in brackets.</summary>
    private static readonly Regex Error = new(
        @"^\s*(?<origin>.+?)(?<position>\(\d+(?:,\d+)*\))?\s*:\s*(?:[\w ]+ )?error (?<code>[A-Za-z]+\d+)\s*:\s*(?<text>.*?)(?:\s+\[[^\]]*\])?\s*$",
        RegexOptions.CultureInvariant);

    /// <summary>The engine's files a build's output depends on besides the project: the targets and the build task.</summary>
    private static readonly string[] Engine = ["Microsoft.Data.Tools.Schema.SqlTasks.targets", "Microsoft.Data.Tools.Schema.Tasks.Sql.dll"];

    public static Result<string> Tool() => Tool(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("ESTATE_TOOL"), Directory.GetCurrentDirectory());

    /// <summary>
    /// The tool folder: the one estate runs from, when it carries the targets; else the one ESTATE_TOOL names; else dist/estate/
    /// in the nearest directory at or above the working directory, as in a clone of the engine that ci/publish has run in.
    /// </summary>
    public static Result<string> Tool(string running, string? variable, string workingDirectory) =>
        Doctor.Tool(running).Remedy is null ? running
        : !string.IsNullOrEmpty(variable) && Doctor.Tool(variable) is { Remedy: { } publish } named
            ? new Refusal("tool.missing", "ESTATE_TOOL names " + variable + ", which is " + named.Found + ".", publish + ", and set ESTATE_TOOL to it or unset it; then estate doctor")
        : !string.IsNullOrEmpty(variable) ? variable
        : Nearest(new DirectoryInfo(workingDirectory)) is { } nearest ? nearest
        : new Refusal(
            "tool.missing",
            "estate does not run from a published tool folder, ESTATE_TOOL is unset, and no dist/estate/ lies at or above " + workingDirectory + ".",
            "run ci/publish.sh, or ci/publish.ps1 on Windows, in a clone of the engine, or set ESTATE_TOOL to a published tool folder; then estate doctor");

    /// <summary>dist/estate/ in the nearest directory at or above <paramref name="directory"/> where that is a published tool folder, else null.</summary>
    private static string? Nearest(DirectoryInfo? directory) => directory is null ? null
        : Path.Combine(directory.FullName, "dist", "estate") is var tool && Doctor.Tool(tool).Remedy is null ? tool : Nearest(directory.Parent);

    /// <summary>The project a ref's worktree holds, as a path from its root: the one named, else its one .sqlproj outside hidden folders, bin/ and obj/.</summary>
    public static Result<string> Project(string worktree, string? named)
    {
        List<string> found = named is not null ? [named] : Directory.EnumerateFiles(worktree, "*.sqlproj", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(worktree, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').SkipLast(1).Any(folder => folder is "bin" or "obj" || folder.StartsWith('.'))).Order(StringComparer.Ordinal).ToList();
        return found is [var project] && File.Exists(Path.Combine(worktree, project)) ? project : new Refusal("build.no-project",
            found.Count > 1 ? "The repository holds several projects: " + string.Join(", ", found) + "." : "The repository holds no project at " + (named ?? "any path") + ".",
            "Name the .sqlproj to build with --project, by its path from the repository's root.");
    }

    public static Result<Dacpac> Build(string project, string toolFolder, string outputRoot) => Build(project, toolFolder, outputRoot, Doctor.Run);

    /// <summary>A project as a ref holds it: its path from the repository's root, found in the ref's worktree, built under outputRoot/&lt;the commit&gt;/.</summary>
    public static Result<Dacpac> Build(Git.Worktree at, string project, string toolFolder, string outputRoot) => Path.IsPathRooted(project)
        ? new Refusal("build.no-project", project + " is not a path from the repository's root, where a ref's project is found.", "Name the .sqlproj by its path from the repository's root.")
        : Build(Path.Combine(at.Path, project), toolFolder, outputRoot, Doctor.Run, at.Commit);

    public static Result<Dacpac> Build(string project, string toolFolder, string outputRoot, Doctor.Command probe) => Build(project, toolFolder, outputRoot, probe, null);

    /// <summary>
    /// Builds a classic .sqlproj as §1 fact 1 does, with the SDK the probe lists: dotnet build against the tool folder's targets
    /// and reference stub, telemetry off, its output and intermediate files under outputRoot/&lt;the inputs' fingerprint&gt;/, or
    /// under outputRoot/&lt;commit&gt;/ for a ref's worktree, so nothing is written beside the project and two refs never share a
    /// folder. A missing SDK band or tool folder is refused before anything builds.
    /// </summary>
    private static Result<Dacpac> Build(string project, string toolFolder, string outputRoot, Doctor.Command probe, string? commit)
    {
        var (file, tool) = (Path.GetFullPath(project), Path.TrimEndingDirectorySeparator(Path.GetFullPath(toolFolder)));
        var directory = Path.GetDirectoryName(file)!;
        return (File.Exists(file), Doctor.Sdk(directory, probe), Doctor.Tool(tool)) switch
        {
            (false, _, _) => new Refusal("build.no-project", "No project at " + file + ".", "Name the .sqlproj to build, by its path from the working directory."),
            (_, { Remedy: { } install } sdk, _) => new Refusal(
                "sdk.missing", "dotnet build loads DacFx's net10.0 build task, and this machine has " + sdk.Found + ".", install + "; then estate doctor"),
            (_, _, { Remedy: { } publish } found) => new Refusal("tool.missing", tool + " is " + found.Found + ".", publish + "; then estate doctor"),
            _ => Run(file, tool, Path.GetFullPath(outputRoot), commit),
        };
    }

    private static Result<Dacpac> Run(string project, string tool, string outputRoot, string? commit)
    {
        var directory = Path.GetDirectoryName(project)!;
        var inputs = Inputs(directory, tool);

        // outputRoot/<the ref's commit>/, or for a plain path <the inputs' fingerprint, its first 16 digits so MSBuild's paths stay short on Windows>/.
        var output = Path.Combine(outputRoot, commit ?? inputs.ToString()[..16]) + "/";
        var (exit, log) = Dotnet(directory,
        [
            "build", project, "-c", "Release", "--no-restore", "-nologo", "-tl:off", "-v:m", "-nodeReuse:false",
            "-p:DacFxTelemetryEnabled=false", "-p:NetCoreBuild=true", "-p:NETCoreTargetsPath=" + tool, "-p:SQLDBExtensionsRefPath=" + tool,
            "-p:TargetFrameworkRootPath=" + Path.Combine(tool, "refasm"),
            "-p:OutputPath=" + output, "-p:BaseIntermediateOutputPath=" + output + "obj/", "-p:IntermediateOutputPath=" + output + "obj/",
        ]);
        var errors = log.Split('\n').Select(line => Error.Match(line.TrimEnd('\r'))).Where(m => m.Success).Select(m => Located(m, directory)).Distinct().ToList();
        var dacpac = Wrote.Matches(log).Select(m => m.Groups["path"].Value).LastOrDefault();
        return exit == 0 && errors.Count == 0 && dacpac is not null && File.Exists(dacpac)
            ? new Dacpac(dacpac, inputs)
            : new Refusal(
                "build.failed",
                "dotnet build of " + Path.GetFileName(project) + " failed:\n" + string.Join('\n', errors.Count > 0 ? errors : log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(20)),
                "Fix each error at the file and line it names, then build again.");
    }

    /// <summary>An error as MSBuild writes it, with a file under the project named from the project's folder and the project's bracket dropped.</summary>
    private static string Located(Match error, string directory)
    {
        var origin = error.Groups["origin"].Value;
        var file = Path.IsPathFullyQualified(origin) && !Path.GetRelativePath(directory, origin).StartsWith("..", StringComparison.Ordinal)
            ? Path.GetRelativePath(directory, origin).Replace('\\', '/')
            : origin;
        return file + error.Groups["position"].Value + ": error " + error.Groups["code"].Value + ": " + error.Groups["text"].Value;
    }

    /// <summary>
    /// The fingerprint of what a build reads: each file under the project's folder by its relative path and its bytes, except
    /// under bin/, obj/ and hidden folders such as .git/ and .estate/; then the engine's files, so a new engine never reuses
    /// an old engine's folder.
    /// </summary>
    private static Fingerprint Inputs(string directory, string tool) => Fingerprint.Of(string.Join('\n',
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').SkipLast(1).Any(folder => folder is "bin" or "obj" || folder.StartsWith('.')))
            .Order(StringComparer.Ordinal)
            .Select(file => file + " " + Fingerprint.Of(File.ReadAllBytes(Path.Combine(directory, file))))
            .Concat(Engine.Where(file => File.Exists(Path.Combine(tool, file))).Select(file => "tool/" + file + " " + Fingerprint.Of(File.ReadAllBytes(Path.Combine(tool, file)))))));

    /// <summary>dotnet with the arguments, from the directory, telemetry off: its exit code, and its output and errors together.</summary>
    private static (int Exit, string Log) Dotnet(string directory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("dotnet", arguments) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DACFX_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        try
        {
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output + errors.Result);
        }
        catch (Win32Exception e)
        {
            return (-1, "dotnet did not start: " + e.Message);
        }
    }

    /// <summary>A package's model, its deploy scripts and its refactorlog; a file DacFx cannot read as a package is refused.</summary>
    public static Result<Package> Load(string dacpac)
    {
        try
        {
            using var package = DacPackage.Load(dacpac, DacSchemaModelStorageType.Memory, FileAccess.Read);
            using var zip = ZipFile.OpenRead(dacpac);
            using var log = zip.GetEntry("refactor.xml")?.Open();
            using var xml = zip.GetEntry("model.xml")?.Open() ?? throw new InvalidDataException("The package holds no model.xml.");
            var (pre, post, refactors) = (Text(package.PreDeploymentScript), Text(package.PostDeploymentScript), log is null ? [] : Entries(log));
            return new Package(TSqlModel.LoadFromDacpac(dacpac, new ModelLoadOptions(DacSchemaModelStorageType.Memory, loadAsScriptBackedModel: false)), pre, post, refactors, Serialized(xml));
        }
        catch (Exception e) when (e is DacServicesException or DacModelException or IOException or InvalidDataException or XmlException or UnauthorizedAccessException)
        {
            return new Refusal("package.unreadable", dacpac + " is not a package DacFx reads: " + e.Message, "Name a .dacpac a build wrote, or build its project again.");
        }
    }

    /// <summary>A .refactorlog file's entries, read as Load reads the copy a build puts in the package.</summary>
    public static Result<IReadOnlyList<RefactorEntry>> RefactorLog(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Result.Ok<IReadOnlyList<RefactorEntry>>(Entries(file));
        }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException)
        {
            return new Refusal("refactorlog.unreadable", path + " is not a refactorlog SSDT reads: " + e.Message, "Restore the file from git, then repeat the rename in Visual Studio so SSDT writes its entry.");
        }
    }

    /// <summary>
    /// The operations of a refactorlog, in file order. A build's copy leaves the root element outside the namespace its
    /// operations carry, so the operations are found by name wherever they sit; one with no key, name or element is refused.
    /// </summary>
    private static List<RefactorEntry> Entries(Stream log)
    {
        using var reader = XmlReader.Create(log, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader).Descendants(Dac + "Operation").Select(operation =>
        {
            string? Property(string name) => (string?)operation.Elements(Dac + "Property").FirstOrDefault(p => (string?)p.Attribute("Name") == name)?.Attribute("Value");
            string Required(string? value, string what) => value ?? throw new XmlException("A refactorlog operation has no " + what + ".");
            return new RefactorEntry(
                Required((string?)operation.Attribute("Key"), "Key"), Required((string?)operation.Attribute("Name"), "Name"), (string?)operation.Attribute("ChangeDateTime"),
                Required(Property("ElementName"), "ElementName"), Property("ElementType"), Property("ParentElementName"), Property("ParentElementType"), Property("NewName"), Property("NewSchema"));
        }).ToList();
    }

    private static string? Text(Stream? script)
    {
        using var reader = script is null ? null : new StreamReader(script);
        return reader?.ReadToEnd();
    }

    /// <summary>Each name model.xml gives exactly one element, with that element's serialized type; streamed, as model.xml grows with the estate.</summary>
    private static Dictionary<string, string> Serialized(Stream model)
    {
        var elements = new List<(string? Name, string? Type)>();
        using var reader = XmlReader.Create(model, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        while (reader.ReadToFollowing("Element", Dac.NamespaceName))
        {
            elements.Add((reader.GetAttribute("Name"), reader.GetAttribute("Type")));
        }

        return elements.Where(e => e.Name is not null).GroupBy(e => e.Name!, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Type ?? "", StringComparer.Ordinal);
    }

    /// <summary>
    /// The properties the walk leaves out, each holding a password or a secret: SQL Server never returns one, so DacFx makes up a new
    /// value for a login's password on each database read, and a package carries whatever its script wrote. Besides the passwords and
    /// credential secrets: a symmetric key's KEY_SOURCE and IDENTITY_VALUE, from which SQL Server derives the key; a linked server's
    /// provider string (sp_addlinkedserver's @provstr) and an external data source's CONNECTION_OPTIONS, each a connection string
    /// whose documented form carries PWD=, left out whole, so an edit to either is not seen. DacFx 170.5.96's metadata marks none of
    /// them as secret, so the list is kept here; Io.Tests' WalkTests plants each, and lists every other string-typed property DacFx declares
    /// with the reason it is not a secret. DacFx fills these static fields when its model schema initializes, which the first
    /// TSqlModel a process makes does, so the list is made on first use, after one.
    /// </summary>
    public static IReadOnlySet<ModelPropertyClass> Secrets => Secret.Value;

    private static readonly Lazy<HashSet<ModelPropertyClass>> Secret = new(() =>
    {
        new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions()).Dispose();
        return
        [
            Login.Password, User.Password, ApplicationRole.Password, MasterKey.Password, AsymmetricKey.Password, SymmetricKeyPassword.Password,
            Certificate.EncryptionPassword, Certificate.PrivateKeyDecryptionPassword, Certificate.PrivateKeyEncryptionPassword,
            Credential.Secret, DatabaseCredential.Secret, LinkedServerLogin.LinkedServerPassword, SignatureEncryptionMechanism.Password,
            SymmetricKey.KeySource, SymmetricKey.IdentityValue, LinkedServer.ProviderString, ExternalDataSource.ConnectionOptions,
        ];
    });

    /// <summary>A package read whole (§2.1 rule 1): its elements, and the renames its refactorlog records, as Change.Between takes them.</summary>
    public sealed record Read(Seq<Element> Elements, Seq<Rename> Renames);

    /// <summary>
    /// The model walked, one element for each deploy script and each refactorlog entry, and the entries' renames. An entry's type,
    /// written as model.xml writes it (SqlSimpleColumn), is the walk's (Column) through a named object model.xml names once, matched
    /// by its own name and never by a key an unnamed object shares; a type the package no longer holds keys nothing (a drop and an add).
    /// </summary>
    public static Result<Read> Walk(Package package) => Walked(package.Model).Bind(model =>
    {
        var types = model.Where(w => w.Name is { } name && package.Serialized.ContainsKey(name)).GroupBy(w => package.Serialized[w.Name!], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Element.Key.Type, StringComparer.Ordinal);
        string TypeOf(string? serialized) => serialized is not null && types.TryGetValue(serialized, out var type) ? type : serialized ?? "";
        var scripts = new[] { package.PreDeploy is { } pre ? Element.PreDeploy(Lf(pre)) : null, package.PostDeploy is { } post ? Element.PostDeploy(Lf(post)) : null }.OfType<Element>();
        return All(package.Refactors.Select(Entry)).Bind(entries =>
            All(package.Refactors.Where(r => r.NewName is not null || r.NewSchema is not null).Select(r => Renaming(r, TypeOf)))
                .Map(renames => new Read(Seq.Of(model.Select(w => w.Element).Concat(scripts).Concat(entries)), Seq.Of(renames))));
    });

    /// <summary>
    /// A model read whole, no code per type (§1 fact 6): each user-defined top-level object but the two grants to public SQL Server
    /// makes in every new database (<see cref="Default"/>) and, depth first, what its composing relationships reach, each object
    /// once, with every property its type declares but a password or a secret (<see cref="Secrets"/>), a module's Definition as
    /// written too, and every relationship's targets in DacFx's order; a target's own property (an index column's Ascending) is
    /// Relationship[position].Property. A key is the name while it has one or two parts and nothing
    /// composes the object; else the parent's key (the composer, or the hierarchical parent: an index's table, a grant's securable)
    /// and the name parts the parent's name does not hold. An unnamed default, check, unique or foreign key constraint on exactly one
    /// column is keyed under that column by the relationship that names it (TargetColumn, ExpressionDependencies, Columns), so it
    /// moves with the column's rename, and a column added beside it, or a table whose columns a database holds in another order
    /// (a publish under IgnoreColumnOrder appends a column the project inserts), leaves its key as it was. Any other unnamed object
    /// (a primary key, a constraint on several columns) is keyed by the relationship to its table. Several unnamed objects of one
    /// type under one parent are numbered from 1 in the order of the names they reference, then of their own values, never by a
    /// generated name or DacFx's order; a package and the database it was published to hold the same names, so they number alike,
    /// and a rename of a column that a constraint on several columns references can renumber that constraint and its siblings. SQL
    /// Server normalizes a check's text, so two checks on one column may number apart in a package and its database. Two objects
    /// keyed alike are refused; an unresolved reference is keyed as the type Unresolved. Reads are compared only between like sources and, for databases, like identities:
    /// SQL Server shows a server-scoped login only to a reader with permission on it (sysadmin, VIEW ANY DEFINITION, or its own), and
    /// a db_datareader login holding VIEW DEFINITION read Query Store's database options differently from sa when measured on 2026-09-24.
    /// </summary>
    public static Result<Seq<Element>> Walk(TSqlModel model) => Walked(model).Map(walked => Seq.Of(walked.Select(w => w.Element)));

    /// <summary>
    /// A grant SQL Server makes in every new database, copying it from model: VIEW ANY COLUMN ENCRYPTION KEY DEFINITION and VIEW ANY
    /// COLUMN MASTER KEY DEFINITION to public, which Always Encrypted's client drivers read. A database holds both whatever its project
    /// says, and a project imported from a database may hold them too, so the walk leaves both out of every read; a REVOKE of either
    /// is therefore not seen.
    /// </summary>
    private static bool Default(TSqlObject o) => o.ObjectType == Permission.TypeClass
        && o.GetProperty<PermissionAction>(Permission.PermissionAction) == PermissionAction.Grant && !o.GetProperty<bool>(Permission.WithGrantOption)
        && o.GetProperty<PermissionType>(Permission.PermissionType) is PermissionType.ViewAnyColumnEncryptionKeyDefinition or PermissionType.ViewAnyColumnMasterKeyDefinition
        && o.GetReferenced(Permission.Grantee, DacQueryScopes.All).ToArray() is [var grantee] && grantee.Name.Parts is [var role] && string.Equals(role, "public", StringComparison.OrdinalIgnoreCase);

    /// <summary>The walk, each element with its object's name as model.xml writes it ([dbo].[Customer].[Email]), null for an unnamed object.</summary>
    private static Result<List<(Element Element, string? Name)>> Walked(TSqlModel model)
    {
        var composers = new Dictionary<TSqlObject, (TSqlObject Parent, string Relationship)>();
        var walked = new HashSet<TSqlObject>();
        void Descend(TSqlObject o)
        {
            var composed = o.ObjectType.Relationships.Where(r => r.Type == RelationshipType.Composing).SelectMany(r => o.GetReferenced(r, DacQueryScopes.All).Select(c => (r, c)));
            foreach (var (r, child) in walked.Add(o) ? composed : [])
            {
                composers[child] = (o, r.Name);
                Descend(child);
            }
        }

        model.GetObjects(DacQueryScopes.UserDefined).Where(o => !Default(o)).ToList().ForEach(Descend);

        // The relationship through which each type of unnamed constraint that can sit on one column names its columns.
        var on = new Dictionary<ModelTypeClass, ModelRelationshipClass>
        {
            [DefaultConstraint.TypeClass] = DefaultConstraint.TargetColumn, [CheckConstraint.TypeClass] = CheckConstraint.ExpressionDependencies,
            [UniqueConstraint.TypeClass] = UniqueConstraint.Columns, [ForeignKeyConstraint.TypeClass] = ForeignKeyConstraint.Columns,
        };
        (TSqlObject? Parent, string Relationship) Anchor(TSqlObject o) => composers.TryGetValue(o, out var composer) ? composer
            : !o.Name.HasName && on.TryGetValue(o.ObjectType, out var through)
                && o.GetReferenced(through, DacQueryScopes.All).Where(c => c.ObjectType == Column.TypeClass).Distinct().ToArray() is [var column]
                ? (column, through.Name)
            : o.GetParent(DacQueryScopes.All) is not { } parent ? (null, o.ObjectType.Name)
            : (parent, o.ObjectType.Relationships.FirstOrDefault(r => r.Type == RelationshipType.Hierarchical && o.GetReferenced(r, DacQueryScopes.All).Contains(parent))?.Name ?? o.ObjectType.Name);

        string References(TSqlObject o) => string.Join('\n', o.ObjectType.Relationships.Where(r => r.Type != RelationshipType.Composing)
            .SelectMany(r => o.GetReferencedRelationshipInstances(r, DacExternalQueryScopes.All).Select(i => r.Name + " " + i.ObjectName)));
        var secrets = Secrets;
        IEnumerable<ModelPropertyClass> Kept(IEnumerable<ModelPropertyClass> declared) => declared.Where(p => !secrets.Contains(p));
        string Values(TSqlObject o) => string.Join('\n', Kept(o.ObjectType.Properties).Select(p => p.Name + " " + ValueOf(() => o.GetProperty(p), p.DataType)));
        var unnamed = walked.Where(o => !o.Name.HasName).GroupBy(o => (Anchor: Anchor(o), Type: o.ObjectType.Name))
            .SelectMany(g => g.OrderBy(References, StringComparer.Ordinal).ThenBy(Values, StringComparer.Ordinal)
                .Select((o, i) => (Object: o, Name: g.Count() == 1 ? g.Key.Anchor.Relationship : string.Create(CultureInfo.InvariantCulture, $"{g.Key.Anchor.Relationship} {i + 1}"))))
            .ToDictionary(u => u.Object, u => u.Name);

        var keys = new Dictionary<TSqlObject, Result<ElementKey>>();
        Result<ElementKey> Key(TSqlObject o) => keys.TryGetValue(o, out var key) ? key : keys[o] = KeyOf(o);
        Result<ElementKey> KeyOf(TSqlObject o)
        {
            string[] own = o.Name.HasName ? [.. o.Name.Parts] : [unnamed.GetValueOrDefault(o) ?? Anchor(o).Relationship];
            var parent = o.Name.HasName && own.Length <= 2 && !composers.ContainsKey(o) ? null : Anchor(o).Parent;
            return parent is null ? Keyed(o.ObjectType.Name, own, null) : Key(parent).Bind(home => Keyed(o.ObjectType.Name, Beneath(own, [.. parent.Name.Parts]), home));
        }

        Result<Element> Read(TSqlObject o)
        {
            var relationships = o.ObjectType.Relationships.Select(r => (Class: r, Instances: o.GetReferencedRelationshipInstances(r, DacExternalQueryScopes.All).ToArray())).ToArray();
            var properties = Kept(o.ObjectType.Properties).Select(p => (p.Name, Value: ValueOf(() => o.GetProperty(p), p.DataType)))
                .Append((Name: "Definition", Value: Module(o.ObjectType) ? ValueOf(() => o.TryGetScript(out var script) ? script : null, typeof(string)) : null))
                .Concat(relationships.SelectMany(r => r.Instances.SelectMany((i, n) => Kept(r.Class.Properties).Select(p =>
                    (Name: string.Create(CultureInfo.InvariantCulture, $"{r.Class.Name}[{n}].{p.Name}"), Value: ValueOf(() => i.GetProperty(p), p.DataType))))))
                .Where(p => p.Value is not null).Select(p => new Element.Property(p.Name, p.Value!));
            var targets = relationships.Select(r => All(r.Instances.Select(i => i.Object is { } target ? Key(target) : Keyed("Unresolved", [.. i.ObjectName.ExternalParts ?? [], .. i.ObjectName.Parts], null)))
                .Map(to => Element.Relationship.Of(r.Class.Name, to)));
            return Key(o).Bind(key => All(targets).Bind(rs => Element.Of(key, properties, rs)));
        }

        return All(walked.Select(o => Read(o).Map(e => (Element: e, Name: o.Name.HasName ? Keyed(o.ObjectType.Name, [.. o.Name.Parts], null).Match<string?>(k => k.Path, _ => null) : null))))
            .Bind(read => read.GroupBy(w => w.Element.Key).FirstOrDefault(g => g.Count() > 1) is not { } alike ? Result.Ok(read) : new Refusal("walk.duplicate-key",
                $"{alike.Count()} {alike.Key.Type} objects of the model are keyed alike, as {alike.Key}: {string.Join(", ", alike.Select(w => w.Name ?? "unnamed"))}.",
                "Report the model's source with this refusal: a key names one object, so the walk keys this type ambiguously, a defect in io/Ssdt.Walk."));
    }

    /// <summary>
    /// An object's property as the kernel's closed Value, or null where DacFx cannot read it, and the walk skips it. An enumeration
    /// reaches an untyped read as its integer, so the declared type names its member; any other type (a double, as a spatial
    /// index's bounds) is its invariant string, so no value is dropped for its type. Text has CRLF and a lone CR made LF.
    /// </summary>
    public static Value? ValueOf(TSqlObject o, ModelPropertyClass property) => ValueOf(() => o.GetProperty(property), property.DataType);

    private static Value? ValueOf(Func<object?> read, Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        try
        {
            var value = read();
            return value switch
            {
                null => new Value.Null(),
                bool b => new Value.Boolean(b),
                string s => new Value.Text(Lf(s)),
                Enum or sbyte or byte or short or ushort or int or uint or long when type.IsEnum => new Value.Enumeration(type.Name, Enum.Format(type, Enum.ToObject(type, value), "G")),
                sbyte or byte or short or ushort or int or uint or long => new Value.Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                IFormattable f => new Value.Text(f.ToString(null, CultureInfo.InvariantCulture)),
                _ => new Value.Text(Lf(value.ToString() ?? "")),
            };
        }
        catch (DacModelException)
        {
            return null;
        }
    }

    /// <summary>A module, whose body DacFx reads for BodyDependencies and holds in no property of its script type: a procedure, a function, a trigger (a view's is SelectStatement).</summary>
    private static bool Module(ModelTypeClass type) => type.Relationships.Any(r => r.Name == "BodyDependencies") && type.Properties.All(p => p.DataType.Name != "SqlScriptProperty");

    /// <summary>A refactorlog entry as an element: its key, and as text each attribute and property the file gives it.</summary>
    private static Result<Element> Entry(RefactorEntry r) => Element.RefactorLogEntry(r.Key, new (string Name, string? Value)[] {
        ("Operation", r.Operation), ("ChangeDateTime", r.ChangeDateTime), ("ElementName", r.ElementName), ("ElementType", r.ElementType),
        ("ParentElementName", r.ParentName), ("ParentElementType", r.ParentType), ("NewName", r.NewName), ("NewSchema", r.NewSchema) }
        .Where(p => p.Value is not null).Select(p => new Element.Property(p.Name, new Value.Text(p.Value!))));

    /// <summary>An entry's rename: its element's key (past two parts, under its parent's, as the walk keys it) to the key its NewName or NewSchema gives; ScriptDom reads the names.</summary>
    private static Result<Rename> Renaming(RefactorEntry r, Func<string?, string> typeOf) =>
        Parts(r.ElementName).Bind(parts => parts.Length > 2 && r.ParentName is { } parent
                ? Parts(parent).Bind(home => Keyed(typeOf(r.ParentType), home, null).Bind(key => Keyed(typeOf(r.ElementType), Beneath(parts, home), key)))
                : Keyed(typeOf(r.ElementType), parts, null))
            .Bind(before => r.NewName is { } name
                ? Parts(name).Bind(n => Rename.Of(before, n[^1]))
                : Parts(r.NewSchema!).Bind(s => Name.Of(s[^1], before.Name.Base)).Bind(n => ElementKey.Of(before.Type, n)).Map(after => new Rename(before, after)));

    private static Result<string[]> Parts(string name) =>
        new TSql160Parser(initialQuotedIdentifiers: true).ParseSchemaObjectName(new StringReader(name), out _) is { } parsed
            ? parsed.Identifiers.Select(i => i.Value).ToArray()
            : new Refusal("refactorlog.name", "The refactorlog names " + name + ", which is not a name of one to four parts.", "Restore the refactorlog from git, then repeat the rename in Visual Studio so SSDT writes its entry.");

    /// <summary>A key from name parts: under home, each part a level down; with no home, the first one or two parts at the top and each further part a level down.</summary>
    private static Result<ElementKey> Keyed(string type, string[] parts, ElementKey? home) => parts.Skip(home is null ? 2 : 0).Aggregate(
        home is not null ? Result.Ok(home) : (parts.Length > 1 ? Name.Of(parts[0], parts[1]) : Name.Of(parts.FirstOrDefault() ?? "")).Bind(name => ElementKey.Of(type, name)),
        (key, part) => key.Bind(parent => Name.Of(part).Bind(name => ElementKey.Of(parent, type, name))));

    /// <summary>A name's parts less the run of its parent's name parts (ignoring case) where that run leaves one or more; else all of them.</summary>
    private static string[] Beneath(string[] own, string[] home) => Enumerable.Range(0, Math.Max(0, own.Length - home.Length + 1))
        .Where(i => own.Skip(i).Take(home.Length).SequenceEqual(home, StringComparer.OrdinalIgnoreCase)).Select(i => (string[])[.. own[..i], .. own[(i + home.Length)..]]).FirstOrDefault(rest => rest.Length > 0) ?? own;

    /// <summary>Every value, in order, or the first refusal.</summary>
    private static Result<List<T>> All<T>(IEnumerable<Result<T>> results) =>
        results.Aggregate(Result.Ok(new List<T>()), (all, next) => all.Bind(list => next.Map(value => { list.Add(value); return list; })));

    /// <summary>XML's end-of-line rule (CRLF and a lone CR to LF), which DacFx's own model values arrive under, so a build on Windows reads as one on Linux.</summary>
    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
