using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Emission;

namespace Osm.Pipeline.Orchestration;

public interface ISsdtSqlValidator
{
    Task<SsdtSqlValidationSummary> ValidateAsync(
        string outputDirectory,
        IReadOnlyList<TableManifestEntry> tables,
        CancellationToken cancellationToken = default);
}

public sealed class SsdtSqlValidator : ISsdtSqlValidator
{
    private readonly IFileSystem _fileSystem;

    public SsdtSqlValidator()
        : this(new FileSystem())
    {
    }

    public SsdtSqlValidator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<SsdtSqlValidationSummary> ValidateAsync(
        string outputDirectory,
        IReadOnlyList<TableManifestEntry> tables,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        var issues = new List<SsdtSqlValidationIssue>();
        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = table.TableFile.Replace('/', _fileSystem.Path.DirectorySeparatorChar);
            var fullPath = _fileSystem.Path.Combine(outputDirectory, relativePath);

            try
            {
                if (!_fileSystem.File.Exists(fullPath))
                {
                    issues.Add(SsdtSqlValidationIssue.Create(
                        table.TableFile,
                        new[]
                        {
                            SsdtSqlValidationError.Create(
                                number: -1,
                                state: 0,
                                severity: 16,
                                line: 0,
                                column: 0,
                                message: $"SQL artifact missing: {table.TableFile}")
                        }));
                    continue;
                }

                var script = await _fileSystem.File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                using var reader = new StringReader(script);
                var parser = new TSql150Parser(initialQuotedIdentifiers: true);
                parser.Parse(reader, out var errors);
                if (errors is { Count: > 0 })
                {
                    var errorRecords = errors
                        .Select(error => SsdtSqlValidationError.Create(
                            error.Number,
                            state: 0,
                            severity: 0,
                            error.Line,
                            error.Column,
                            NormalizeMessage(error.Message)))
                        .ToArray();
                    issues.Add(SsdtSqlValidationIssue.Create(table.TableFile, errorRecords));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(SsdtSqlValidationIssue.Create(
                    table.TableFile,
                    new[]
                    {
                        SsdtSqlValidationError.Create(
                            number: -1,
                            state: 0,
                            severity: 16,
                            line: 0,
                            column: 0,
                            message: $"Unable to read SQL artifact: {ex.Message}")
                    }));
            }
        }

        return SsdtSqlValidationSummary.Create(tables.Count, issues);
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unspecified parser error.";
        }

        return message.Trim();
    }
}
