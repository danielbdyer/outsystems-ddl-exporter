using System;
using System.IO;
using System.Linq;
using System.Net;
using Estate.Budgets.Tests;
using Estate.Budgets.Tests.Register;
using Estate.Cli;
using Estate.Kernel;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// io/SqlServer's targets and connections (V3_MILESTONES.md WP 1.4, M1 exits 5 and 7; VALUES.md X1, X2): the target grammar as a
/// closed type; env: resolved against estate/posture.json and copy: against .estate/copies.json alone, on the server its row records;
/// R15 by spelling and by address, failing closed on what it cannot read; a connection reference resolved to the caller's integrated
/// identity unless it names another; and no error or printed value carrying what a reference resolves to.
/// </summary>
public sealed class TargetTests : IDisposable
{
    private const string Planted = "Pa55!planted#7f3a";

    private readonly string scratch = Directory.CreateTempSubdirectory("estate-targets-").FullName;

    private readonly Lazy<Scratch> repository = new(() => new Scratch());

    public void Dispose()
    {
        Directory.Delete(scratch, recursive: true);
        if (repository.IsValueCreated)
        {
            repository.Value.Dispose();
        }
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env:dev", "Env", "dev")]
    [InlineData("env:uat-2", "Env", "uat-2")]
    [InlineData("copy:estate_danny_pc_4242_0a1b2c3d", "Copy", "estate_danny_pc_4242_0a1b2c3d")]
    [InlineData("synthetic-copy", "SyntheticCopy", "")]
    [InlineData("ref:main", "Ref", "main")]
    [InlineData("ref:origin/release/2026.09", "Ref", "origin/release/2026.09")]
    [InlineData("dacpac:.estate/build/0a1b/SampleCatalog.dacpac", "Dacpac", ".estate/build/0a1b/SampleCatalog.dacpac")]
    public void The_target_grammar_reads_each_form_into_its_case_and_writes_it_back(string text, string form, string named)
    {
        var target = Made(SqlServer.Target.Parse(text));

        Assert.Equal(form, target.GetType().Name);
        Assert.Equal(named, target.Match(e => e.Name, c => c.Name, () => "", r => r.Name, d => d.Path));
        Assert.Equal(text, target.ToString());
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("sql:dev")]
    [InlineData("dev")]
    [InlineData("env:")]
    [InlineData("env:DEV")]
    [InlineData("env:ESTATE_DEV")]
    [InlineData("synthetic-copy:dev")]
    [InlineData("ref:")]
    [InlineData("ref:-n")]
    [InlineData("dacpac:")]
    [InlineData("")]
    public void An_unknown_target_form_is_a_bad_argument_at_exit_1(string text)
    {
        var error = Failed(SqlServer.Target.Parse(text));

        Assert.Equal(("target.unknown", 1), (error.Code, Contract.Exit(error)));
    }

    /// <summary>VALUES.md X1, M1 exit 7: a literal connection string given where a target goes is exit 6, and nothing of it is quoted.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=db;User ID=estate;Password=" + Planted)]
    [InlineData("Data Source=db;Initial Catalog=Orders;Integrated Security=True;Application Name=" + Planted)]
    [InlineData("env:dev;Pwd=" + Planted)]
    public void A_literal_connection_string_as_a_target_is_exit_6_and_quoted_nowhere(string text)
    {
        var error = Failed(SqlServer.Target.Parse(text, "--target"));

        Assert.Equal(("connection.literal", 6), (error.Code, Contract.Exit(error)));
        Assert.Contains("--target", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>M1 exit 5: copy: resolves against .estate/copies.json alone, and a name it does not hold is exit 9.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    public void A_copy_the_registry_does_not_hold_is_exit_9()
    {
        var error = Failed(SqlServer.Resolve(Made(SqlServer.Target.Parse("copy:estate_nowhere_1_00000000")), scratch));

        Assert.Equal(("copy.unregistered", 9), (error.Code, Contract.Exit(error)));
        Assert.Contains("copy:estate_nowhere_1_00000000", error.Message, StringComparison.Ordinal);
    }

    /// <summary>M1 exit 5: copy: before a name no copy estate makes can carry names a copy the registry does not hold, so it is exit 9 as well, and the name is quoted nowhere.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("copy:")]
    [InlineData("copy:Estate-Copy")]
    [InlineData("copy:Estate_host_1_0a1b2c3d")]
    [InlineData("copy:estate-host")]
    [InlineData("copy:estate host")]
    [InlineData("copy:estate_host_1_0a1b2c3d\n")]
    public void A_copy_named_as_no_copy_can_be_is_exit_9_and_its_name_is_quoted_nowhere(string text)
    {
        var error = Failed(SqlServer.Target.Parse(text, "--target"));

        Assert.Equal(("copy.unregistered", 9), (error.Code, Contract.Exit(error)));
        Assert.Contains("--target", error.Message, StringComparison.Ordinal);
        Assert.Contains(".estate/copies.json", error.Message, StringComparison.Ordinal);
        Assert.All(new[] { text["copy:".Length..].Trim() }.Where(name => name.Length > 0), name => Assert.DoesNotContain(name, error.Message + error.Remedy, StringComparison.Ordinal));
    }

    /// <summary>
    /// M1 exit 5, R15: a scratch server on the host an environment's reference resolves to is exit 9, before anything connects, whether
    /// or not the reference names a database; a reference that names no server names SqlClient's local default instance. The refusal
    /// names the environment and quotes neither connection.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=tcp:127.0.0.1,1433;Initial Catalog=Dev", "localhost,11433")]
    [InlineData("Server=localhost;Initial Catalog=Dev", "127.0.0.1,11433")]
    [InlineData("Server=(local)\\SQLEXPRESS;Initial Catalog=Dev", ".")]
    [InlineData("Server=dev-sql.corp.example,1433;Initial Catalog=Dev", "tcp:DEV-SQL.corp.example,11433")]
    [InlineData("Server=prod-sql.corp.example;Integrated Security=true", "prod-sql.corp.example,1")]
    [InlineData("Data Source=tcp:prod-sql.corp.example,1433", "PROD-SQL.corp.example,1")]
    [InlineData("Initial Catalog=Dev;Integrated Security=true", "localhost,1")]
    [Trait("Law", "a named environment cannot be written")]
    public void A_scratch_server_on_a_host_an_environment_s_reference_names_is_exit_9(string reference, string server)
    {
        var root = Estate(Dev(Written("dev.connection", reference + ";User ID=reader;Password=" + Planted)));

        var error = Failed(ScratchServer.Create(root, "Server=" + server + ";Initial Catalog=master;User ID=sa;Password=" + Planted + ";TrustServerCertificate=True;Connect Timeout=2"));

        Assert.Equal(("copy.named-host", 9), (error.Code, Contract.Exit(error)));
        Assert.Contains("env:dev", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, ".estate", "copies.json")), "a refused scratch server registered a copy");
    }

    /// <summary>
    /// R15 fails closed: an environment whose reference resolves here to text SqlClient reads no connection string from has a host no
    /// check can clear, so the scratch server is refused at exit 6 by that reference; and without estate/posture.json no environment's host
    /// can be read, so no copy is made. Neither refusal quotes a connection, and neither registers a copy.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("an unreadable reference", "connection.malformed")]
    [InlineData("no posture", "posture.missing")]
    public void A_scratch_server_whose_environments_cannot_be_read_is_refused_before_anything_connects(string how, string code)
    {
        var root = how == "no posture" ? Directory.CreateDirectory(Path.Combine(scratch, "no-posture")).FullName
            : Estate(Dev(Written("dev.connection", "Server=dev-sql;Nonsense " + Planted + " = 1")));

        var error = Failed(ScratchServer.Create(root, "Server=127.0.0.1,1;Initial Catalog=master;User ID=sa;Password=" + Planted + ";Connect Timeout=2"));

        Assert.Equal((code, 6), (error.Code, Contract.Exit(error)));
        Assert.Equal(how != "no posture", error.Message.StartsWith("env:dev's connection", StringComparison.Ordinal));
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, ".estate", "copies.json")), "a refused scratch server registered a copy");
    }

    /// <summary>
    /// R15 by address: a scratch server is on an environment's host when the two hosts share an address, whatever either spelling, a name
    /// and its FQDN, a name and its IP address; this machine is every loopback address, LocalDB and each address of its own. DNS is
    /// the resolver given here, so no lookup leaves the test.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("dev-sql", "dev-sql.corp.example,11433")]
    [InlineData("dev-sql.corp.example,1433", "dev-sql,11433")]
    [InlineData("192.0.2.10,1433", "dev-sql,11433")]
    [InlineData("dev-sql", "192.0.2.10")]
    [InlineData("[::ffff:192.0.2.10],1433", "dev-sql")]
    [InlineData("127.0.0.2,1433", "localhost,11433")]
    [InlineData("(localdb)\\MSSQLLocalDB", "localhost,11433")]
    [InlineData("localhost", "(localdb)\\MSSQLLocalDB")]
    [InlineData("sql.this-machine.example", "localhost,11433")]
    public void A_scratch_server_on_an_alias_of_an_environment_s_host_is_exit_9(string environment, string server)
    {
        var root = Estate(Dev(Written("dev.connection", "Server=" + environment + ";Initial Catalog=Dev;User ID=reader;Password=" + Planted)));

        var error = Failed(ScratchServer.Unnamed(root, server, Resolver));

        Assert.Equal(("copy.named-host", 9), (error.Code, Contract.Exit(error)));
        Assert.Contains("env:dev", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>A scratch server whose host shares no address and no spelling with any environment's is cleared; an environment whose reference resolves to nothing here goes uncompared.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=prod-sql.corp.example;Initial Catalog=Prod", "localhost,11433")]
    [InlineData("Server=dev-sql;Initial Catalog=Dev", "192.0.2.20,11433")]
    [InlineData("Server=no-such-host.corp.example;Initial Catalog=Dev", "localhost,11433")]
    [InlineData("", "localhost,11433")]
    public void A_scratch_server_on_a_host_no_environment_s_reference_names_is_cleared(string reference, string server)
    {
        var root = Estate(reference.Length == 0 ? "\"dev\": { \"connection\": \"env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }"
            : Dev(Written("dev.connection", reference)));

        Assert.Equal(server, Made(ScratchServer.Unnamed(root, server, Resolver)));
    }

    /// <summary>
    /// M1 exit 5, the registry bound to its server: a copy's row records the server it was made on, and copy: resolves it only while
    /// the scratch server is that server; on another it is a copy the registry does not hold there. A row whose server an environment's
    /// reference names is exit 9 before the scratch server is chosen.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_copy_resolves_only_on_the_server_its_row_records_and_never_on_a_named_host()
    {
        const string Name = "estate_host_1_0a1b2c3d";
        var (clear, named) = (Registry(Estate(""), Name, "localhost,11433"), Registry(Estate(Dev(Written("dev.connection", "Server=127.0.0.1,1433;Initial Catalog=Dev"))), Name, "localhost,11433"));

        var there = Made(ScratchServer.Registered(clear, Name, () => "Server=tcp:127.0.0.1,11433;User ID=sa;Password=" + Planted, Resolver));
        var elsewhere = Failed(ScratchServer.Registered(clear, Name, () => "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true", Resolver));
        var onNamedHost = Failed(ScratchServer.Registered(named, Name, () => throw new Xunit.Sdk.XunitException("the scratch server was chosen before R15 read the row's server"), Resolver));

        Assert.Equal(("copy:" + Name, "localhost,11433"), (there.Target, ScratchServer.ServerName(null, Written("sql.env", "ESTATE_SQL_PORT=11433\nMSSQL_SA_PASSWORD=" + Planted), false).Match(n => n, r => r.Code)));
        Assert.Equal(("copy.unregistered", 9), (elsewhere.Code, Contract.Exit(elsewhere)));
        Assert.Contains("copy:" + Name, elsewhere.Message, StringComparison.Ordinal);
        Assert.Equal(("copy.named-host", 9), (onNamedHost.Code, Contract.Exit(onNamedHost)));
        Assert.DoesNotContain(Planted, elsewhere.Message + elsewhere.Remedy + onNamedHost.Message + onNamedHost.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_host_is_this_machine_however_the_connection_spells_it_and_otherwise_its_name_in_lower_case()
    {
        foreach (var local in (string[])["", "localhost", "127.0.0.1,11433", "tcp:127.0.0.1,1433", ".", "(local)", "[::1],1433", Environment.MachineName + "\\SQLEXPRESS", "tcp:" + Environment.MachineName.ToLowerInvariant()])
        {
            Assert.Equal("localhost", SqlServer.Host(local));
        }

        Assert.Equal("dev-sql.corp.example", SqlServer.Host("tcp:DEV-SQL.corp.example,1433"));
        Assert.Equal("dev-sql", SqlServer.Host("np:\\\\DEV-SQL\\pipe\\sql\\query"));
        Assert.Equal("(localdb)", SqlServer.Host("(localdb)\\MSSQLLocalDB"));
    }

    /// <summary>The caller's integrated identity by default; SQL authentication where the reference names it; and an EnvironmentDatabase prints as its environment alone.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=dev-sql;Initial Catalog=Dev", true)]
    [InlineData("Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted, false)]
    public void A_reference_resolves_to_the_caller_s_integrated_identity_unless_it_names_another(string connection, bool integrated)
    {
        var variable = "ESTATE_TEST_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        Environment.SetEnvironmentVariable(variable, connection);
        try
        {
            var root = Estate("\"qa\": { \"connection\": \"env:" + variable + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }");

            var named = Assert.IsType<SqlServer.EnvironmentDatabase>(Made(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:qa")), root)));

            var resolved = new SqlConnectionStringBuilder(named.Connection);
            Assert.Equal((integrated, "Dev"), (resolved.IntegratedSecurity, resolved.InitialCatalog));
            Assert.Equal(integrated ? "" : "reader", resolved.UserID);
            Assert.Equal("env:qa", named.ToString());
            Assert.Equal("env:qa", named.Target);
            Assert.DoesNotContain(Planted, named.ToString() + named.Target + named.Environment, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>A reference that resolves to nothing, or to no connection string that names its database, is exit 6 by the reference, and quotes nothing it read.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env", "connection.unresolved")]
    [InlineData("missing file", "connection.unresolved")]
    [InlineData("not a connection string", "connection.malformed")]
    [InlineData("no database", "connection.malformed")]
    public void A_reference_that_resolves_to_no_connection_is_exit_6_and_quotes_nothing_it_read(string how, string code)
    {
        var reference = how switch
        {
            "env" => "env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            "missing file" => "file:" + Path.Combine(scratch, "absent.connection"),
            "not a connection string" => "file:" + Written("garbled.connection", "Nonsense " + Planted + " = 1"),
            _ => "file:" + Written("bare.connection", "Server=dev-sql;User ID=reader;Password=" + Planted),
        };
        var root = Estate("\"qa\": { \"connection\": \"" + reference.Replace("\\", "\\\\", StringComparison.Ordinal) + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }");

        var error = Failed(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:qa")), root));

        Assert.Equal((code, 6), (error.Code, Contract.Exit(error)));
        Assert.Contains("env:qa", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// Windows forbids ? * &lt; &gt; and | in a file name, so no file is at a path whose name holds one; File.Exists answers false and
    /// File.GetAttributes throws an IOException for ERROR_INVALID_NAME. On Linux and macOS the name is legal and no file is there.
    /// On every operating system the reference resolves to nothing: Resolve fails with connection.unresolved, and
    /// ScratchServer.Unnamed leaves env:dev uncompared and returns the server, as it does for any path with no file.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("dev?.connection")]
    [InlineData("dev*.connection")]
    [InlineData("dev<.connection")]
    [InlineData("dev>.connection")]
    [InlineData("dev|.connection")]
    public void A_connection_file_whose_name_Windows_forbids_resolves_to_nothing_on_every_operating_system(string name)
    {
        var root = Estate(Dev(scratch.Replace('\\', '/') + "/" + name));

        var error = Failed(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), root));

        Assert.Equal(("connection.unresolved", 6), (error.Code, Contract.Exit(error)));
        Assert.Equal("localhost,11433", Made(ScratchServer.Unnamed(root, "localhost,11433", Resolver)));
    }

    /// <summary>
    /// kernel/NamedEnvironment.cs documents a file: reference as naming a file outside git, and the estate's own principal
    /// files sit under .estate/, which .gitignore lists. A connection file git tracks, or one git does not ignore, which the next
    /// git add would commit, is exit 6 by the environment and the reference, before the file is read, and quotes nothing it holds;
    /// one git ignores, or one in no git repository while the estate's root is in one, resolves.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("estate/dev.connection", "committed", "reference.tracked")]
    [InlineData("estate/dev.connection", "written", "reference.not-ignored")]
    [InlineData(".estate/dev.connection", "written", null)]
    [InlineData("outside every repository", "written", null)]
    public void A_connection_file_git_tracks_or_does_not_ignore_is_refused_and_one_git_ignores_resolves(string file, string how, string? code)
    {
        using var repository = new Scratch();
        const string Connection = "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted;
        var reference = file == "outside every repository" ? Written("dev.connection", Connection) : file;
        repository.Commit("the estate", (".gitignore", ".estate/\n"), ("estate/posture.json", "{ \"environments\": { " + Dev(reference) + " } }"));
        if (file != "outside every repository")
        {
            repository.Write((file, Connection));
            OwnerOnly(Path.Combine(repository.Root, file));
        }

        if (how == "committed")
        {
            repository.Commit("the connection file");
        }

        var resolved = SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), repository.Root);

        Assert.Equal(code, resolved.Match<string?>(_ => null, error => error.Code));
        resolved.Match(_ => 0, error =>
        {
            Assert.Equal(6, Contract.Exit(error));
            Assert.StartsWith("env:dev's connection, file:" + file + ", ", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
            return 0;
        });
    }

    /// <summary>
    /// estate/secrets/dev.connection, added with --force and committed though .gitignore lists estate/secrets/ and *.connection, is
    /// refused as tracked under each spelling that opens it: its own name; the name in another case on Windows and macOS; on Windows
    /// the name with trailing dots and spaces, which Windows drops, and its 8.3 short name where the volume makes one; and a link
    /// under .estate/, which .gitignore also lists: on Windows .estate/link, a directory junction to estate/secrets/, which Windows
    /// makes without the symbolic-link privilege, and on Linux and macOS .estate/link/dev.connection, a symbolic link to the file.
    /// git is asked about the name the folder lists, so .gitignore's patterns never match the spelling instead. Windows opens the
    /// file's default data stream as dev.connection::$DATA, a name no folder lists: reference.unlisted. A spelling that opens no file
    /// (on Linux, every spelling but the file's own name and the link) resolves to nothing. The file's text reaches no connection.
    /// On Windows each row but the 8.3 name must open the file.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("estate/secrets/dev.connection", "reference.tracked")]
    [InlineData("estate/secrets/Dev.connection", "reference.tracked")]
    [InlineData("estate/secrets/dev.connection.", "reference.tracked")]
    [InlineData("estate/secrets/dev.connection . .", "reference.tracked")]
    [InlineData("estate/secrets/DEV~1.CON", "reference.tracked")]
    [InlineData(".estate/link/dev.connection", "reference.tracked")]
    [InlineData("estate/secrets/dev.connection::$DATA", "reference.unlisted")]
    public void A_connection_file_git_tracks_is_refused_under_each_spelling_that_opens_it_though_gitignore_lists_it(string reference, string code)
    {
        using var repository = new Scratch();
        repository.Commit("the estate", (".gitignore", ".estate/\nestate/secrets/\n*.connection\n"), ("estate/posture.json", "{ \"environments\": { " + Dev(reference) + " } }"));
        repository.Write(("estate/secrets/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "estate", "secrets", "dev.connection"));
        repository.Git("add", "--force", "--", "estate/secrets/dev.connection");
        repository.Git("commit", "-q", "-m", "the connection file");
        using var link = reference == ".estate/link/dev.connection" ? Linked(repository.Root) : null;

        var opens = File.Exists(Path.Combine(repository.Root, reference));
        var error = Failed(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), repository.Root));

        Assert.True(opens || !OperatingSystem.IsWindows() || reference.Contains('~', StringComparison.Ordinal), reference + " opens no file on Windows");
        Assert.Equal((opens ? code : "connection.unresolved", 6), (error.Code, Contract.Exit(error)));
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// GIT_CEILING_DIRECTORIES set to the repository's root stops git's search for a repository in estate/secrets/, though
    /// git status at the root lists estate/secrets/uat.txt as untracked. io/Git clears the variable, so the file is still refused
    /// as one the next git add commits.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_git_does_not_ignore_is_refused_when_GIT_CEILING_DIRECTORIES_stops_the_search_below_its_repository()
    {
        using var repository = new Scratch();
        repository.Commit("the estate", (".gitignore", ".estate/\n"), ("estate/posture.json", "{ \"environments\": { " + Dev("estate/secrets/uat.txt") + " } }"));
        repository.Write(("estate/secrets/uat.txt", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "estate", "secrets", "uat.txt"));
        var ceiling = Environment.GetEnvironmentVariable("GIT_CEILING_DIRECTORIES");
        Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", repository.Root);
        Result<SqlServer.Database> resolved;
        try
        {
            resolved = SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), repository.Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", ceiling);
        }

        var error = Failed(resolved);

        Assert.Equal(("reference.not-ignored", 6), (error.Code, Contract.Exit(error)));
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// A .git file whose gitdir names no repository makes git fail its search in the connection file's folder with an error other
    /// than "not a git repository (or any ...)", so git cannot say whether a commit would hold the file: git.failed, exit 6, the file
    /// unread. The message leads with the environment and the reference, then quotes git's own error.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_in_a_folder_git_cannot_search_is_refused_as_git_failed()
    {
        using var repository = new Scratch();
        repository.Commit("the estate", (".gitignore", ".estate/\n"), ("estate/posture.json", "{ \"environments\": { " + Dev("estate/broken/dev.connection") + " } }"));
        repository.Write(("estate/broken/.git", "gitdir: nowhere\n"), ("estate/broken/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "estate", "broken", "dev.connection"));

        var error = Failed(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), repository.Root));

        Assert.Equal(("git.failed", 6), (error.Code, Contract.Exit(error)));
        Assert.StartsWith("env:dev's connection, file:estate/broken/dev.connection, cannot be checked against git: git rev-parse failed: ", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// M1 exit 5, R15: a connection file that exists, in a folder this identity cannot list, that this identity cannot read, or whose
    /// attributes this identity cannot read, holds a host estate cannot learn. Before this was a refusal it resolved to nothing, so
    /// env:dev went uncompared and a scratch server on dev's host was made. Now Resolve refuses it at exit 6, reference.unlistable,
    /// reference.unreadable or reference.inaccessible, by the environment and the reference, and ScratchServer.Unnamed returns that
    /// refusal instead of the server. The denial is a deny entry for RD (list the folder, read the file) on Windows, or mode 0300 or
    /// 0200 on Linux and macOS. For the file whose attributes are withheld, where File.Exists answers false as it does where no file
    /// is, the denial is RD on the folder and RA (read attributes) on the file on Windows, and mode 0600 on the folder, which withholds
    /// search, on Linux and macOS. Each denial is undone after.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("folder", "reference.unlistable")]
    [InlineData("file", "reference.unreadable")]
    [InlineData("attributes", "reference.inaccessible")]
    public void A_connection_file_this_identity_cannot_list_or_read_is_refused_and_leaves_no_environment_uncompared(string denied, string code)
    {
        Directory.CreateDirectory(Path.Combine(scratch, "locked"));
        var file = Written(Path.Combine("locked", "dev.connection"), "Server=127.0.0.1,1433;Initial Catalog=Dev;User ID=reader;Password=" + Planted);
        var root = Estate(Dev(file));

        var use = () => (SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), root), ScratchServer.Unnamed(root, "localhost,11433", Resolver));
        var (resolved, unnamed) = denied switch
        {
            "folder" => RefusalPaths.Denied(Path.Combine(scratch, "locked"), use),
            "file" => RefusalPaths.Denied(file, use),
            _ => RefusalPaths.Unexaminable(file, use),
        };

        var error = Failed(resolved);
        Assert.Equal((code, 6), (error.Code, Contract.Exit(error)));
        Assert.StartsWith("env:dev's connection, file:" + file + ", ", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
        Assert.Equal(error, Failed(unnamed));
    }

    /// <summary>
    /// With LANGUAGE and LC_MESSAGES set to German in estate's own process, a connection file in no repository resolves and is not
    /// refused as git.failed: io/Git still finds git's "not a git repository (or any ...)". A git that carries no German translation
    /// passes this whatever variables reach it; GitTests' stand-in for git shows which variables reach it.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_in_no_repository_resolves_while_the_caller_asks_for_git_s_messages_in_German()
    {
        var root = Estate(Dev(Written("dev.connection", "Server=dev-sql;Initial Catalog=Dev")));
        var asked = (Language: Environment.GetEnvironmentVariable("LANGUAGE"), Messages: Environment.GetEnvironmentVariable("LC_MESSAGES"));
        (string?, string?) resolved;
        Environment.SetEnvironmentVariable("LANGUAGE", "de");
        Environment.SetEnvironmentVariable("LC_MESSAGES", "de_DE.UTF-8");
        try
        {
            resolved = SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), root).Match<(string?, string?)>(database => (database.Target, null), error => (null, error.Code + ": " + error.Message));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LANGUAGE", asked.Language);
            Environment.SetEnvironmentVariable("LC_MESSAGES", asked.Messages);
        }

        Assert.Equal(("env:dev", null), resolved);
    }

    /// <summary>An estate's root in no git repository leaves git unable to say whether it would commit a connection file, so the reference is refused, saying so, and the file is not read.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_of_an_estate_root_in_no_git_repository_is_refused_saying_git_cannot_check_it()
    {
        var root = Directory.CreateDirectory(Path.Combine(scratch, "no-repository")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "estate"));
        File.WriteAllText(Path.Combine(root, "estate", "posture.json"), "{ \"environments\": { " + Dev(Written("dev.connection", "Server=dev-sql;Initial Catalog=Dev;Password=" + Planted)) + " } }");

        var error = Failed(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), root));

        Assert.Equal(("reference.no-repository", 6), (error.Code, Contract.Exit(error)));
        Assert.Contains(root + " is in no git repository", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>
    /// On Linux and macOS a connection file its group or other users can read is refused by its mode, and one its owner alone
    /// reads resolves. Windows keeps no Unix mode on a file, so there the same file resolves; the Windows and the Ubuntu CI jobs each
    /// assert their own half.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_its_group_can_read_is_refused_where_files_carry_a_Unix_mode()
    {
        var file = Written("dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        var resolved = SqlServer.Resolve(Made(SqlServer.Target.Parse("env:dev")), Estate(Dev(file)));

        Assert.Equal(OperatingSystem.IsWindows() ? null : "reference.readable-by-others", resolved.Match<string?>(_ => null, error => error.Code));
        resolved.Match(_ => 0, error =>
        {
            Assert.DoesNotContain(Planted, error.Message + error.Remedy, StringComparison.Ordinal);
            return 0;
        });
        Assert.Equal(["reference.readable-by-others", "reference.readable-by-others", null], ((int[])[0b110_100_000, 0b110_000_100, 0b110_000_000])   // modes 0640, 0604, 0600
            .Select(mode => SqlServer.ReadableByOthers("env:dev's connection, file:" + file + ",", (UnixFileMode)mode)?.Code));
    }

    /// <summary>ref: and dacpac: name no database; the synthetic copy arrives in M3.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("ref:main", "target.not-a-database", 1)]
    [InlineData("dacpac:build/x.dacpac", "target.not-a-database", 1)]
    [InlineData("synthetic-copy", "synthetic-copy.not-built", 6)]
    public void A_target_that_is_no_database_this_build_reads_is_refused_where_a_database_is_asked_for(string text, string code, int exit)
    {
        var error = Failed(SqlServer.Resolve(Made(SqlServer.Target.Parse(text)), scratch));

        Assert.Equal((code, exit), (error.Code, Contract.Exit(error)));
    }

    /// <summary>
    /// VALUES.md X2, M1 exit 7, §18: a named environment's SQL Server error is withheld whatever its number, and a denied login says a lead's
    /// prediction will appear on the pull request; a copy's rows are minted, so a copy's failure keeps the engine's message.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(18456, "server.denied")]
    [InlineData(4060, "server.denied")]
    [InlineData(229, "server.denied")]
    [InlineData(-2, "server.unreachable")]
    [InlineData(53, "server.unreachable")]
    [InlineData(258, "server.unreachable")]
    [InlineData(245, "server.failed")]
    [InlineData(2628, "server.failed")]
    public void A_named_environment_s_error_is_withheld_and_a_copy_s_is_kept(int number, string code)
    {
        var root = Estate("\"qa\": { \"connection\": \"file:" + Written("qa.connection", "Server=qa-sql;Initial Catalog=Qa") + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }");
        var named = Made(SqlServer.Resolve(Made(SqlServer.Target.Parse("env:qa")), root));
        var copy = new SqlServer.Copy("estate_host_1_0a1b2c3d", "Server=localhost,11433;User ID=sa;Password=" + Planted, root);
        var message = "Conversion failed when converting the nvarchar value '" + Planted + "' to data type int.";

        var (fromNamed, fromCopy) = (named.ErrorOf(number, message), copy.ErrorOf(number, message));

        Assert.Equal((code, 4), (fromNamed.Code, Contract.Exit(fromNamed)));
        Assert.StartsWith("env:qa ", fromNamed.Message, StringComparison.Ordinal);
        Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Msg {number}"), fromNamed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, fromNamed.Message + fromNamed.Remedy + fromCopy.Remedy, StringComparison.Ordinal);
        Assert.Equal(code == "server.denied", fromNamed.Message.Contains("a lead's prediction will appear on the pull request", StringComparison.Ordinal));
        Assert.Equal(code == "server.failed", fromCopy.Message.Contains(Planted, StringComparison.Ordinal));
    }

    /// <summary>
    /// DacFx's own failure through Database.ErrorOf: DacPackageExtensions.BuildPackage over a view on a table the model lacks throws
    /// DacServicesException, whose Message already holds each of its three SQL71501 messages and which holds no SqlException. The error is
    /// dacfx.failed at exit 6 for a named environment and a copy alike, quoting DacFx's words with each SQL71501 message once.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env:qa")]
    [InlineData("copy:estate_host_1_0a1b2c3d")]
    public void A_DacFx_failure_with_no_SQL_Server_error_inside_is_dacfx_failed_quoting_its_SQL7_codes(string target)
    {
        Telemetry.OptOut();
        var root = Estate("\"qa\": { \"connection\": \"file:" + Written("qa.connection", "Server=qa-sql;Initial Catalog=Qa") + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }");
        SqlServer.Database database = target == "env:qa"
            ? Made(SqlServer.Resolve(Made(SqlServer.Target.Parse(target)), root))
            : new SqlServer.Copy("estate_host_1_0a1b2c3d", "Server=localhost,11433;User ID=sa;Password=" + Planted, root);
        var failure = Assert.IsType<Microsoft.SqlServer.Dac.DacServicesException>(Record.Exception(() =>
        {
            using var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160, new Microsoft.SqlServer.Dac.Model.TSqlModelOptions());
            model.AddObjects("CREATE VIEW dbo.V AS SELECT Id FROM dbo.Missing;");
            Microsoft.SqlServer.Dac.DacPackageExtensions.BuildPackage(Path.Combine(scratch, "unresolved.dacpac"), model, new Microsoft.SqlServer.Dac.PackageMetadata());
        }));

        var error = database.ErrorOf(failure);

        Assert.Equal(3, failure.Messages.Count(m => m.Prefix + m.Number == "SQL71501"));
        Assert.Equal(("dacfx.failed", 6), (error.Code, Contract.Exit(error)));
        Assert.StartsWith("DacFx failed against " + target + " with no SQL Server error inside: Cannot save package to file.", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, error.Message.Split("SQL71501").Length - 1);
        Assert.Contains("[dbo].[V] has an unresolved reference to object [dbo].[Missing].", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', error.Message);
    }

    /// <summary>DNS as these tests have it: dev-sql and its FQDN at one TEST-NET address, prod-sql at another, a name of this machine at loopback, and nothing else.</summary>
    private static IPAddress[] Resolver(string host) => host switch
    {
        "dev-sql" or "dev-sql.corp.example" => [IPAddress.Parse("192.0.2.10")],
        "prod-sql.corp.example" => [IPAddress.Parse("192.0.2.20")],
        "sql.this-machine.example" => [IPAddress.Loopback],
        _ => [],
    };

    /// <summary>The environment dev in posture JSON, its connection the file given.</summary>
    private static string Dev(string connectionFile) => "\"dev\": { \"connection\": \"file:" + connectionFile + "\", \"profile\": \"estate/profiles/pipeline.publish.xml\" }";

    /// <summary>The estate's root with .estate/copies.json holding one copy, made on the server given.</summary>
    private static string Registry(string root, string name, string server)
    {
        Directory.CreateDirectory(Path.Combine(root, ".estate"));
        File.WriteAllText(Path.Combine(root, ".estate", "copies.json"),
            "{ \"copies\": [ { \"name\": \"" + name + "\", \"server\": \"" + server + "\", \"host\": \"host\", \"pid\": 1, \"created\": \"2026-09-24T00:00:00Z\" } ] }");
        return root;
    }

    /// <summary>On Linux and macOS, the file's mode set to 0600, so its owner alone reads it, as a connection file must be; Windows keeps no such mode.</summary>
    private static void OwnerOnly(string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// .estate/link/dev.connection in the repository, opening estate/secrets/dev.connection. On Windows .estate/link is a directory
    /// junction to estate/secrets/, made by cmd's mklink /J, since File.CreateSymbolicLink needs Developer Mode or an administrator's
    /// rights there; what is returned removes the junction alone, since Directory.Delete's recursive delete cannot remove a junction
    /// without those rights either. On Linux and macOS .estate/link/dev.connection is a symbolic link to the file, and nothing is returned.
    /// </summary>
    private static IDisposable? Linked(string root)
    {
        var (link, secrets) = (Path.Combine(root, ".estate", "link"), Path.Combine(root, "estate", "secrets"));
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(link);
            File.CreateSymbolicLink(Path.Combine(link, "dev.connection"), Path.Combine(secrets, "dev.connection"));
            return null;
        }

        Directory.CreateDirectory(Path.Combine(root, ".estate"));
        var mklink = new Command("cmd.exe", ["/c", "mklink", "/J", link, secrets], TimeSpan.FromMinutes(1)).Finish();
        Assert.True(mklink.Code == 0, "mklink /J exited " + mklink.Code + ": " + mklink.Errors);
        return new Removal(() => Directory.Delete(link));
    }

    private sealed class Removal(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }

    /// <summary>A connection file under the scratch folder, in no git repository and read by its owner alone; its path with '/'.</summary>
    private string Written(string file, string text)
    {
        File.WriteAllText(Path.Combine(scratch, file), text);
        OwnerOnly(Path.Combine(scratch, file));
        return Path.Combine(scratch, file).Replace('\\', '/');
    }

    /// <summary>An estate's root in a git repository of its own, whose estate/posture.json names the environments given; the connection files stay outside it.</summary>
    private string Estate(string environments)
    {
        var root = Directory.CreateDirectory(Path.Combine(repository.Value.Root, "estate-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        Directory.CreateDirectory(Path.Combine(root, "estate"));
        File.WriteAllText(Path.Combine(root, "estate", "posture.json"), "{ \"environments\": { " + environments + " } }");
        return root;
    }

    private static T Made<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    private static Error Failed<T>(Result<T> result) => Assert.IsType<Result<T>.Failed>(result).Error;
}
