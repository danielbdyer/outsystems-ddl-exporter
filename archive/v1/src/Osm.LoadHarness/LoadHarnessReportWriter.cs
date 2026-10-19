using System;
using System.IO;
using System.IO.Abstractions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.LoadHarness;

public sealed class LoadHarnessReportWriter
{
    private readonly IFileSystem _fileSystem;

    public LoadHarnessReportWriter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task WriteAsync(LoadHarnessReport report, string path, CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A report output path must be provided.", nameof(path));
        }

        var directory = _fileSystem.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !_fileSystem.Directory.Exists(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        await using var stream = _fileSystem.File.Create(path);
        await JsonSerializer.SerializeAsync(
                stream,
                report,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
