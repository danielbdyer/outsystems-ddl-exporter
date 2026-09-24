using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace Osm.Cli.Tests;

internal static class DotNetCli
{
    private const string CommandSeparator = " -- ";
    private const string TargetFramework = "net9.0";

    private static readonly string BuildConfiguration =
        typeof(DotNetCli).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
        ?? "Debug";

    private static readonly SemaphoreSlim InProcessLock = new(1, 1);
    private static readonly Lazy<Task> DotNetInfoVerification = new(
        VerifyDotNetInfoAsync,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static async Task<CommandResult> RunAsync(string workingDirectory, string arguments)
    {
        await EnsureSdkAvailableAsync().ConfigureAwait(false);

        var invocation = TryCreateInvocation(arguments);
        if (invocation.Success)
        {
            return await RunInProcessAsync(workingDirectory, invocation.CliArguments).ConfigureAwait(false);
        }

        var startInfo = CreateStartInfo(workingDirectory, arguments);
        return await RunExternalAsync(startInfo).ConfigureAwait(false);
    }

    public static Task EnsureSdkAvailableAsync()
    {
        return DotNetInfoVerification.Value;
    }

    public static ProcessStartInfo CreateStartInfo(string workingDirectory, string arguments)
    {
        var invocation = TryCreateInvocation(arguments);
        var commandArguments = invocation.Success
            ? invocation.ProcessArguments
            : EnsureNoBuildAndConfiguration(arguments);

        return new ProcessStartInfo("dotnet", commandArguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private static async Task VerifyDotNetInfoAsync()
    {
        var startInfo = new ProcessStartInfo("dotnet", "--info")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        CommandResult result;
        try
        {
            result = await RunExternalAsync(startInfo).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not SkipException)
        {
            throw CreateSkipException(
                $"`dotnet --info` could not be executed. Install the .NET 9 SDK as described in notes/run-checklist.md before running CLI integration tests. Underlying error: {ex.Message}");
        }

        if (result.ExitCode == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("`dotnet --info` must succeed before running CLI integration tests.");
        builder.AppendLine("Install the .NET 9 SDK and confirm the tooling setup via notes/run-checklist.md.");
        builder.AppendLine();
        builder.Append(result.FormatFailureMessage(expectedExitCode: 0));

        throw CreateSkipException(builder.ToString());
    }

    private static SkipException CreateSkipException(string message)
    {
        var skipConstructor = typeof(SkipException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        if (skipConstructor is not null)
        {
            return (SkipException)skipConstructor.Invoke(new object[] { message });
        }

        var skipInstance = (SkipException)FormatterServices.GetUninitializedObject(typeof(SkipException));
        var messageField = typeof(Exception).GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic);
        messageField?.SetValue(skipInstance, message);

        return skipInstance;
    }

    private static CliInvocation TryCreateInvocation(string arguments)
    {
        var separatorIndex = arguments.IndexOf(CommandSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return CliInvocation.Unavailable;
        }

        var optionSegment = arguments[..separatorIndex];
        if (!optionSegment.AsSpan().TrimStart().StartsWith("run", StringComparison.Ordinal))
        {
            return CliInvocation.Unavailable;
        }

        var commandSegment = arguments[(separatorIndex + CommandSeparator.Length)..].Trim();
        if (commandSegment.Length == 0)
        {
            return CliInvocation.Unavailable;
        }

        var tokens = SplitArguments(optionSegment);
        if (tokens.Count == 0 || !string.Equals(tokens[0], "run", StringComparison.Ordinal))
        {
            return CliInvocation.Unavailable;
        }

        var projectPath = GetOptionValue(tokens, "--project");
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return CliInvocation.Unavailable;
        }

        var configuration = GetOptionValue(tokens, "--configuration") ?? BuildConfiguration;
        var assemblyPath = ResolveAssemblyPath(projectPath!, configuration);
        if (assemblyPath.Length == 0 || !File.Exists(assemblyPath))
        {
            return CliInvocation.Unavailable;
        }

        var processArguments = BuildProcessArguments(assemblyPath, commandSegment);
        var cliArguments = SplitArguments(commandSegment).ToArray();
        return new CliInvocation(true, processArguments, cliArguments);
    }

    private static string BuildProcessArguments(string assemblyPath, string commandSegment)
    {
        if (string.IsNullOrEmpty(commandSegment))
        {
            return QuoteIfNeeded(assemblyPath);
        }

        var builder = new StringBuilder(assemblyPath.Length + commandSegment.Length + 1);
        builder.Append(QuoteIfNeeded(assemblyPath));
        builder.Append(' ');
        builder.Append(commandSegment);
        return builder.ToString();
    }

    private static string ResolveAssemblyPath(string projectPath, string configuration)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return string.Empty;
        }

        var trimmed = projectPath.Trim('\"');
        var projectFullPath = Path.GetFullPath(trimmed);
        var projectDirectory = Path.GetDirectoryName(projectFullPath);
        if (projectDirectory is null)
        {
            return string.Empty;
        }

        var assemblyFile = Path.GetFileNameWithoutExtension(projectFullPath) + ".dll";
        return Path.Combine(projectDirectory, "bin", configuration, TargetFramework, assemblyFile);
    }

    private static List<string> SplitArguments(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\"':
                    inQuotes = !inQuotes;
                    continue;
                case ' ':
                case '\t':
                    if (inQuotes)
                    {
                        current.Append(ch);
                    }
                    else if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                default:
                    current.Append(ch);
                    continue;
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string? GetOptionValue(IReadOnlyList<string> tokens, string option)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i], option, StringComparison.Ordinal) && i + 1 < tokens.Count)
            {
                return tokens[i + 1];
            }
        }

        return null;
    }

    private static string QuoteIfNeeded(string path)
    {
        return path.IndexOf(' ') >= 0 ? $"\"{path}\"" : path;
    }

    private static async Task<CommandResult> RunInProcessAsync(string workingDirectory, IReadOnlyList<string> cliArguments)
    {
        await InProcessLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            var originalOut = Console.Out;
            var originalError = Console.Error;

            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            try
            {
                Directory.SetCurrentDirectory(workingDirectory);
                Console.SetOut(stdout);
                Console.SetError(stderr);

                var exitCode = await InvokeEntryPointAsync(cliArguments).ConfigureAwait(false);
                return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                Directory.SetCurrentDirectory(originalDirectory);
            }
        }
        finally
        {
            InProcessLock.Release();
        }
    }

    private static async Task<int> InvokeEntryPointAsync(IReadOnlyList<string> cliArguments)
    {
        var assembly = typeof(Osm.Cli.ProfileSnapshotDebugFormatter).Assembly;
        var entryPoint = assembly.EntryPoint
            ?? throw new InvalidOperationException("CLI assembly does not define an entry point.");

        object? invocationResult;
        if (entryPoint.GetParameters().Length == 0)
        {
            invocationResult = entryPoint.Invoke(null, Array.Empty<object?>());
        }
        else
        {
            var args = cliArguments is string[] array
                ? array
                : cliArguments.ToArray();
            invocationResult = entryPoint.Invoke(null, new object?[] { args });
        }

        return await UnwrapExitCodeAsync(invocationResult).ConfigureAwait(false);
    }

    private static async Task<int> UnwrapExitCodeAsync(object? invocationResult)
    {
        switch (invocationResult)
        {
            case null:
                return 0;
            case int exitCode:
                return exitCode;
            case Task<int> exitCodeTask:
                return await exitCodeTask.ConfigureAwait(false);
            case Task task:
                await task.ConfigureAwait(false);
                return 0;
            default:
                return 0;
        }
    }

    private static string EnsureNoBuildAndConfiguration(string arguments)
    {
        var updated = AppendOption(arguments, "--no-build");
        updated = AppendConfiguration(updated);
        return updated;
    }

    private static string AppendOption(string arguments, string option)
    {
        return arguments.Contains(option, StringComparison.Ordinal)
            ? arguments
            : string.Create(arguments.Length + option.Length + 1, (arguments, option), static (span, state) =>
            {
                var (source, opt) = state;
                source.CopyTo(span);
                span[source.Length] = ' ';
                opt.CopyTo(span[(source.Length + 1)..]);
            });
    }

    private static string AppendConfiguration(string arguments)
    {
        if (arguments.Contains("--configuration", StringComparison.Ordinal))
        {
            return arguments;
        }

        var configurationOption = $"--configuration {BuildConfiguration}";
        return string.Create(arguments.Length + configurationOption.Length + 1, (arguments, configurationOption), static (span, state) =>
        {
            var (source, option) = state;
            source.CopyTo(span);
            span[source.Length] = ' ';
            option.CopyTo(span[(source.Length + 1)..]);
        });
    }

    private static async Task<CommandResult> RunExternalAsync(ProcessStartInfo startInfo)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask).ConfigureAwait(false);

        return new CommandResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private readonly record struct CliInvocation(bool Success, string ProcessArguments, string[] CliArguments)
    {
        public static CliInvocation Unavailable { get; } = new(false, string.Empty, Array.Empty<string>());
    }

    internal readonly record struct CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string FormatFailureMessage(int expectedExitCode)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Expected exit code {expectedExitCode}, but received {ExitCode}.");

            if (!string.IsNullOrWhiteSpace(StandardOutput))
            {
                builder.AppendLine("stdout:");
                builder.AppendLine(StandardOutput.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(StandardError))
            {
                builder.AppendLine("stderr:");
                builder.AppendLine(StandardError.TrimEnd());
            }

            return builder.ToString();
        }
    }
}
