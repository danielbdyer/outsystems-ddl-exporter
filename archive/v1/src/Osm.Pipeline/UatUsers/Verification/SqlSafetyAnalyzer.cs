using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.RegularExpressions;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Analyzes generated UPDATE scripts for required safety guards.
/// Uses pattern matching to verify presence of NULL guards, target sanity checks, and idempotence patterns.
/// </summary>
public sealed class SqlSafetyAnalyzer
{
    // Patterns to detect safety guards in SQL scripts
    private static readonly Regex NullGuardPattern = new(
        @"IF\s+NOT\s+EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+#UserRemap\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TargetSanityCheckPattern = new(
        @"(WHERE\s+u\.\w+\s+IS\s+NULL|Target\s+user\s+IDs\s+are\s+missing)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TempTablePattern = new(
        @"CREATE\s+TABLE\s+#(UserRemap|Changes)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TransactionPattern = new(
        @"(BEGIN\s+TRAN|BEGIN\s+TRANSACTION|COMMIT|ROLLBACK)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Analyzes a SQL script file for required safety guards.
    /// </summary>
    /// <param name="scriptPath">Path to the SQL script file (02_apply_user_remap.sql)</param>
    /// <returns>Verification result indicating which guards are present</returns>
    public SqlSafetyVerificationResult Analyze(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Script path must be provided.", nameof(scriptPath));
        }

        if (!File.Exists(scriptPath))
        {
            return SqlSafetyVerificationResult.Failure(
                hasNullGuards: false,
                hasTargetSanityCheck: false,
                hasIdempotenceGuard: false,
                missingGuards: ImmutableArray.Create("NULL guards", "Target sanity check", "Idempotence guard"),
                warnings: ImmutableArray.Create($"Script file not found: {scriptPath}"));
        }

        string scriptContent;
        try
        {
            scriptContent = File.ReadAllText(scriptPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SqlSafetyVerificationResult.Failure(
                hasNullGuards: false,
                hasTargetSanityCheck: false,
                hasIdempotenceGuard: false,
                missingGuards: ImmutableArray.Create("NULL guards", "Target sanity check", "Idempotence guard"),
                warnings: ImmutableArray.Create($"Failed to read script: {ex.Message}"));
        }

        // Check for required safety patterns
        var hasNullGuards = NullGuardPattern.IsMatch(scriptContent);
        var hasTargetSanityCheck = TargetSanityCheckPattern.IsMatch(scriptContent);
        var hasIdempotenceGuard = TempTablePattern.IsMatch(scriptContent);

        // Build list of missing guards
        var missingGuards = new List<string>();
        if (!hasNullGuards)
        {
            missingGuards.Add("NULL guards (IF NOT EXISTS check for #UserRemap)");
        }

        if (!hasTargetSanityCheck)
        {
            missingGuards.Add("Target sanity check (validation that target user IDs exist)");
        }

        if (!hasIdempotenceGuard)
        {
            missingGuards.Add("Idempotence guard (temp table usage)");
        }

        // Build warnings
        var warnings = new List<string>();
        if (!TransactionPattern.IsMatch(scriptContent))
        {
            warnings.Add("No explicit transaction detected - consider wrapping updates in BEGIN TRAN/COMMIT");
        }

        var isValid = missingGuards.Count == 0;

        if (isValid)
        {
            return SqlSafetyVerificationResult.Success();
        }

        return SqlSafetyVerificationResult.Failure(
            hasNullGuards,
            hasTargetSanityCheck,
            hasIdempotenceGuard,
            missingGuards.ToImmutableArray(),
            warnings.ToImmutableArray());
    }
}
