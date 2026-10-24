using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DbChangeLite;

public static class Drift
{
    public static Answer Run(Invocation invocation, Run run)
    {
        var environments = EnvironmentsFile.Read(run.Root);
        var environment = environments.Named(invocation.Values["--env"]);
        if (environment.NeverRead)
        {
            throw Stop.Of("refused-by-name", "environment.never-read", environment.Name, environment.Name + " is marked neverRead in " + EnvironmentsFile.Name + ", so nothing reads it.",
                "Predict with --env " + environment.Name + " to see its worst case, or ask the person who runs the queries before promoting.");
        }

        var ledger = ToolchainLedger.Read(run.Root);
        ledger.HoldWindow();
        var profile = PublishProfiles.Load(run.Root, environment.Profile);
        var at = Ssdt.Package(Source.Parse(invocation.Values["--at"], "--at"), null, run);
        using var baseline = Baselines.Read(environment, run);
        var plan = DacFx.Script(DacFx.Load(at.PackagePath), DacFx.Load(baseline.ExtractPath), baseline.Database, profile.Options);
        File.WriteAllText(Path.Combine(run.Folder, environment.Name + ".report.xml"), plan.Report);
        File.WriteAllText(Path.Combine(run.Folder, environment.Name + ".script.sql"), plan.Script);
        var atModel = Model.Load(at.PackagePath);
        var findings = new List<Finding>();
        var operations = plan.Operations.Select(o => new PlanOperationAnswer(o.Kind, o.Type, o.Name, Causes.Of(o, plan.Operations, atModel, plan.Script))).ToList();
        foreach (var operation in operations)
        {
            var known = operation.Cause is null ? null : Causes.Known(run.Tool).FirstOrDefault(c => c.Name == operation.Cause);
            findings.Add(Finding.Warning("drift.differs", operation.Name, operation.Kind + " " + operation.Type + ": " + environment.Name + " differs from " + at.Describe + " at " + operation.Name
                + (known is null ? "." : "; cause " + known.Name + ": " + known.Why), known?.Action ?? "Correct the environment or the repository at the object, or record the cause in causes.json."));
        }

        var known2 = atModel.Objects.ToHashSet();
        findings.AddRange(baseline.Model.Objects.Where(o => !known2.Contains(o) && !plan.Operations.Any(p => p.Name.Equals(o.Name, StringComparison.OrdinalIgnoreCase))).Select(o => Finding.Note("drift.unmanaged", o.Name, o.Type + " " + o.Name + " exists only in " + environment.Name + ", and no plan touches it while DropObjectsNotInSource is off.")));
        if (!ledger.Confirms(profile.Hash))
        {
            findings.Add(Finding.Note("profile.not-verified", profile.Path, "The publish profile's hash " + profile.Hash + " has no confirmation row in " + Toolchain.Path + "."));
        }

        var differing = plan.Operations.Count;
        var outcome = Contract.Of(differing == 0 ? "in-sync" : "differs");
        var message = differing == 0 ? environment.Name + " holds what " + at.Describe + " holds."
            : environment.Name + " differs from " + at.Describe + " in " + differing.ToString(CultureInfo.InvariantCulture) + " operation" + (differing == 1 ? "" : "s") + ".";
        return Answer.Of("drift", outcome, message, findings, run) with
        {
            Provenance = new Provenance(at.Commit ?? at.Fingerprint, null, at.Tree, profile.Hash, ledger.Confirms(profile.Hash), run.Stamp),
            Environments = [new EnvironmentAnswer(environment.Name, operations.All(o => o.Cause is not null) ? 1 : 0, null, differing == 0 ? "in-sync" : "differs", null, at.Describe, baseline.Collation, null, null, null,
                new PlanAnswer(operations, [], [], []), null, null)],
        };
    }
}
