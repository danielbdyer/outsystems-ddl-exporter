using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

namespace DbChangeLite;

public static class Prove
{
    public static Answer Run(Invocation invocation, Run run)
    {
        var environments = EnvironmentsFile.Read(run.Root);
        var environment = environments.Named(invocation.Values["--env"]);
        var ledger = ToolchainLedger.Read(run.Root);
        ledger.HoldWindow();
        var local = LocalServer.ConnectionString();
        LocalServer.Refuse(local, environments);
        var rowsPath = Path.GetFullPath(invocation.Values["--rows"], run.WorkingDirectory);
        if (!File.Exists(rowsPath))
        {
            throw Stop.Of("bad-arguments", "rows.missing", invocation.Values["--rows"], "There is no rows file at " + invocation.Values["--rows"] + ".", "Write the rows the copy needs as INSERT batches separated by GO, and give --rows its path.");
        }

        var profile = PublishProfiles.Load(run.Root, environment.Profile);
        var (to, onto) = Source.Change(invocation);
        var change = Ssdt.Package(to, onto, run);
        var cdc = CdcLedger.Read(run.Root);
        string baselinePackage;
        string collation;
        Measurement? expected;
        Reading reading;
        if (environment.NeverRead)
        {
            var at = invocation.Values.TryGetValue("--at", out var atText) ? Ssdt.Package(Source.Parse(atText, "--at"), null, run)
                : throw Stop.Of("bad-arguments", "at.required", environment.Name, environment.Name + " is never read, so the copy needs the release it was deployed from.", "Give --at dacpac:<the release's package> or --at ref:<its tag>.");
            var model = Model.Load(at.PackagePath);
            baselinePackage = at.PackagePath;
            collation = model.Collation;
            expected = null;
            reading = Predict.Read(environment, profile, environment.Name, at.PackagePath, model, change, null, null, run, cdc);
        }
        else
        {
            using var baseline = Baselines.Read(environment, run);
            baselinePackage = baseline.ExtractPath;
            collation = baseline.Collation;
            reading = Predict.Read(environment, profile, baseline.Database, baseline.ExtractPath, baseline.Model, change, null, null, run, cdc.Concat(baseline.CdcTracked).ToHashSet(StringComparer.OrdinalIgnoreCase));
            expected = Predict.Measure(baseline.Session.Scalar, reading.Preconditions, false);
        }

        var copy = LocalServer.CopyName(run.At);
        using var master = Session.Open(local, run.Log, run.Cancel, "local server");
        LocalServer.Sweep(master, run.At);
        master.Execute("CREATE DATABASE " + Names.Quote(copy) + (collation.Length > 0 ? " COLLATE " + collation : ""));
        try
        {
            var seed = new DacDeployOptions { BlockOnPossibleDataLoss = false, IgnorePermissions = true, IgnoreRoleMembership = true, IgnoreUserSettingsObjects = true, IgnoreLoginSids = true, AllowIncompatiblePlatform = true, DropObjectsNotInSource = false, ExcludeObjectTypes = [ObjectType.Users, ObjectType.Logins, ObjectType.RoleMembership, ObjectType.Permissions, ObjectType.Credentials, ObjectType.DatabaseScopedCredentials] };
            var seeded = DacFx.Deploy(local, DacFx.Load(baselinePackage), copy, seed, run.Cancel);
            if (seeded.Outcome != "done")
            {
                throw Stop.Of("configuration-refused", "copy.baseline-failed", copy, "The environment's package did not deploy to the copy: " + string.Join(" | ", seeded.Messages.Take(3)), "Report the object the first message names to the maintainer of dbchange-lite, with this answer, so the copy's seed excludes its type.");
            }

            using (var copySession = Session.Open(new SqlConnectionStringBuilder(local) { InitialCatalog = copy }.ConnectionString, run.Log, run.Cancel, copy))
            {
                var number = 0;
                foreach (var batch in LocalServer.Batches(File.ReadAllText(rowsPath)))
                {
                    try
                    {
                        number++;
                        copySession.Execute(batch, "rows file batch " + number.ToString(CultureInfo.InvariantCulture) + ", " + batch.Length.ToString(CultureInfo.InvariantCulture) + " characters");
                    }
                    catch (SqlException e)
                    {
                        throw Stop.Of("needs-input", "rows.failed", invocation.Values["--rows"], "Batch " + number.ToString(CultureInfo.InvariantCulture) + " of " + invocation.Values["--rows"] + " failed on the copy: Msg " + e.Number.ToString(CultureInfo.InvariantCulture) + ", " + e.Message.ReplaceLineEndings(" "),
                            "Correct the batch and run prove again; a key the release's post-deploy script already wrote on the copy cannot be inserted again.");
                    }
                }

                var planted = Predict.Measure(copySession.Scalar, reading.Preconditions, false);
                var differences = Differences(expected, planted, reading.Preconditions, environment.Name);
                if (differences.Count > 0)
                {
                    throw new Stop(Contract.Of("needs-input"), Finding.Error("copy.rows-differ", copy, "The copy's rows answer otherwise than " + environment.Name + "'s: " + string.Join("; ", differences) + ".",
                        "Add or remove rows in " + invocation.Values["--rows"] + " until the copy answers as the environment does, then run prove again; an item not read on " + environment.Name + " needs predict as a login that reads it first."));
                }

                DacFx.Script(DacFx.Load(change.PackagePath), DacFx.Load(baselinePackage), copy, PublishProfiles.ForProof(profile.Options)).Write(run, copy);
                var verdict = DacFx.Deploy(local, DacFx.Load(change.PackagePath), copy, PublishProfiles.ForProof(profile.Options), run.Cancel);
                var proven = Predict.Measure(copySession.Scalar, reading.Preconditions, false);
                var findings = new List<Finding>(change.Warnings ?? []);
                findings.AddRange(verdict.Messages.Select(m => Finding.Note("prove.dacfx", copy, m)));
                findings.AddRange(planted.Preconditions.Where(p => p.Violated == true)
                    .Where(p => proven.Preconditions.Any(q => q.Kind == p.Kind && q.Table == p.Table && q.Columns.SequenceEqual(p.Columns) && q.Violated == false))
                    .Select(p => Finding.Warning("prove.rows-changed", p.Table, p.Kind + " on " + p.Table + " was violated on the planted copy and holds after the deploy: a deploy script changed the rows before DacFx validated the constraint, so the read-only prediction and the proof differ.",
                        "Read the deploy scripts for the statement that corrects the rows, and say so in the pull request.")));
                findings.Add(Finding.Note("prove.rows", invocation.Values["--rows"], "The copy was planted from " + invocation.Values["--rows"] + " " + Fingerprint.OfBytes(File.ReadAllBytes(rowsPath)) + "."));
                var message = verdict.Outcome == "done" ? "The deploy applied on a copy of " + environment.Name + " with the planted rows."
                    : "The deploy was stopped on a copy of " + environment.Name + ": " + (verdict.BlockedBy == "block-on-possible-data-loss"
                        ? "BlockOnPossibleDataLoss, because " + (planted.Checks.FirstOrDefault(c => c.Fires == true)?.Table ?? "a table the change alters") + " has rows."
                        : "SQL Server refused the change on the planted rows" + (planted.Preconditions.FirstOrDefault(p => p.Violated == true) is { } v ? ", which violate " + v.Kind + " on " + v.Table : "") + ".");
                var (version, edition, _) = master.ServerInfo();
                return Answer.Of("prove", Contract.Of(verdict.Outcome), message, findings, run) with
                {
                    BlockedBy = verdict.BlockedBy,
                    Provenance = new Provenance(change.Commit ?? change.Fingerprint, change.OntoCommit, change.Tree, profile.Hash, ledger.Confirms(profile.Hash), run.Stamp),
                    Confidence = (environment.NeverRead ? environment.Name + " assumed; its worst case deployed" : "Proven on " + environment.Name) + ", on " + copy + " (SQL Server " + version + " " + edition + ").",
                    Environments = [new EnvironmentAnswer(environment.Name, environment.NeverRead ? null : 4, environment.NeverRead ? "worst" : null, verdict.Outcome == "done" ? "applies" : "blocks", verdict.BlockedBy, copy, collation, null, planted.Checks, planted.Preconditions, null, null, null)],
                    Open = Predict.Open(environment, reading, new Measurement([], [], []), _ => "", to, onto, null, run.Tool).Where(o => o.Next.Person is not null).ToList(),
                };
            }
        }
        finally
        {
            LocalServer.Drop(master, copy);
        }
    }

