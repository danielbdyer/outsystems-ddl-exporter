using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Evidence;

internal sealed class EvidenceCacheWriter
{
    private readonly IFileSystem _fileSystem;

    public EvidenceCacheWriter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<EvidenceCacheManifest> WriteAsync(
        string cacheDirectory,
        string manifestFileName,
        string manifestVersion,
        string key,
        string command,
        DateTimeOffset creationTimestamp,
        EvidenceCacheModuleSelection moduleSelection,
        IReadOnlyDictionary<string, string?> metadata,
        IReadOnlyCollection<EvidenceArtifactDescriptor> descriptors,
        CancellationToken cancellationToken)
    {
        _fileSystem.Directory.CreateDirectory(cacheDirectory);

        var expiresAtUtc = EvidenceCacheTtlPolicy.DetermineExpiry(creationTimestamp, metadata);
        var artifacts = new List<EvidenceCacheArtifact>(descriptors.Count);

        foreach (var descriptor in descriptors.OrderBy(static d => d.Type))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = BuildArtifactFileName(descriptor);
            var destinationPath = _fileSystem.Path.Combine(cacheDirectory, relativePath);
            await CopyFileAsync(descriptor.SourcePath, destinationPath, cancellationToken).ConfigureAwait(false);

            artifacts.Add(new EvidenceCacheArtifact(
                descriptor.Type,
                descriptor.SourcePath,
                relativePath,
                descriptor.Hash,
                descriptor.Length));
        }

        var manifest = new EvidenceCacheManifest(
            manifestVersion,
            key,
            command,
            creationTimestamp,
            creationTimestamp,
            expiresAtUtc,
            moduleSelection,
            metadata,
            artifacts);

        var manifestPath = _fileSystem.Path.Combine(cacheDirectory, manifestFileName);
        await WriteManifestAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);

        return manifest;
    }

    private string BuildArtifactFileName(EvidenceArtifactDescriptor descriptor)
    {
        var prefix = descriptor.Type switch
        {
            EvidenceArtifactType.Model => "model",
            EvidenceArtifactType.Profile => "profile",
            EvidenceArtifactType.Dmm => "dmm",
            EvidenceArtifactType.Configuration => "config",
            _ => "artifact",
        };

        var extension = descriptor.Extension.StartsWith(".", StringComparison.Ordinal)
            ? descriptor.Extension
            : $".{descriptor.Extension}";

        return string.Concat(prefix, extension);
    }

    private async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var directory = _fileSystem.Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        var temporaryPath = string.Concat(destinationPath, ".tmp-", Guid.NewGuid().ToString("N"));

        try
        {
            await using var source = _fileSystem.File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var destination = _fileSystem.File.Open(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_fileSystem.File.Exists(temporaryPath))
            {
                _fileSystem.File.Delete(temporaryPath);
            }

            throw;
        }

        try
        {
            if (_fileSystem.File.Exists(destinationPath))
            {
                _fileSystem.File.Delete(destinationPath);
            }

            _fileSystem.File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            if (_fileSystem.File.Exists(temporaryPath))
            {
                _fileSystem.File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private async Task WriteManifestAsync(
        string manifestPath,
        EvidenceCacheManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = _fileSystem.File.Open(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions { WriteIndented = true }, cancellationToken)
            .ConfigureAwait(false);
    }
}
