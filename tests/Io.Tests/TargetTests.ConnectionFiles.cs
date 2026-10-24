using System;
using System.IO;
using System.Linq;
using DbChange.Budgets.Tests;
using DbChange.Budgets.Tests.Register;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>A connection file a reference names: one whose name Windows forbids resolves to nothing; one git tracks or does not ignore, one
/// git cannot check, one this identity cannot read and one its group can read are refused.</summary>
public sealed partial class TargetTests
{
    /// <summary>
    /// Windows forbids ? * &lt; &gt; and | in a file name, so no file is at a path whose name holds one; File.Exists answers false and
    /// File.GetAttributes throws an IOException for ERROR_INVALID_NAME. On Linux and macOS the name is legal and no file is there.
    /// On every operating system the reference resolves to nothing: Resolve fails with connection.unresolved, and
    /// LocalServer.Unnamed leaves env:dev uncompared and returns the server, as it does for any path with no file.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "O2")]
    [InlineData("dev?.connection")]
    [InlineData("dev*.connection")]
    [InlineData("dev<.connection")]
    [InlineData("dev>.connection")]
    [InlineData("dev|.connection")]
    public void A_connection_file_whose_name_Windows_forbids_resolves_to_nothing_on_every_operating_system(string name)
    {
        var root = RepositoryAt(EnvironmentsJson.Dev("file:" + scratch.Path.Replace('\\', '/') + "/" + name));

        Failed(SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), root), "connection.unresolved");
        Assert.Equal(Server("localhost,11433"),
            Value(LocalServer.Unnamed(EnvironmentsAt(root), root, Server("localhost,11433"), Resolver)));
    }

    /// <summary>
    /// kernel/Environments.cs documents a file: reference as naming a file outside git, and dbchange's own files, those of its principals
    /// among them, sit under .dbchange/, which .gitignore lists. A connection file git tracks, or one git does not ignore, which the next
    /// git add would commit, is refused by the environment and the reference, before the file is read, and quotes nothing it holds; one git
    /// ignores, or one in no git repository while the repository root is in one, resolves.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "X1")]
    [InlineData("dbchange/dev.connection", "committed", "reference.tracked")]
    [InlineData("dbchange/dev.connection", "written", "reference.not-ignored")]
    [InlineData(".dbchange/dev.connection", "written", null)]
    [InlineData("outside every repository", "written", null)]
    public void A_connection_file_git_tracks_or_does_not_ignore_is_refused_and_one_git_ignores_resolves(string file, string how,
        string? code)
    {
        using var repository = new Scratch();
        var connection = "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted;
        var reference = file == "outside every repository" ? Written("dev.connection", connection) : file;
        repository.Commit("the estate", (".gitignore", ".dbchange/\n"),
            ("dbchange/environments.json", EnvironmentsJson.Dev("file:" + reference).Json()));
        if (file != "outside every repository")
        {
            repository.Write((file, connection));
            OwnerOnly(Path.Combine(repository.Root, file));
        }

        if (how == "committed")
        {
            repository.Commit("the connection file");
        }

        var resolved = SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), repository.Root);

        Assert.Equal(code, resolved.Match<string?>(_ => null, error => error.Code));
        resolved.Match(_ => 0, error =>
        {
            Assert.StartsWith("env:dev's connection, file:" + file + ", ", error.Message, StringComparison.Ordinal);
            Planted.AbsentFrom(error);
            return 0;
        });
    }

    /// <summary>
    /// dbchange/secrets/dev.connection, added with --force and committed though .gitignore lists dbchange/secrets/ and *.connection, is
    /// refused as tracked under each spelling that opens it: its own name; the name in another case on Windows and macOS; on Windows
    /// the name with trailing dots and spaces, which Windows drops, and its 8.3 short name where the volume makes one; and a link
    /// under .dbchange/, which .gitignore also lists: on Windows .dbchange/link, a directory junction to dbchange/secrets/, which Windows
    /// makes without the symbolic-link privilege, and on Linux and macOS .dbchange/link/dev.connection, a symbolic link to the file.
    /// git is asked about the name the folder lists, so .gitignore's patterns never match the spelling instead. Windows opens the
    /// file's default data stream as dev.connection::$DATA, a name no folder lists: reference.unlisted. A spelling that opens no file
    /// (on Linux, every spelling but the file's own name and the link) resolves to nothing. The file's text reaches no connection.
    /// On Windows each row but the 8.3 name must open the file.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "X1")]
    [Trait("Value", "O2")]
    [InlineData("dbchange/secrets/dev.connection", "reference.tracked")]
    [InlineData("dbchange/secrets/Dev.connection", "reference.tracked")]
    [InlineData("dbchange/secrets/dev.connection.", "reference.tracked")]
    [InlineData("dbchange/secrets/dev.connection . .", "reference.tracked")]
    [InlineData("dbchange/secrets/DEV~1.CON", "reference.tracked")]
    [InlineData(".dbchange/link/dev.connection", "reference.tracked")]
    [InlineData("dbchange/secrets/dev.connection::$DATA", "reference.unlisted")]
    public void A_connection_file_git_tracks_is_refused_under_each_spelling_that_opens_it_though_gitignore_lists_it(string reference,
        string code)
    {
        using var repository = new Scratch();
        repository.Commit("the estate", (".gitignore", ".dbchange/\nestate/secrets/\n*.connection\n"),
            ("dbchange/environments.json", EnvironmentsJson.Dev("file:" + reference).Json()));
        repository.Write(("dbchange/secrets/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "dbchange", "secrets", "dev.connection"));
        repository.Git("add", "--force", "--", "dbchange/secrets/dev.connection");
        repository.Git("commit", "-q", "-m", "the connection file");
        using var link = reference == ".dbchange/link/dev.connection" ? Linked(repository.Root) : null;

        var opens = File.Exists(Path.Combine(repository.Root, reference));
        var error = Failed(SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), repository.Root),
            opens ? code : "connection.unresolved");

        Assert.True(opens || !OperatingSystem.IsWindows() || reference.Contains('~', StringComparison.Ordinal),
            reference + " opens no file on Windows");
        Planted.AbsentFrom(error);
    }

    /// <summary>
    /// GIT_CEILING_DIRECTORIES set to the repository's root stops git's search for a repository in dbchange/secrets/, though
    /// git status at the root lists dbchange/secrets/uat.txt as untracked. io/Git clears the variable, so the file is still refused
    /// as one the next git add commits.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_git_does_not_ignore_is_refused_when_GIT_CEILING_DIRECTORIES_stops_the_search_below_its_repository()
    {
        using var repository = new Scratch();
        repository.Commit("the estate", (".gitignore", ".dbchange/\n"),
            ("dbchange/environments.json", EnvironmentsJson.Dev("file:dbchange/secrets/uat.txt").Json()));
        repository.Write(("dbchange/secrets/uat.txt", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "dbchange", "secrets", "uat.txt"));
        var ceiling = Environment.GetEnvironmentVariable("GIT_CEILING_DIRECTORIES");
        Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", repository.Root);
        Result<SqlServer.Database> resolved;
        try
        {
            resolved = SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), repository.Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", ceiling);
        }

        Planted.AbsentFrom(Failed(resolved, "reference.not-ignored"));
    }

    /// <summary>
    /// A .git file whose gitdir names no repository makes git fail its search in the connection file's folder with an error other
    /// than "not a git repository (or any ...)", so git cannot say whether a commit would hold the file: git.failed, the file
    /// unread. The message leads with the environment and the reference, then quotes git's own error.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_in_a_folder_git_cannot_search_is_refused_as_git_failed()
    {
        using var repository = new Scratch();
        repository.Commit("the estate", (".gitignore", ".dbchange/\n"),
            ("dbchange/environments.json", EnvironmentsJson.Dev("file:dbchange/broken/dev.connection").Json()));
        repository.Write(("dbchange/broken/.git", "gitdir: nowhere\n"),
            ("dbchange/broken/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "dbchange", "broken", "dev.connection"));

        var error = Failed(SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), repository.Root), "git.failed");

        Assert.StartsWith(
            "env:dev's connection, file:dbchange/broken/dev.connection, cannot be checked against git: git rev-parse failed: ",
            error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
    }

    /// <summary>
    /// R15: a connection file that exists, in a folder this identity cannot list, that this identity cannot read, or whose
    /// attributes this identity cannot read, holds a host dbchange cannot learn. Before this was a refusal it resolved to nothing, so
    /// env:dev went uncompared and a local server on dev's host was made. Now Resolve refuses it as reference.unlistable,
    /// reference.unreadable or reference.inaccessible, by the environment and the reference, and LocalServer.Unnamed returns that
    /// refusal instead of the server. The denial is a deny entry for RD (list the folder, read the file) on Windows, or mode 0300 or
    /// 0200 on Linux and macOS. For the file whose attributes are withheld, where File.Exists answers false as it does where no file
    /// is, the denial is RD on the folder and RA (read attributes) on the file on Windows, and mode 0600 on the folder, which withholds
    /// search, on Linux and macOS. Each denial is undone after.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Exit", "M1.5")]
    [InlineData("folder", "reference.unlistable")]
    [InlineData("file", "reference.unreadable")]
    [InlineData("attributes", "reference.inaccessible")]
    public void A_connection_file_this_identity_cannot_list_or_read_is_refused_and_leaves_no_environment_uncompared(string denied,
        string code)
    {
        scratch.Folder("locked");
        var file = Written(Path.Combine("locked", "dev.connection"),
            "Server=127.0.0.1,1433;Initial Catalog=Dev;User ID=reader;Password=" + Planted);
        var root = RepositoryAt(EnvironmentsJson.Dev("file:" + file));

        var use = () => (SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), root),
            LocalServer.Unnamed(EnvironmentsAt(root), root, Server("localhost,11433"), Resolver));
        var (resolved, unnamed) = denied switch
        {
            "folder" => ErrorPaths.Denied(scratch.Under("locked"), use),
            "file" => ErrorPaths.Denied(file, use),
            _ => ErrorPaths.Unexaminable(file, use),
        };

        var error = Failed(resolved, code);
        Assert.StartsWith("env:dev's connection, file:" + file + ", ", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
        Assert.Equal(error, Failed(unnamed));
    }

    /// <summary>A repository root in no git repository leaves git unable to say whether it would commit a connection file, so the reference
    /// is refused, saying so, and the file is not read.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_of_a_repository_root_in_no_git_repository_is_refused_saying_git_cannot_check_it()
    {
        var root = EnvironmentsJson.Dev("file:" + Written("dev.connection", "Server=dev-sql;Initial Catalog=Dev;Password=" + Planted))
            .WriteTo(scratch.Folder("no-repository"));

        var error = Failed(SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), root), "reference.no-repository");

        Assert.Contains(root + " is in no git repository", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
    }

    /// <summary>
    /// On Linux and macOS a connection file its group or other users can read is refused by its mode, and one its owner alone
    /// reads resolves. Windows keeps no Unix mode on a file, so there the same file resolves; the Windows and the Ubuntu CI jobs each
    /// assert their own half.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X1")]
    public void A_connection_file_its_group_can_read_is_refused_where_files_carry_a_Unix_mode()
    {
        var file = Written("dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        var resolved = SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")),
            RepositoryAt(EnvironmentsJson.Dev("file:" + file)));

        Assert.Equal(OperatingSystem.IsWindows() ? null : "reference.readable-by-others",
            resolved.Match<string?>(_ => null, error => error.Code));
        resolved.Match(_ => 0, error =>
        {
            Planted.AbsentFrom(error);
            return 0;
        });
        // modes 0640, 0604, 0600
        Assert.Equal(["reference.readable-by-others", "reference.readable-by-others", null],
            ((int[])[0b110_100_000, 0b110_000_100, 0b110_000_000])
            .Select(mode => SqlServer.ReadableByOthers("env:dev's connection, file:" + file + ",", (UnixFileMode)mode)?.Code));
    }
}