    internal static IReadOnlyList<string> Differences(Measurement? expected, Measurement planted, IReadOnlyList<Precondition> preconditions, string environment)
    {
        var differences = new List<string>();
        foreach (var check in planted.Checks)
        {
            var want = expected is null ? true : expected.Checks.FirstOrDefault(c => c.Table == check.Table)?.Fires;
            if (want is not { } w)
            {
                differences.Add(check.Table + " was not read on " + environment);
            }
            else if (check.Fires != w)
            {
                differences.Add(check.Table + (w ? " has rows in the environment and none in the copy" : " is empty in the environment and has rows in the copy"));
            }
        }

        foreach (var precondition in planted.Preconditions)
        {
            var source = preconditions.First(p => p.Quoted == precondition.Table && p.Kind == precondition.Kind && p.Columns.SequenceEqual(precondition.Columns));
            if (source.ReadsNew)
            {
                continue;
            }

            var want = expected is null ? !source.OnNewObject : expected.Preconditions.FirstOrDefault(p => p.Kind == precondition.Kind && p.Table == precondition.Table && p.Columns.SequenceEqual(precondition.Columns))?.Violated;
            if (want is not { } w)
            {
                differences.Add(precondition.Kind + " on " + precondition.Table + " was not read on " + environment);
            }
            else if (precondition.Violated != w)
            {
                differences.Add(precondition.Kind + " on " + precondition.Table + (w ? " is violated in the environment and holds in the copy" : " holds in the environment and is violated in the copy"));
            }
        }

        return differences;
    }
}
