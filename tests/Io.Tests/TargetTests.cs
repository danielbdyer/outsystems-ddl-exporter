using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Estate.Budgets.Tests;
using Estate.Budgets.Tests.Register;
using Estate.Kernel;
using Estate.Tests;
using Microsoft.Data.SqlClient;
using Xunit;
using static Estate.Tests.Expect;

namespace Estate.Io.Tests;

/// <summary>
/// io/SqlServer's targets and connections (V3_MILESTONES.md WP 1.4; VALUES.md X1, X2): the target grammar as a closed type; env:
/// resolved against estate/posture.json and copy: against .estate/copies.json alone, on the server its row records; R15 by spelling
/// and by address, failing closed on what it cannot read; a connection reference resolved to the caller's integrated identity unless it
/// names another; and no error or printed value carrying what a reference resolves to.
/// </summary>
public sealed class TargetTests : IDisposable
{
    private static readonly PlantedValue Planted = PlantedValue.Password;

    private readonly ScratchFolder scratch = ScratchFolder.Temporary("targets");

    private readonly Lazy<Scratch> repository = new(() => new Scratch());

    public void Dispose()
    {
        scratch.Dispose();
        if (repository.IsValueCreated)
        {
            repository.Value.Dispose();
        }
    }

    /// <summary>VALUES.md X1: a literal connection string given where a target goes is refused as one, and nothing of it is quoted.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "X1")]
    [InlineData("Server=db;User ID=estate;Password=" + PlantedValue.PasswordText)]
    [InlineData("Data Source=db;Initial Catalog=Orders;Integrated Security=True;Application Name=" + PlantedValue.PasswordText)]
    [InlineData("env:dev;Pwd=" + PlantedValue.PasswordText)]
    public void A_literal_connection_string_as_a_target_is_connection_literal_and_quoted_nowhere(string text)
    {
        var error = Failed(SqlServer.Target(text, "--target"), "connection.literal");

        Assert.Contains("--target", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
    }

    /// <summary>copy: resolves against .estate/copies.json alone, and a name it does not hold is refused.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    [Trait("Value", "S7")]
    [Trait("Exit", "M1.5")]
    public void A_copy_the_registry_does_not_hold_is_refused()
    {
        var error = Failed(SqlServer.Resolve(Value(SqlServer.Target("copy:estate_nowhere_1_00000000", "--target")), scratch.Path), "copy.unregistered");

        Assert.Contains("copy:estate_nowhere_1_00000000", error.Message, StringComparison.Ordinal);
    }

    /// <summary>copy: before a name no copy estate makes can carry names a copy the registry does not hold, so it is refused as one, and the name is quoted nowhere.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Exit", "M1.5")]
    [InlineData("copy:")]
    [InlineData("copy:Estate-Copy")]
    [InlineData("copy:Estate_host_1_0a1b2c3d")]
    [InlineData("copy:estate-host")]
    [InlineData("copy:estate host")]
    [InlineData("copy:estate_host_1_0a1b2c3d\n")]
    public void A_copy_named_as_no_copy_can_be_is_refused_and_its_name_is_quoted_nowhere(string text)
    {
        var error = Failed(SqlServer.Target(text, "--target"), "copy.unregistered");

        Assert.Contains("--target", error.Message, StringComparison.Ordinal);
        Assert.Contains(".estate/copies.json", error.Message, StringComparison.Ordinal);
        Assert.All(new[] { text["copy:".Length..].Trim() }.Where(name => name.Length > 0), name => Assert.DoesNotContain(name, error.Message + error.Remedy, StringComparison.Ordinal));
    }

    /// <summary>
    /// R15: a scratch server on the host an environment names in estate/posture.json is refused, before anything connects, the environment's
    /// reference agreeing with that host, whether or not it names a database; a reference that names no server names SqlClient's local
    /// default instance. The refusal names the environment and quotes neither connection.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Law", "a named environment cannot be written")]
    [Trait("Exit", "M1.5")]
    [InlineData("Server=tcp:127.0.0.1,1433;Initial Catalog=Dev", "127.0.0.1", "localhost,11433")]
    [InlineData("Server=localhost;Initial Catalog=Dev", "localhost", "127.0.0.1,11433")]
    [InlineData("Server=(local)\\SQLEXPRESS;Initial Catalog=Dev", "localhost", ".")]
    [InlineData("Server=dev-sql.corp.example,1433;Initial Catalog=Dev", "DEV-SQL.corp.example", "tcp:DEV-SQL.corp.example,11433")]
    [InlineData("Server=prod-sql.corp.example;Integrated Security=true", "prod-sql.corp.example", "prod-sql.corp.example,1")]
    [InlineData("Data Source=tcp:prod-sql.corp.example,1433", "prod-sql.corp.example", "PROD-SQL.corp.example,1")]
    [InlineData("Initial Catalog=Dev;Integrated Security=true", "localhost", "localhost,1")]
    public void A_scratch_server_on_the_host_an_environment_names_is_refused(string reference, string host, string server)
    {
        var root = Estate(PostureFile.Dev("file:" + Written("dev.connection", reference + ";User ID=reader;Password=" + Planted), host));

        var error = Failed(ScratchServer.Create(root, "Server=" + server + ";Initial Catalog=master;User ID=sa;Password=" + Planted + ";TrustServerCertificate=True;Connect Timeout=2"), "copy.named-host");

        Assert.Contains("env:dev", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
        Assert.False(File.Exists(Path.Combine(root, ".estate", "copies.json")), "a refused scratch server registered a copy");
    }

    /// <summary>
    /// The ruling of 2026-09-25: each environment names its host, so R15 compares every environment. Before it, an environment whose
    /// reference resolved to nothing on this machine, its variable unset here, went uncompared, and a copy was made on its host.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_scratch_server_on_the_host_of_an_environment_whose_reference_resolves_to_nothing_here_is_refused()
    {
        var root = Estate(new PostureFile(new Dictionary<string, PostureFile.Environment> { ["dev"] = Unresolved("localhost"), ["uat"] = Unresolved("dev-sql") }));

        var local = Failed(ScratchServer.Unnamed(Posture(root), root, Server("localhost,11433"), Resolver), "copy.named-host");
        var aliased = Failed(ScratchServer.Unnamed(Posture(root), root, Server("192.0.2.10,1433"), Resolver), "copy.named-host");

        Assert.Contains("env:dev", local.Message, StringComparison.Ordinal);
        Assert.Contains("env:uat", aliased.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// R15 fails closed: an environment whose reference resolves here to text SqlClient reads no connection string from has a server no
    /// check can place, so the scratch server is refused by that reference; and without estate/posture.json no environment's host
    /// can be read, so no copy is made. Neither refusal quotes a connection, and neither registers a copy.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("an unreadable reference", "connection.malformed")]
    [InlineData("no posture", "posture.missing")]
    public void A_scratch_server_whose_environments_cannot_be_read_is_refused_before_anything_connects(string how, string code)
    {
        var root = how == "no posture" ? scratch.Folder("no-posture") : Estate(PostureFile.Dev("file:" + Written("dev.connection", "Server=dev-sql;Nonsense " + Planted + " = 1")));

        var error = Failed(ScratchServer.Create(root, "Server=127.0.0.1,1;Initial Catalog=master;User ID=sa;Password=" + Planted + ";Connect Timeout=2"), code);

        Assert.Equal(how != "no posture", error.Message.StartsWith("env:dev's connection", StringComparison.Ordinal));
        Planted.AbsentFrom(error);
        Assert.False(File.Exists(Path.Combine(root, ".estate", "copies.json")), "a refused scratch server registered a copy");
    }

    /// <summary>
    /// R15 compares the host the posture names, so a reference that resolves here to a server on another host is refused by the
    /// environment (posture.host) before a copy is made: the posture and the connection disagree about where the environment
    /// is. The refusal names the posture's host, which the repository holds, and not the host the reference names.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_reference_to_a_server_on_another_host_than_the_posture_names_is_refused_by_the_environment()
    {
        var root = Estate(PostureFile.Dev("file:" + Written("dev.connection", "Server=prod-sql.corp.example,1433;Initial Catalog=Dev;User ID=reader;Password=" + Planted), "dev-sql"));

        var error = Failed(ScratchServer.Create(root, "Server=127.0.0.1,1;Initial Catalog=master;User ID=sa;Password=" + Planted + ";Connect Timeout=2"), "posture.host");

        Assert.StartsWith("env:dev's connection", error.Message, StringComparison.Ordinal);
        Assert.Contains("dev-sql", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("prod-sql", error.Message + error.Remedy, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
        Assert.False(File.Exists(Path.Combine(root, ".estate", "copies.json")), "a refused scratch server registered a copy");
    }

    /// <summary>
    /// R15 by address: a scratch server is on an environment's host when the two hosts share an address, whatever either spelling, a name
    /// and its FQDN, a name and its IP address; this machine is every loopback address, LocalDB and each address of its own. DNS is
    /// the resolver given here, so no lookup leaves the test.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Exit", "M1.5")]
    [InlineData("dev-sql", "dev-sql.corp.example,11433")]
    [InlineData("dev-sql.corp.example", "dev-sql,11433")]
    [InlineData("192.0.2.10", "dev-sql,11433")]
    [InlineData("dev-sql", "192.0.2.10")]
    [InlineData("::ffff:192.0.2.10", "dev-sql")]
    [InlineData("127.0.0.2", "localhost,11433")]
    [InlineData("(localdb)", "localhost,11433")]
    [InlineData("localhost", "(localdb)\\MSSQLLocalDB")]
    [InlineData("sql.this-machine.example", "localhost,11433")]
    public void A_scratch_server_on_an_alias_of_an_environment_s_host_is_refused(string host, string server)
    {
        var root = Estate(PostureFile.Of("dev", Unresolved(host)));

        var error = Failed(ScratchServer.Unnamed(Posture(root), root, Server(server), Resolver), "copy.named-host");

        Assert.Contains("env:dev", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A scratch server whose host shares no address and no spelling with any environment's host is cleared, the environment's reference resolving here or not.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=prod-sql.corp.example;Initial Catalog=Prod", "prod-sql.corp.example", "localhost,11433")]
    [InlineData("Server=dev-sql;Initial Catalog=Dev", "dev-sql", "192.0.2.20,11433")]
    [InlineData("Server=no-such-host.corp.example;Initial Catalog=Dev", "no-such-host.corp.example", "localhost,11433")]
    [InlineData("", "prod-sql.corp.example", "localhost,11433")]
    public void A_scratch_server_on_a_host_no_environment_names_is_cleared(string reference, string host, string server)
    {
        var root = Estate(reference.Length == 0 ? PostureFile.Of("dev", Unresolved(host)) : PostureFile.Dev("file:" + Written("dev.connection", reference), host));

        Assert.Equal(Server(server), Value(ScratchServer.Unnamed(Posture(root), root, Server(server), Resolver)));
    }

    /// <summary>
    /// The registry bound to its server: a copy's row records the server it was made on, and copy: resolves it only while the scratch
    /// server is that server; on another it is a copy the registry does not hold there. A row whose server is on the host an
    /// environment names is refused before the scratch server is chosen.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Exit", "M1.5")]
    public void A_copy_resolves_only_on_the_server_its_row_records_and_never_on_a_named_host()
    {
        var name = CopyName.Make("host", 1, 0x0a1b2c3d);
        var clear = Registry(Estate(new PostureFile(new Dictionary<string, PostureFile.Environment>())), name, "localhost,11433");
        var named = Registry(Estate(PostureFile.Dev("file:" + Written("dev.connection", "Server=127.0.0.1,1433;Initial Catalog=Dev"), "localhost")), name, "localhost,11433");

        var there = Value(ScratchServer.Registered(clear, name, Io.Posture.Environments(clear), () => "Server=tcp:127.0.0.1,11433;User ID=sa;Password=" + Planted, Resolver));
        var elsewhere = Failed(ScratchServer.Registered(clear, name, Io.Posture.Environments(clear), () => "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true", Resolver), "copy.unregistered");
        var onNamedHost = Failed(ScratchServer.Registered(named, name, Io.Posture.Environments(named), () => throw new Xunit.Sdk.XunitException("the scratch server was chosen before R15 read the row's server"), Resolver), "copy.named-host");

        Assert.Equal(("copy:" + name, "localhost,11433"), (there.Target.ToString(), ScratchServer.ServerName(null, Written("sql.env", "ESTATE_SQL_PORT=11433\nMSSQL_SA_PASSWORD=" + Planted), false).Match(n => n.ToString(), r => r.Code)));
        Assert.Contains("copy:" + name, elsewhere.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(elsewhere);
        Planted.AbsentFrom(onNamedHost);
    }

    /// <summary>The caller's integrated identity by default; SQL authentication where the reference names it; and an EnvironmentDatabase prints as its environment alone.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=dev-sql;Initial Catalog=Dev", true)]
    [InlineData("Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + PlantedValue.PasswordText, false)]
    public void A_reference_resolves_to_the_caller_s_integrated_identity_unless_it_names_another(string connection, bool integrated)
    {
        var variable = "ESTATE_TEST_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        Environment.SetEnvironmentVariable(variable, connection);
        try
        {
            var root = Estate(PostureFile.Of("qa", new("env:" + variable)));

            var named = Assert.IsType<SqlServer.EnvironmentDatabase>(Value(SqlServer.Resolve(Value(SqlServer.Target("env:qa", "--target")), root)));

            var resolved = new SqlConnectionStringBuilder(named.Connection);
            Assert.Equal((integrated, "Dev"), (resolved.IntegratedSecurity, resolved.InitialCatalog));
            Assert.Equal(integrated ? "" : "reader", resolved.UserID);
            Assert.Equal("env:qa", named.Target.ToString());
            Planted.AbsentFrom(named.ToString() + named.Target + named.Environment);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>
    /// estate adds no TLS keyword to a named environment's connection: the reference's own Encrypt and HostNameInCertificate reach the
    /// server as written, so a corporate certificate check holds, and a reference that names none gets none, so SqlClient's default,
    /// a certificate the machine trusts, applies. Only the local scratch server's connection trusts its self-signed certificate.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=dev-sql;Initial Catalog=Dev;Encrypt=Strict;HostNameInCertificate=dev-sql.corp.example", "Strict", "dev-sql.corp.example")]
    [InlineData("Server=dev-sql;Initial Catalog=Dev", null, null)]
    public void A_reference_reaches_the_server_with_its_own_TLS_keywords_and_no_other(string connection, string? encrypt, string? hostNameInCertificate)
    {
        var root = Estate(PostureFile.Of("qa", new("file:" + Written("qa.connection", connection))));

        var resolved = new SqlConnectionStringBuilder(Value(SqlServer.Resolve(Value(SqlServer.Target("env:qa", "--target")), root)).Connection);

        Assert.Equal((encrypt, hostNameInCertificate), (resolved.ShouldSerialize("Encrypt") ? resolved.Encrypt.ToString() : null, resolved.ShouldSerialize("Host Name In Certificate") ? resolved.HostNameInCertificate : null));
        Assert.False(resolved.ShouldSerialize("Trust Server Certificate"), "estate set TrustServerCertificate on a named environment's connection");
    }

    /// <summary>
    /// Finding R-7: ESTATE_SQL, the scratch server the operator names, is configuration, so a value SqlClient reads no connection string
    /// from is connection.malformed, not a server that does not answer; and the same text given as a named environment's reference is
    /// refused by the one parse, alike, its text withheld from both, since it can hold a password.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_malformed_ESTATE_SQL_is_connection_malformed_as_the_same_text_is_as_a_reference_its_text_withheld()
    {
        var malformed = "Server=db;User ID=sa;Password=" + Planted + ";Nonsense " + Planted + " = 1";
        var root = Estate(PostureFile.Dev("file:" + Written("dev.connection", malformed)));

        var scratchServer = Failed(ScratchServer.ServerName(malformed, scratch.Under("no-sql.env"), localDb: false), "connection.malformed");
        var reference = Failed(SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), root), "connection.malformed");

        Assert.StartsWith("ESTATE_SQL", scratchServer.Message, StringComparison.Ordinal);
        Assert.StartsWith("env:dev's connection", reference.Message, StringComparison.Ordinal);
        Assert.All(new[] { scratchServer, reference }, error =>
        {
            Assert.EndsWith(" is no connection string SqlClient reads; its text is withheld.", error.Message, StringComparison.Ordinal);
            Planted.AbsentFrom(error);
        });
    }

    /// <summary>A reference that resolves to nothing, or to no connection string that names its database, is refused by the reference, and quotes nothing it read.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env", "connection.unresolved")]
    [InlineData("missing file", "connection.unresolved")]
    [InlineData("not a connection string", "connection.malformed")]
    [InlineData("no database", "connection.malformed")]
    public void A_reference_that_resolves_to_no_connection_is_refused_by_the_reference_and_quotes_nothing_it_read(string how, string code)
    {
        var reference = how switch
        {
            "env" => "env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            "missing file" => "file:" + scratch.Under("absent.connection"),
            "not a connection string" => "file:" + Written("garbled.connection", "Nonsense " + Planted + " = 1"),
            _ => "file:" + Written("bare.connection", "Server=dev-sql;User ID=reader;Password=" + Planted),
        };
        var root = Estate(PostureFile.Of("qa", new(reference)));

        var error = Failed(SqlServer.Resolve(Value(SqlServer.Target("env:qa", "--target")), root), code);

        Assert.Contains("env:qa", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
    }

    /// <summary>
    /// Windows forbids ? * &lt; &gt; and | in a file name, so no file is at a path whose name holds one; File.Exists answers false and
    /// File.GetAttributes throws an IOException for ERROR_INVALID_NAME. On Linux and macOS the name is legal and no file is there.
    /// On every operating system the reference resolves to nothing: Resolve fails with connection.unresolved, and
    /// ScratchServer.Unnamed leaves env:dev uncompared and returns the server, as it does for any path with no file.
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
        var root = Estate(PostureFile.Dev("file:" + scratch.Path.Replace('\\', '/') + "/" + name));

        Failed(SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), root), "connection.unresolved");
        Assert.Equal(Server("localhost,11433"), Value(ScratchServer.Unnamed(Posture(root), root, Server("localhost,11433"), Resolver)));
    }

    /// <summary>
    /// kernel/Environments.cs documents a file: reference as naming a file outside git, and the estate's own principal files sit under
    /// .estate/, which .gitignore lists. A connection file git tracks, or one git does not ignore, which the next git add would commit,
    /// is refused by the environment and the reference, before the file is read, and quotes nothing it holds; one git ignores, or one in
    /// no git repository while the estate's root is in one, resolves.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "X1")]
    [InlineData("estate/dev.connection", "committed", "reference.tracked")]
    [InlineData("estate/dev.connection", "written", "reference.not-ignored")]
    [InlineData(".estate/dev.connection", "written", null)]
    [InlineData("outside every repository", "written", null)]
    public void A_connection_file_git_tracks_or_does_not_ignore_is_refused_and_one_git_ignores_resolves(string file, string how, string? code)
    {
        using var repository = new Scratch();
        var connection = "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted;
        var reference = file == "outside every repository" ? Written("dev.connection", connection) : file;
        repository.Commit("the estate", (".gitignore", ".estate/\n"), ("estate/posture.json", PostureFile.Dev("file:" + reference).Json()));
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
    [Trait("Value", "X1")]
    [Trait("Value", "O2")]
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
        repository.Commit("the estate", (".gitignore", ".estate/\nestate/secrets/\n*.connection\n"), ("estate/posture.json", PostureFile.Dev("file:" + reference).Json()));
        repository.Write(("estate/secrets/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "estate", "secrets", "dev.connection"));
        repository.Git("add", "--force", "--", "estate/secrets/dev.connection");
        repository.Git("commit", "-q", "-m", "the connection file");
        using var link = reference == ".estate/link/dev.connection" ? Linked(repository.Root) : null;

        var opens = File.Exists(Path.Combine(repository.Root, reference));
        var error = Failed(SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), repository.Root), opens ? code : "connection.unresolved");

        Assert.True(opens || !OperatingSystem.IsWindows() || reference.Contains('~', StringComparison.Ordinal), reference + " opens no file on Windows");
        Planted.AbsentFrom(error);
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
        repository.Commit("the estate", (".gitignore", ".estate/\n"), ("estate/posture.json", PostureFile.Dev("file:estate/secrets/uat.txt").Json()));
        repository.Write(("estate/secrets/uat.txt", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "estate", "secrets", "uat.txt"));
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
        repository.Commit("the estate", (".gitignore", ".estate/\n"), ("estate/posture.json", PostureFile.Dev("file:estate/broken/dev.connection").Json()));
        repository.Write(("estate/broken/.git", "gitdir: nowhere\n"), ("estate/broken/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + Planted));
        OwnerOnly(Path.Combine(repository.Root, "estate", "broken", "dev.connection"));

        var error = Failed(SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), repository.Root), "git.failed");

        Assert.StartsWith("env:dev's connection, file:estate/broken/dev.connection, cannot be checked against git: git rev-parse failed: ", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
    }

    /// <summary>
    /// R15: a connection file that exists, in a folder this identity cannot list, that this identity cannot read, or whose
    /// attributes this identity cannot read, holds a host estate cannot learn. Before this was a refusal it resolved to nothing, so
    /// env:dev went uncompared and a scratch server on dev's host was made. Now Resolve refuses it as reference.unlistable,
    /// reference.unreadable or reference.inaccessible, by the environment and the reference, and ScratchServer.Unnamed returns that
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
    public void A_connection_file_this_identity_cannot_list_or_read_is_refused_and_leaves_no_environment_uncompared(string denied, string code)
    {
        scratch.Folder("locked");
        var file = Written(Path.Combine("locked", "dev.connection"), "Server=127.0.0.1,1433;Initial Catalog=Dev;User ID=reader;Password=" + Planted);
        var root = Estate(PostureFile.Dev("file:" + file));

        var use = () => (SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), root), ScratchServer.Unnamed(Posture(root), root, Server("localhost,11433"), Resolver));
        var (resolved, unnamed) = denied switch
        {
            "folder" => RefusalPaths.Denied(scratch.Under("locked"), use),
            "file" => RefusalPaths.Denied(file, use),
            _ => RefusalPaths.Unexaminable(file, use),
        };

        var error = Failed(resolved, code);
        Assert.StartsWith("env:dev's connection, file:" + file + ", ", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
        Assert.Equal(error, Failed(unnamed));
    }

    /// <summary>An estate's root in no git repository leaves git unable to say whether it would commit a connection file, so the reference is refused, saying so, and the file is not read.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_connection_file_of_an_estate_root_in_no_git_repository_is_refused_saying_git_cannot_check_it()
    {
        var root = PostureFile.Dev("file:" + Written("dev.connection", "Server=dev-sql;Initial Catalog=Dev;Password=" + Planted)).WriteTo(scratch.Folder("no-repository"));

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

        var resolved = SqlServer.Resolve(Value(SqlServer.Target("env:dev", "--target")), Estate(PostureFile.Dev("file:" + file)));

        Assert.Equal(OperatingSystem.IsWindows() ? null : "reference.readable-by-others", resolved.Match<string?>(_ => null, error => error.Code));
        resolved.Match(_ => 0, error =>
        {
            Planted.AbsentFrom(error);
            return 0;
        });
        Assert.Equal(["reference.readable-by-others", "reference.readable-by-others", null], ((int[])[0b110_100_000, 0b110_000_100, 0b110_000_000])   // modes 0640, 0604, 0600
            .Select(mode => SqlServer.ReadableByOthers("env:dev's connection, file:" + file + ",", (UnixFileMode)mode)?.Code));
    }

    /// <summary>ref: and dacpac: name no database; the synthetic copy arrives in M3.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("ref:main", "target.not-a-database")]
    [InlineData("dacpac:build/x.dacpac", "target.not-a-database")]
    [InlineData("synthetic-copy", "synthetic-copy.not-built")]
    public void A_target_that_is_no_database_this_build_reads_is_refused_where_a_database_is_asked_for(string text, string code) =>
        Failed(SqlServer.Resolve(Value(SqlServer.Target(text, "--target")), scratch.Path), code);

    /// <summary>
    /// VALUES.md X2, §18: a named environment's SQL Server error is withheld whatever its number, and a denied login says a lead's
    /// prediction will appear on the pull request; a copy's rows are generated, so a copy's failure keeps SQL Server's message. The rows
    /// pin the one classifier of SQL Server's numbers: a login, a database or a permission refused; no answer, or a timeout before the
    /// connection opened; a timeout of a statement on an open connection; and a statement that failed, a deadlock among them. Every
    /// error of the classifier is of the category server, whatever its detail.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "X2")]
    [Trait("Exit", "M1.7")]
    [InlineData(18456, false, "server.denied")]
    [InlineData(4060, false, "server.denied")]
    [InlineData(229, false, "server.denied")]
    [InlineData(-2, false, "server.unreachable")]
    [InlineData(53, false, "server.unreachable")]
    [InlineData(258, false, "server.unreachable")]
    [InlineData(40613, false, "server.unreachable")]
    [InlineData(-2, true, "server.timed-out")]
    [InlineData(18456, true, "server.denied")]
    [InlineData(245, false, "server.failed")]
    [InlineData(1205, false, "server.failed")]
    [InlineData(1205, true, "server.failed")]
    [InlineData(2628, false, "server.failed")]
    public void A_named_environment_s_error_is_withheld_and_a_copy_s_is_kept(int number, bool opened, string code)
    {
        var root = Estate(PostureFile.Of("qa", new("file:" + Written("qa.connection", "Server=qa-sql;Initial Catalog=Qa"), "qa-sql")));
        var named = Value(SqlServer.Resolve(Value(SqlServer.Target("env:qa", "--target")), root));
        var copy = new SqlServer.Copy(CopyName.Make("host", 1, 0x0a1b2c3d), "Server=localhost,11433;User ID=sa;Password=" + Planted, root);
        var message = "Conversion failed when converting the nvarchar value '" + Planted + "' to data type int.";

        var (fromNamed, fromCopy) = (named.ErrorOf(number, message, fatal: false, opened), copy.ErrorOf(number, message, fatal: false, opened));

        Assert.Equal((code, ErrorCategory.Server, code), (fromNamed.Code, fromNamed.Category, fromCopy.Code));
        Assert.StartsWith("env:qa ", fromNamed.Message, StringComparison.Ordinal);
        Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Msg {number}"), fromNamed.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(fromNamed);
        Assert.DoesNotContain(Planted.Text, fromCopy.Remedy, StringComparison.Ordinal);
        Assert.Equal(code == "server.denied", fromNamed.Message.Contains("a lead's prediction will appear on the pull request", StringComparison.Ordinal));
        Assert.Equal(code == "server.failed", fromCopy.Message.Contains(Planted.Text, StringComparison.Ordinal));
    }

    /// <summary>
    /// DacFx's own failure through io/DacFx.Failed: DacPackageExtensions.BuildPackage over a view on a table the model lacks throws
    /// DacServicesException, whose Message already holds each of its three SQL71501 messages and which holds no SqlException. The error is
    /// dacfx.failed for a named environment and a copy alike, quoting DacFx's words with each SQL71501 message once.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("env:qa")]
    [InlineData("copy:estate_host_1_0a1b2c3d")]
    public void A_DacFx_failure_with_no_SQL_Server_error_inside_is_dacfx_failed_quoting_its_SQL7_codes(string target)
    {
        var root = Estate(PostureFile.Of("qa", new("file:" + Written("qa.connection", "Server=qa-sql;Initial Catalog=Qa"), "qa-sql")));
        SqlServer.Database database = target == "env:qa"
            ? Value(SqlServer.Resolve(Value(SqlServer.Target(target, "--target")), root))
            : new SqlServer.Copy(CopyName.Make("host", 1, 0x0a1b2c3d), "Server=localhost,11433;User ID=sa;Password=" + Planted, root);
        var failure = Assert.IsType<Microsoft.SqlServer.Dac.DacServicesException>(Record.Exception(() =>
        {
            using var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160, new Microsoft.SqlServer.Dac.Model.TSqlModelOptions());
            model.AddObjects("CREATE VIEW dbo.V AS SELECT Id FROM dbo.Missing;");
            Microsoft.SqlServer.Dac.DacPackageExtensions.BuildPackage(scratch.Under("unresolved.dacpac"), model, new Microsoft.SqlServer.Dac.PackageMetadata());
        }));

        var error = DacFx.Failed(database, failure);

        Assert.Equal(3, failure.Messages.Count(m => m.Prefix + m.Number == "SQL71501"));
        Assert.Equal("dacfx.failed", error.Code);
        Assert.StartsWith("DacFx failed against " + target + " with no SQL Server error inside: Error SQL71501: ", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, error.Message.Split("SQL71501").Length - 1);
        Assert.Contains("[dbo].[V] has an unresolved reference to object [dbo].[Missing].", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', error.Message);
    }

    /// <summary>
    /// The scratch server named by a ~/.estate/sql.env whose port nothing listens on, as after the container stopped: creating a copy
    /// is refused as a server that does not answer, with the remedy that starts the container, and the row written before the CREATE
    /// DATABASE is taken out of the registry again. A closed port answers at once, so no server is needed.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_scratch_server_that_does_not_answer_names_ci_sql_up_and_leaves_no_registry_row()
    {
        var root = Estate(new PostureFile(new Dictionary<string, PostureFile.Environment>()));
        var port = ClosedPort();
        var server = Value(ScratchServer.Server(null, Written("sql.env", "MSSQL_SA_PASSWORD=" + Planted + "\nESTATE_SQL_PORT=" + port + "\n"), localDb: false));

        var error = Failed(ScratchServer.Create(root, server), "server.unreachable");

        Assert.Contains("ci/sql.sh up", error.Remedy, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
        Assert.Empty(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".estate", "copies.json")))!["copies"]!.AsArray());
    }

    /// <summary>A loopback port nothing listens on: bound and released, so a connection to it is refused at once.</summary>
    private static int ClosedPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>A server as this machine reads its data source.</summary>
    private static ServerName Server(string dataSource) => ServerName.Of(dataSource, Environment.MachineName);

    /// <summary>DNS as these tests have it: dev-sql and its FQDN at one TEST-NET address, prod-sql at another, a name of this machine at loopback, and nothing else.</summary>
    private static IPAddress[] Resolver(string host) => host switch
    {
        "dev-sql" or "dev-sql.corp.example" => [IPAddress.Parse("192.0.2.10")],
        "prod-sql.corp.example" => [IPAddress.Parse("192.0.2.20")],
        "sql.this-machine.example" => [IPAddress.Loopback],
        _ => [],
    };

    /// <summary>An environment on the host given whose connection is a variable no process sets, so it resolves to nothing here.</summary>
    private static PostureFile.Environment Unresolved(string host) => new("env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), host);

    /// <summary>The estate's posture, read.</summary>
    private static Environments Posture(string root) => Value(Io.Posture.Environments(root));

    /// <summary>The estate's root with .estate/copies.json holding one copy, made on the server given.</summary>
    private static string Registry(string root, CopyName name, string server)
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
        OwnerOnly(scratch.File(file, text));
        return scratch.Under(file).Replace('\\', '/');
    }

    /// <summary>An estate's root in a git repository of its own, holding the posture given; the connection files stay outside it.</summary>
    private string Estate(PostureFile posture) => posture.WriteTo(Directory.CreateDirectory(Path.Combine(repository.Value.Root, "estate-" + Guid.NewGuid().ToString("N")[..8])).FullName);
}
