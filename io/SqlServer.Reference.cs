using System;
using System.IO;
using System.Linq;
using System.Threading;
using DbChange.Kernel;
using Microsoft.Data.SqlClient;

namespace DbChange.Io;

public static partial class SqlServer
{
    /// <summary>
    /// A reference's connection (§4 row 14, VALUES.md X1): env:NAME's variable or file:path's text, a relative path read from the
    /// repository root, parsed by SqlClient's own grammar (io/ConnectionString.cs); the caller's integrated identity when it names no
    /// other; dbchange as the application unless it names one. An error names the reference and quotes nothing it read.
    /// </summary>
    internal static Result<string> Connect(string subject, SecretReference reference, string repositoryRoot) =>
        Read(subject, reference, repositoryRoot).Bind(read => read is not { } text
        ? new Error("connection.unresolved", subject + " resolves to nothing here.",
            "Set the variable, or write the file outside git, that " + reference + " names.")
        : Parsed(subject, reference, text).Bind(connection => connection.InitialCatalog.Length == 0
            ? new Error("connection.malformed", subject + " names no database; every read reads the database the connection names.",
                "Give the connection string an Initial Catalog, in the place " + reference + " names.")
            : Result.Ok(ConnectionString.WithDefaults(connection))));

    /// <summary>
    /// An environment's server as R15 reads it, a database named or not: null when its reference resolves to nothing here; an error when
    /// SqlClient reads nothing from it.
    /// </summary>
    internal static Result<ServerName?> DataSource(NamedEnvironment environment, string repositoryRoot) =>
        Read(EnvironmentDatabase.Subject(environment), environment.Connection, repositoryRoot).Bind(read => read is not { } text
        ? Result.Ok<ServerName?>(null)
        : Parsed(EnvironmentDatabase.Subject(environment), environment.Connection, text)
            .Map(connection => (ServerName?)ConnectionString.ServerOf(connection)));

    /// <summary>A reference's text as SqlClient's own grammar reads it; the error names the reference and quotes nothing it read.</summary>
    private static Result<SqlConnectionStringBuilder> Parsed(string subject, SecretReference reference, string text) =>
        ConnectionString.Parse(subject, text, "Correct the connection string in the place " + reference + " names.");

