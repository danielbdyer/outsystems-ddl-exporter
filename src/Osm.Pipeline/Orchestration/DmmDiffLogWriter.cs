using System.Text.Json;
using Osm.Dmm;

namespace Osm.Pipeline.Orchestration;

public sealed class DmmDiffLogWriter
{
    public async Task<string> WriteAsync(
        string outputPath,
        string modelPath,
        string profilePath,
        string dmmPath,
        DmmComparisonResult comparison,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Diff output path must be provided.", nameof(outputPath));
        }

        if (comparison is null)
        {
            throw new ArgumentNullException(nameof(comparison));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var log = new DmmDiffLog(
            comparison.IsMatch,
            modelPath,
            profilePath,
            dmmPath,
            DateTimeOffset.UtcNow,
            comparison.ModelDifferences.ToArray(),
            comparison.SsdtDifferences.ToArray());

        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(fullPath, json, cancellationToken).ConfigureAwait(false);
        return fullPath;
    }

    private sealed record DmmDiffLog(
        bool IsMatch,
        string ModelPath,
        string ProfilePath,
        string DmmPath,
        DateTimeOffset GeneratedAtUtc,
        IReadOnlyList<string> ModelDifferences,
        IReadOnlyList<string> SsdtDifferences);
}
