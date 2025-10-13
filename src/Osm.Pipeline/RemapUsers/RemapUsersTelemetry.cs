using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Osm.Pipeline.RemapUsers;

public sealed class RemapUsersTelemetry : IRemapUsersTelemetry
{
    private readonly ILogger<RemapUsersTelemetry> _logger;
    private readonly RemapUsersLogLevel _logLevel;
    private readonly List<RemapUsersTelemetryEntry> _entries = new();
    private readonly Dictionary<string, DateTimeOffset> _stepStartTimes = new(StringComparer.OrdinalIgnoreCase);

    public RemapUsersTelemetry(ILogger<RemapUsersTelemetry> logger, RemapUsersLogLevel logLevel)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logLevel = logLevel;
    }

    public IReadOnlyList<RemapUsersTelemetryEntry> Entries => new ReadOnlyCollection<RemapUsersTelemetryEntry>(_entries);

    public void StepStarted(string stepName)
    {
        var timestamp = DateTimeOffset.UtcNow;
        _stepStartTimes[stepName] = timestamp;
        AppendEntry(timestamp, stepName, "started", null, "Step started.", null, null, null);

        if (_logLevel == RemapUsersLogLevel.Trace)
        {
            _logger.LogTrace("remap-users step {Step} started", stepName);
        }
    }

    public void StepCompleted(string stepName)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var duration = _stepStartTimes.TryGetValue(stepName, out var start)
            ? timestamp - start
            : (TimeSpan?)null;
        AppendEntry(timestamp, stepName, "completed", duration, "Step completed.", null, null, null);

        switch (_logLevel)
        {
            case RemapUsersLogLevel.Trace:
                if (duration.HasValue)
                {
                    _logger.LogTrace("remap-users step {Step} completed in {Duration}.", stepName, duration.Value);
                }
                else
                {
                    _logger.LogTrace("remap-users step {Step} completed.", stepName);
                }
                break;
            case RemapUsersLogLevel.Debug:
                if (duration.HasValue)
                {
                    _logger.LogDebug("remap-users step {Step} completed in {Duration}.", stepName, duration.Value);
                }
                else
                {
                    _logger.LogDebug("remap-users step {Step} completed.", stepName);
                }
                break;
            default:
                _logger.LogInformation("remap-users step {Step} completed.", stepName);
                break;
        }
    }

    public void Info(string stepName, string message, IReadOnlyDictionary<string, string?>? metadata = null)
    {
        AppendEntry(DateTimeOffset.UtcNow, stepName, "info", null, message, metadata, null, null);
        var formatted = FormatLogMessage(stepName, message, metadata);
        switch (_logLevel)
        {
            case RemapUsersLogLevel.Trace:
                _logger.LogTrace("{Message}", formatted);
                break;
            case RemapUsersLogLevel.Debug:
                _logger.LogDebug("{Message}", formatted);
                break;
            default:
                _logger.LogInformation("{Message}", formatted);
                break;
        }
    }

    public void Warning(string stepName, string message, IReadOnlyDictionary<string, string?>? metadata = null)
    {
        AppendEntry(DateTimeOffset.UtcNow, stepName, "warning", null, message, metadata, null, null);
        _logger.LogWarning("{Message}", FormatLogMessage(stepName, message, metadata));
    }

    public void Error(string stepName, string message, Exception exception, IReadOnlyDictionary<string, string?>? metadata = null)
    {
        AppendEntry(
            DateTimeOffset.UtcNow,
            stepName,
            "error",
            null,
            message,
            metadata,
            exception.GetType().FullName,
            exception.Message);
        _logger.LogError(exception, "{Message}", FormatLogMessage(stepName, message, metadata));
    }

    private void AppendEntry(
        DateTimeOffset timestamp,
        string step,
        string eventType,
        TimeSpan? duration,
        string message,
        IReadOnlyDictionary<string, string?>? metadata,
        string? exceptionType,
        string? exceptionMessage)
    {
        _entries.Add(new RemapUsersTelemetryEntry(
            timestamp,
            step,
            eventType,
            duration,
            message,
            metadata is null ? null : new ReadOnlyDictionary<string, string?>(metadata.ToDictionary(pair => pair.Key, pair => pair.Value)),
            exceptionType,
            exceptionMessage));
    }

    private static string FormatLogMessage(string stepName, string message, IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return $"[{stepName}] {message}";
        }

        var kv = string.Join(", ", metadata.Select(pair => $"{pair.Key}={pair.Value}"));
        return $"[{stepName}] {message} ({kv})";
    }
}