    /// <summary>
    /// What a reference names: the variable's value, or the file's text trimmed; null when the variable is unset or empty, when the
    /// file system reports that no file or folder is at the path, that a folder is, or that no file can have the path's name
    /// (<see cref="Absent"/>), or when the file holds only white space. A file,
    /// a relative path read from the repository root, is read only when git keeps it out of every commit, ignored or in no repository
    /// while the repository root is in one, and, where files carry a Unix mode, when its owner alone can read it; an error leads with
    /// <paramref name="subject"/>. git is asked about the file by the name its folder lists (<see cref="Listed"/>), and that name is the
    /// one read. Whatever the file system withholds is an error, never null, since the host of a file that may be there is unknown
    /// here and R15 must not leave the environment uncompared as it does one that resolves to nothing: a path whose attributes this
    /// identity cannot read, where File.Exists answers false as it does where no file is, is reference.inaccessible; a file whose
    /// folder it cannot list is reference.unlistable; and one it cannot read is reference.unreadable. The check covers the path the
    /// reference names, since git tracks paths: a hard link to a committed file, or a plain copy of one, under a folder .gitignore
    /// lists such as .dbchange/ is read, though the commit holds what it holds. The attempt, a refused one too, is recorded in the run's
    /// Reads.
    /// </summary>
    internal static Result<string?> Read(string subject, SecretReference reference, string repositoryRoot)
    {
        Reads.Record();
        return reference.Match(
            variable => Result.Ok(System.Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : null),
            file => System.IO.Path.Combine(repositoryRoot, file) is var path && File.Exists(path)
                ? Opened(() => Listed(subject, path), () => Unlistable(subject))
                    .Bind(listed => Opened(() => Kept(subject, repositoryRoot, listed)
                        .Map(kept => File.ReadAllText(kept).Trim() is { Length: > 0 } text ? text : null), () => Unreadable(subject)))
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
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException
            || (e is IOException && e.HResult == InvalidName))
        {
            return Result.Ok<string?>(null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("reference.inaccessible",
                subject + " names a path whose attributes this identity cannot read, or that it cannot reach,"
                + " so whether a file is there, and what it holds, is unknown here; it is not read.",
                "Grant this identity the right to list the file's folder and read the file's attributes (on Linux and macOS, search "
                    + "permission on every folder of the path), or move the file under a folder it can list, such as .dbchange/.");
        }
    }

    /// <summary>
    /// The HResult of the IOException .NET throws for Windows' ERROR_INVALID_NAME (123), a name no file on Windows can have.
    /// </summary>
    private const int InvalidName = unchecked((int)0x8007007B);

    /// <summary>
    /// A step on a file that exists; the error given when the file system refuses the step (IOException, UnauthorizedAccessException).
    /// </summary>
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

    private static Error Unlistable(string subject) =>
        new Error("reference.unlistable", subject + " is a file whose folder this identity cannot list,"
        + " so git cannot be asked about the file by the name the folder lists, and the file is not read.",
        "Grant this identity the right to list the file's folder, or move the file under a folder it can list, such as .dbchange/.");

    private static Error Unreadable(string subject) =>
        new Error("reference.unreadable", subject + " is a file this identity cannot open for reading,"
        + " for want of the right to read it or while another program holds it open, so what it holds is unknown here.",
        "Grant this identity the right to read the file, and close any program that holds it open.");

    /// <summary>
    /// The refusal of a file a reference names whose Unix mode lets its group or other users read it, or null: Read asks it of the
    /// file's mode on Linux and macOS, and Windows keeps no such mode.
    /// </summary>
    public static Error? ReadableByOthers(string subject, UnixFileMode mode) =>
        (mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0 ? null
        : new Error("reference.readable-by-others", subject + " is a file its group or other users can read (mode "
            + Convert.ToString((int)mode & 0b111_111_111, 8).PadLeft(4, '0')
            + "); a file holding a connection string is read by its owner alone.",
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

    /// <summary>
    /// The entry of the path's folder that the path names: the one of its exact name, else, where the file system opens a name whatever its
    /// case, the one of its name in another case.
    /// </summary>
    private static FileInfo? Entry(string path)
    {
        var named = new FileInfo(System.IO.Path.GetFullPath(path));
        var entries = named.Directory is { Exists: true } folder ? folder.GetFiles() : [];
        return entries.FirstOrDefault(e => e.Name == named.Name)
            ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? entries.FirstOrDefault(e => string.Equals(e.Name, named.Name, StringComparison.OrdinalIgnoreCase)) : null);
    }

    private static Error Unlisted(string subject) =>
        new Error("reference.unlisted", subject + " opens a file by a name its folder does not list, such as name::$DATA, a data stream;"
        + " git matches .gitignore and its index against the name the folder lists, "
        + "so it cannot say whether a commit holds the file, and the file is not read.",
        "Write the path as dir or ls lists the file, in dbchange/environments.json.");

    /// <summary>
    /// The path of a file a file: reference names, when git keeps it out of every commit and no other user can read it; else the
    /// error, the file unread. git.failed and git.missing lead with <paramref name="subject"/>, then quote io/Git's own message.
    /// </summary>
    private static Result<string> Kept(string subject, string repositoryRoot, string path) =>
        Git.HoldingOf(repositoryRoot, path).Match<Result<string>>(holding => holding switch
    {
        Git.Holding.Tracked => new Error("reference.tracked",
            subject + " is a file git tracks, so every clone of the repository holds what it holds; "
                + "a file: reference names a file git keeps out of every commit.",
            "Run git rm --cached on the file, list it in .gitignore, and change the password it held, since the history keeps the commit."),
        Git.Holding.NotIgnored => new Error("reference.not-ignored",
            subject + " is a file git does not ignore, so the next git add commits it; "
                + "a file: reference names a file git keeps out of every commit.",
            "List the file in .gitignore, or move it under a folder .gitignore lists, such as .dbchange/."),
        Git.Holding.RootInNoRepository =>
            new Error("reference.no-repository", subject + " names a file, and the repository root " + repositoryRoot
            + " is in no git repository, so git cannot say whether a clone would commit the file; it is not read.",
            "Change to a clone of the SSDT repository, or give the reference as env:NAME."),
        Git.Holding.Ignored => OwnerOnly(subject, path),
        Git.Holding.InNoRepository => OwnerOnly(subject, path),
        _ => throw new System.Diagnostics.UnreachableException(),
    }, error => new Error(error.Code, subject + " cannot be checked against git: " + error.Message, error.Remedy));

    /// <summary>
    /// The path of a file git keeps out of every commit, when no other user can read it: on Linux and macOS by its mode; Windows keeps no
    /// such mode.
    /// </summary>
    private static Result<string> OwnerOnly(string subject, string path) =>
        !OperatingSystem.IsWindows() && ReadableByOthers(subject, File.GetUnixFileMode(path)) is { } readable ? readable : path;

    /// <summary>
    /// Whether this run has read a connection or other reference of a named environment (VALUES.md X2), whose text an exception's message
    /// can then quote. SqlServer.Read records each read, and every one goes through it: EnvironmentDatabase.Of, which SqlServer.Resolve
    /// calls for env:; R15's read of each environment's connection in io/LocalServer, which copy: and a new copy run; and a named
    /// environment's SQLCMD values. cli/Program.cs begins a run around each command and withholds an unexpected exception's message when
    /// the run holds a read. The record reaches the threads the run's work starts (it is an AsyncLocal); a read outside any run is recorded
    /// nowhere, no catch reading it.
    /// </summary>
    public static class Reads
    {
        private static readonly AsyncLocal<Run?> Current = new();

        /// <summary>
        /// A run begun on this thread, the current one until it is disposed, when the run it began inside is current again.
        /// </summary>
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
}
