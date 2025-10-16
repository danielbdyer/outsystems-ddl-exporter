using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Evidence;

internal sealed class EvidenceDescriptorCollector
{
    private readonly IFileSystem _fileSystem;

    public EvidenceDescriptorCollector(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<Result<IReadOnlyList<EvidenceArtifactDescriptor>>> CollectAsync(
        EvidenceCacheRequest request,
        CancellationToken cancellationToken)
    {
        var descriptors = new List<EvidenceArtifactDescriptor>(capacity: 4);

        var modelResult = await DescribeAsync(EvidenceArtifactType.Model, request.ModelPath, cancellationToken)
            .ConfigureAwait(false);
        if (modelResult.IsFailure)
        {
            return Result<IReadOnlyList<EvidenceArtifactDescriptor>>.Failure(modelResult.Errors);
        }

        if (modelResult.Value is not null)
        {
            descriptors.Add(modelResult.Value);
        }

        var profileResult = await DescribeAsync(EvidenceArtifactType.Profile, request.ProfilePath, cancellationToken)
            .ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<IReadOnlyList<EvidenceArtifactDescriptor>>.Failure(profileResult.Errors);
        }

        if (profileResult.Value is not null)
        {
            descriptors.Add(profileResult.Value);
        }

        var dmmResult = await DescribeAsync(EvidenceArtifactType.Dmm, request.DmmPath, cancellationToken)
            .ConfigureAwait(false);
        if (dmmResult.IsFailure)
        {
            return Result<IReadOnlyList<EvidenceArtifactDescriptor>>.Failure(dmmResult.Errors);
        }

        if (dmmResult.Value is not null)
        {
            descriptors.Add(dmmResult.Value);
        }

        var configResult = await DescribeAsync(EvidenceArtifactType.Configuration, request.ConfigPath, cancellationToken)
            .ConfigureAwait(false);
        if (configResult.IsFailure)
        {
            return Result<IReadOnlyList<EvidenceArtifactDescriptor>>.Failure(configResult.Errors);
        }

        if (configResult.Value is not null)
        {
            descriptors.Add(configResult.Value);
        }

        return descriptors;
    }

    private async Task<Result<EvidenceArtifactDescriptor?>> DescribeAsync(
        EvidenceArtifactType type,
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<EvidenceArtifactDescriptor?>.Success(null);
        }

        var trimmed = path.Trim();
        if (!_fileSystem.File.Exists(trimmed))
        {
            return ValidationError.Create(
                GetMissingCode(type),
                $"{GetArtifactLabel(type)} '{trimmed}' was not found.");
        }

        await using var stream = _fileSystem.File.Open(trimmed, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var length = stream.Length;
        var extension = _fileSystem.Path.GetExtension(trimmed);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = type switch
            {
                EvidenceArtifactType.Dmm => ".sql",
                _ => ".json",
            };
        }

        return Result<EvidenceArtifactDescriptor?>.Success(
            new EvidenceArtifactDescriptor(type, trimmed, hash, length, extension));
    }

    private static string GetMissingCode(EvidenceArtifactType type)
    {
        return type switch
        {
            EvidenceArtifactType.Model => "cache.model.notFound",
            EvidenceArtifactType.Profile => "cache.profile.notFound",
            EvidenceArtifactType.Dmm => "cache.dmm.notFound",
            EvidenceArtifactType.Configuration => "cache.config.notFound",
            _ => "cache.artifact.notFound",
        };
    }

    private static string GetArtifactLabel(EvidenceArtifactType type)
    {
        return type switch
        {
            EvidenceArtifactType.Model => "Model",
            EvidenceArtifactType.Profile => "Profiling snapshot",
            EvidenceArtifactType.Dmm => "DMM script",
            EvidenceArtifactType.Configuration => "Configuration",
            _ => "Artifact",
        };
    }
}
