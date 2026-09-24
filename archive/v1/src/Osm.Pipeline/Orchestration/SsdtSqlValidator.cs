using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<SsdtSqlValidator> _logger;

    public SsdtSqlValidator()
        : this(new FileSystem(), NullLogger<SsdtSqlValidator>.Instance)
    {
    }

    public SsdtSqlValidator(IFileSystem fileSystem)
        : this(fileSystem, NullLogger<SsdtSqlValidator>.Instance)
    {
    }

    public SsdtSqlValidator(IFileSystem fileSystem, ILogger<SsdtSqlValidator> logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        _logger.LogInformation("Starting SQL validation for {TableCount} table(s) in {OutputDirectory}", tables.Count, outputDirectory);

        var issues = new List<SsdtSqlValidationIssue>();
        var validatedCount = 0;
        var errorFileCount = 0;

        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            validatedCount++;
            var relativePath = table.TableFile.Replace('/', _fileSystem.Path.DirectorySeparatorChar);
            var fullPath = _fileSystem.Path.Combine(outputDirectory, relativePath);

            _logger.LogDebug("Validating [{Index}/{Total}]: {TableFile}", validatedCount, tables.Count, table.TableFile);

            try
            {
                if (!_fileSystem.File.Exists(fullPath))
                {
                    _logger.LogError("SQL file missing: {TableFile} (expected at {FullPath})", table.TableFile, fullPath);
                    errorFileCount++;
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
                    errorFileCount++;
                    _logger.LogWarning(
                        "T-SQL parse error(s) in {TableFile}: {ErrorCount} error(s) found",
                        table.TableFile,
                        errors.Count);

                    foreach (var error in errors)
                    {
                        _logger.LogError(
                            "  [{TableFile}:{Line}:{Column}] Error {ErrorNumber}: {Message}",
                            table.TableFile,
                            error.Line,
                            error.Column,
                            error.Number,
                            error.Message);
                    }

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
                else
                {
                    _logger.LogDebug("  ✓ {TableFile} validated successfully", table.TableFile);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errorFileCount++;
                _logger.LogError(ex, "Unable to read SQL artifact: {TableFile}", table.TableFile);
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

        var summary = SsdtSqlValidationSummary.Create(tables.Count, issues);
        _logger.LogInformation(
            "SQL validation completed: {ValidatedFiles}/{TotalFiles} validated, {ErrorFiles} file(s) with errors, {TotalErrors} total error(s)",
            validatedCount,
            tables.Count,
            errorFileCount,
            summary.ErrorCount);

        return summary;
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
