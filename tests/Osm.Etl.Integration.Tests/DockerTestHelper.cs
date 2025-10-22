using System;
using Docker.DotNet;

namespace Osm.Etl.Integration.Tests;

internal static class DockerTestHelper
{
    private static readonly Lazy<(bool IsAvailable, string Reason)> Availability = new(CheckAvailability);

    public static bool TryEnsureDocker(out string skipReason)
    {
        var (isAvailable, reason) = Availability.Value;
        skipReason = reason;
        return isAvailable;
    }

    private static (bool IsAvailable, string Reason) CheckAvailability()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        var endpoint = string.IsNullOrWhiteSpace(dockerHost) ? GetDefaultDockerEndpoint() : dockerHost;

        try
        {
            var uri = new Uri(endpoint);
            using var client = new DockerClientConfiguration(uri).CreateClient();
            client.System.PingAsync().GetAwaiter().GetResult();
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Docker is not available for integration tests: {ex.Message}");
        }
    }

    private static string GetDefaultDockerEndpoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return "npipe://./pipe/docker_engine";
        }

        return "unix:///var/run/docker.sock";
    }
}
