using System;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Osm.TestSupport;

internal static class DockerAvailability
{
    private const string DefaultSkipMessage = "Docker engine is unavailable.";
    private const string DefaultUnixEndpoint = "unix:///var/run/docker.sock";
    private const string DefaultWindowsEndpoint = "npipe://./pipe/docker_engine";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private static readonly Lazy<DockerAvailabilityResult> Availability = new(
        CheckAvailability,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static bool TryGetAvailability(out string? reason)
    {
        var result = Availability.Value;
        reason = result.Reason ?? DefaultSkipMessage;
        return result.IsAvailable;
    }

    public static void EnsureAvailable()
    {
        if (!TryGetAvailability(out var reason))
        {
            throw new InvalidOperationException(reason);
        }
    }

    public static bool IsConnectivityException(Exception exception)
    {
        return exception switch
        {
            DockerApiException => true,
            HttpRequestException => true,
            SocketException => true,
            TimeoutException => true,
            TaskCanceledException => true,
            AggregateException aggregate when aggregate.InnerExceptions.All(IsConnectivityException) => true,
            _ => false
        };
    }

    public static string GetDefaultSkipMessage() => DefaultSkipMessage;

    private static DockerAvailabilityResult CheckAvailability()
    {
        try
        {
            using var client = new DockerClientConfiguration(GetDockerUri()).CreateClient();
            using var timeout = new CancellationTokenSource(ProbeTimeout);
            client.System.PingAsync(timeout.Token).GetAwaiter().GetResult();
            return DockerAvailabilityResult.Available();
        }
        catch (Exception ex) when (IsConnectivityException(ex))
        {
            return DockerAvailabilityResult.Unavailable(ex.Message);
        }
    }

    private static Uri GetDockerUri()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            return new Uri(dockerHost);
        }

        if (OperatingSystem.IsWindows())
        {
            return new Uri(DefaultWindowsEndpoint);
        }

        return new Uri(DefaultUnixEndpoint);
    }

    private sealed record DockerAvailabilityResult(bool IsAvailable, string? Reason)
    {
        public static DockerAvailabilityResult Available() => new(true, null);

        public static DockerAvailabilityResult Unavailable(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return new(false, DefaultSkipMessage);
            }

            return new(false, $"{DefaultSkipMessage} Details: {reason}");
        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class DockerFactAttribute : Xunit.FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.TryGetAvailability(out var reason))
        {
            Skip = reason;
        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class DockerTheoryAttribute : Xunit.TheoryAttribute
{
    public DockerTheoryAttribute()
    {
        if (!DockerAvailability.TryGetAvailability(out var reason))
        {
            Skip = reason;
        }
    }
}
