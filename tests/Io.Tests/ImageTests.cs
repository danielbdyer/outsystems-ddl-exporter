using System;
using System.Collections.Generic;
using System.IO;
using Estate.Kernel;
using Estate.Tests;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// Finding ARCH-07: the SQL Server image a copy ran in is what Docker reports for the estate-sql container, read when the copy's
/// server is the port the container publishes, however the copy was reached (ESTATE_SQL or ~/.estate/sql.env); it was the pinned
/// digest compiled into estate, stamped whatever the container ran, and nothing at all whenever ESTATE_SQL was set. A stand-in for
/// docker answers here, so no container is needed.
/// </summary>
public sealed class ImageTests : IDisposable
{
    private static readonly string Pulled = "sha256:" + new string('a', 64);

    private static readonly string Built = "sha256:" + new string('b', 64);

    private readonly ScratchFolder root = ScratchFolder.Temporary("image");

    public void Dispose() => root.Dispose();

    [Fact]
    [Trait("Category", "fast")]
    public void A_copy_on_the_container_s_port_ran_in_the_image_Docker_reports_by_its_registry_digest()
    {
        Assert.Equal(Pulled, LocalServer.Image(Copy("Server=127.0.0.1,11433;User ID=sa"), Docker(["mcr.microsoft.com/mssql/server@" + Pulled])));
        Assert.Equal(Pulled, LocalServer.Image(Copy("Server=tcp:localhost,11433;User ID=sa"), Docker(["mirror.example/mssql/server@" + Pulled])));
        Assert.NotEqual(Doctor.ImageDigest, LocalServer.Image(Copy("Server=127.0.0.1,11433;User ID=sa"), Docker(["mcr.microsoft.com/mssql/server@" + Pulled])));
    }

    /// <summary>An image built or loaded on the machine has no registry digest; its image id, the digest of its content, names it instead.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_copy_on_a_container_whose_image_came_from_no_registry_ran_in_the_image_Docker_names_by_its_id() =>
        Assert.Equal(Built, LocalServer.Image(Copy("Server=127.0.0.1,11433;User ID=sa"), Docker([])));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Server=127.0.0.1,1433;User ID=sa")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Integrated Security=true")]
    [InlineData("Server=dev-sql,11433;User ID=sa")]
    public void A_copy_on_another_server_than_the_container_names_no_image(string server) =>
        Assert.Null(LocalServer.Image(Copy(server), Docker(["mcr.microsoft.com/mssql/server@" + Pulled])));

    [Fact]
    [Trait("Category", "fast")]
    public void Without_docker_or_its_container_no_image_is_named()
    {
        Assert.Null(LocalServer.Image(Copy("Server=127.0.0.1,11433;User ID=sa"), (command, _) => new Ran.NotFound(command.Program, "not on the PATH")));
        Assert.Null(LocalServer.Image(Copy("Server=127.0.0.1,11433;User ID=sa"), (_, _) => new Ran.Exited(1, "", "Error: No such container: estate-sql\n")));
        Assert.Null(LocalServer.Image(Copy("Server=127.0.0.1,11433;User ID=sa"), (command, _) => new Ran.TimedOut(command.Timeout, "", "")));
    }

    private SqlServer.Copy Copy(string server) => new(CopyName.Make("host", 1, 1), server + ";Initial Catalog=master", root.Path);

    /// <summary>docker as it answers for a running estate-sql container publishing 1433 on 127.0.0.1:11433, from an image of id <see cref="Built"/> pulled by the registry digests given.</summary>
    private static Runner Docker(IReadOnlyList<string> repoDigests) => (command, _) => (command.Program, command.Arguments) switch
    {
        ("docker", ["container", "inspect", ..]) => new Ran.Exited(0, Built + " {\"1433/tcp\":[{\"HostIp\":\"127.0.0.1\",\"HostPort\":\"11433\"}]}\n", ""),
        ("docker", ["image", "inspect", .., var image]) when image == Built => new Ran.Exited(0, System.Text.Json.JsonSerializer.Serialize(repoDigests) + "\n", ""),
        _ => new Ran.Exited(1, "", ""),
    };
}
