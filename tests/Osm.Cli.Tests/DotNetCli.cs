using System;
using System.Reflection;

namespace Osm.Cli.Tests;

internal static class DotNetCli
{
    private static readonly string BuildConfiguration =
        typeof(DotNetCli).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
        ?? "Debug";

    public static string EnsureNoBuildAndConfiguration(string arguments)
    {
        const string commandSeparator = " -- ";
        var separatorIndex = arguments.IndexOf(commandSeparator, StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            var optionSegment = arguments[..separatorIndex];
            var commandSegment = arguments[separatorIndex..];
            optionSegment = AppendOption(optionSegment, "--no-build");
            optionSegment = AppendConfiguration(optionSegment);
            return optionSegment + commandSegment;
        }

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
}
