using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DbChangeLite;

public sealed record Measurement(IReadOnlyList<CheckAnswer> Checks, IReadOnlyList<PreconditionAnswer> Preconditions, IReadOnlyList<(Precondition Precondition, Measured Result)> Unanswered)
{
    public bool Fires => Checks.Any(c => c.Fires == true);

    public bool Violated => Preconditions.Any(p => p.Violated == true);

    public int Level => Unanswered.Count == 0 ? 3 : 2;

    public string Outcome => Fires || Violated ? "blocks" : Unanswered.Count > 0 ? "unknown" : "applies";

    public string? BlockedBy => Fires ? "block-on-possible-data-loss" : Violated ? "constraint-violation" : null;
}

public sealed record Reading(PublishProfile Profile, Plan Plan, DeployScript Script, IReadOnlyList<Precondition> Preconditions, PlanShape Shape, IReadOnlyList<Unsettled> Unsettled,
    IReadOnlyList<ContractChange> Contract, IReadOnlyList<PlanOperationAnswer> Unexplained, IReadOnlyList<(string Table, string Column, int Line)> SmartDefaults);

public static partial class Predict
{
    public static Answer Run(Invocation invocation, Run run)
    {
        var environments = EnvironmentsFile.Read(run.Root);
        var asked = environments.InDeployOrder(invocation.Values["--env"].Split(',').Select(n => n.Trim()).Where(n => n.Length > 0));
        if (asked.Count == 0)
        {
            throw Stop.Of("bad-arguments", "env.empty", "--env", "--env names no environment.", "Give --env one or more names from " + EnvironmentsFile.Name + ", separated by commas.");
        }

        var ledger = ToolchainLedger.Read(run.Root);
        ledger.HoldWindow();
        var to = Source.Parse(invocation.Values.GetValueOrDefault("--to") ?? "ref:HEAD", "--to");
        var ontoText = invocation.Values.GetValueOrDefault("--onto") ?? "ref:origin/main";
        var onto = ontoText == "none" ? null : Source.Parse(ontoText, "--onto") as Source.Ref
            ?? throw Stop.Of("bad-arguments", "onto.form", "--onto", "--onto is " + ontoText + ", and it takes ref:<git ref> or none.", "Give --onto as ref:<git ref>, or none.");
        var change = Ssdt.Package(to, onto, run);
        var ontoBuilt = onto is null ? null : Ssdt.Package(onto, null, run);
        var at = invocation.Values.TryGetValue("--at", out var atText) ? Ssdt.Package(Source.Parse(atText, "--at"), null, run) : ontoBuilt;
        var intent = invocation.Values.GetValueOrDefault("--intent");
        var counts = invocation.Switches.Contains("--counts");
        var cdc = CdcLedger.Read(run.Root);
        var rows = new List<EnvironmentAnswer>();
        var open = new List<OpenItem>();
        var findings = new List<Finding>();
        PublishProfile? first = null;
        string Command(string name) => "dbchange-lite predict --env " + name + " --to " + to + (onto is null ? " --onto none" : " --onto " + onto) + (intent is null ? "" : " --intent " + intent);
        foreach (var environment in asked)
        {
            if (environment.NeverRead)
            {
                if (at is null)
                {
                    throw Stop.Of("bad-arguments", "at.required", environment.Name, environment.Name + " is never read, so its plan needs the release it was deployed from.", "Give --at dacpac:<the release's package> or --at ref:<its tag>.");
                }

                var profile = PublishProfiles.Load(run.Root, environment.Profile);
                first ??= profile;
                rows.Add(Worst(environment, change, at, profile, run, findings));
                continue;
            }

            Baseline baseline;
            try
            {
                baseline = Baselines.Read(environment, run);
            }
            catch (Stop stop) when (stop.Outcome.Word == "unreachable" || stop.Finding.Code.StartsWith("environments.reference", StringComparison.Ordinal))
            {
                rows.Add(new EnvironmentAnswer(environment.Name, 1, null, "unknown", null, null, null, stop.Finding.Message, null, null, null, null, null));
                open.Add(new OpenItem(environment.Name, 1, "not read: " + stop.Finding.Message, Next.Run(Command(environment.Name), "a lead who reads " + environment.Name)));
                findings.Add(Finding.Warning("environment.not-read", environment.Name, stop.Finding.Message, stop.Finding.Remedy));
                continue;
            }

            using (baseline)
            {
                var reading = Read(environment, baseline, change, ontoBuilt, intent, run, cdc.Concat(baseline.CdcTracked).ToHashSet(StringComparer.OrdinalIgnoreCase));
                first ??= reading.Profile;
                var measured = Measure(baseline.Session, reading.Preconditions, counts);
                rows.Add(Row(environment, baseline, reading, measured));
                open.AddRange(Open(environment, reading, measured, Command, to, onto));
                findings.AddRange(Findings(environment, reading, measured, intent));
            }
        }

        var read = rows.Where(r => r.Level >= 2).ToList();
        var outcome = rows.Any(r => r.Outcome == "blocks") ? "blocked" : open.Count > 0 ? "needs-input" : read.Count == 0 && rows.Any(r => r.NotRead is not null) ? "unreachable" : "done";
        var blocking = rows.FirstOrDefault(r => r.Outcome == "blocks");
        var message = blocking is not null
            ? blocking.Env + " blocks: " + (blocking.BlockedBy == "block-on-possible-data-loss" ? "BlockOnPossibleDataLoss stops the publish because a table the change alters has rows." : "SQL Server refuses the change on existing rows.")
            : outcome == "needs-input" ? "Nothing read blocks; " + open.Count.ToString(CultureInfo.InvariantCulture) + " item" + (open.Count == 1 ? " is" : "s are") + " open."
            : outcome == "unreachable" ? "No environment answered."
            : read.Count == 0 ? "No environment was read; " + string.Join("; ", rows.Select(r => r.Env + " " + r.Outcome)) + "." : "Every environment read applies, and nothing is open.";
        if (first is not null && !ledger.Confirms(first.Hash))
        {
            findings.Add(Finding.Note("profile.not-verified", first.Path, "The publish profile's hash " + first.Hash + " has no confirmation row in " + Toolchain.Path + "."));
        }

        return Answer.Of("predict", Contract.Of(outcome), message, findings, run) with
        {
            BlockedBy = blocking?.BlockedBy,
            Provenance = new Provenance(change.Commit ?? change.Fingerprint, change.OntoCommit, change.Tree, first?.Hash, first is not null && ledger.Confirms(first.Hash), run.Stamp),
            Confidence = Confidence(rows),
            Environments = rows,
            Open = open,
        };
    }

    public static Reading Read(NamedEnvironment environment, Baseline baseline, Built change, Built? onto, string? intent, Run run, IReadOnlySet<string> cdcTracked)
    {
        var profile = PublishProfiles.Load(run.Root, environment.Profile);
        var changePackage = DacFx.Load(change.PackagePath);
        var plan = DacFx.Script(changePackage, DacFx.Load(baseline.ExtractPath), baseline.Database, profile.Options);
        File.WriteAllText(Path.Combine(run.Folder, environment.Name + ".report.xml"), plan.Report);
        File.WriteAllText(Path.Combine(run.Folder, environment.Name + ".script.sql"), plan.Script);
        var script = DeployScripts.Read(plan.Script, DacFx.Text(changePackage.PreDeploymentScript), DacFx.Text(changePackage.PostDeploymentScript), Baselines.SqlCmd(environment, profile, run));
        var checks = BlockOnPossibleDataLoss.Checks(script);
        var preconditions = Preconditions.Of(script, checks, baseline.Model);
        var shape = NotPredictable.Shape(script);
        var unsettled = NotPredictable.Of(script, shape, baseline.Model, cdcTracked);
        var contract = NotPredictable.Contract(script, shape, baseline.Model);
        var changeModel = Model.Load(change.PackagePath);
        var own = onto is null ? null : DacFx.Script(changePackage, DacFx.Load(onto.PackagePath), baseline.Database, profile.Options).Operations.Select(o => o.Type + " " + o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexplained = plan.Operations
            .Where(o => own is not null && !own.Contains(o.Type + " " + o.Name) && !o.Name.Contains(NotPredictable.RebuildPrefix, StringComparison.OrdinalIgnoreCase) && !o.Name.Contains("__RefactorLog", StringComparison.OrdinalIgnoreCase))
            .Concat(intent is null ? [] : NotPredictable.Unexplained(plan.Operations, intent, run.Tool))
            .DistinctBy(o => o.Pair + " " + o.Name)
            .Select(o => new PlanOperationAnswer(o.Kind, o.Type, o.Name, Causes.Of(o, plan.Operations, changeModel, plan.Script)))
            .ToList();
        return new Reading(profile, plan, script, preconditions, shape, unsettled, contract, unexplained, NotPredictable.SmartDefaults(script, changeModel));
    }

    public static Measurement Measure(Session session, IReadOnlyList<Precondition> preconditions, bool counts)
    {
        var checks = new List<CheckAnswer>();
        var answers = new List<PreconditionAnswer>();
        var unanswered = new List<(Precondition, Measured)>();
        foreach (var precondition in preconditions)
        {
            if (precondition.Kind == "TableEmpty")
            {
                var (fires, readBy, result) = DbChangeLite.Measure.RowPresence(session, precondition.Schema, precondition.Table, counts ? precondition.CountQuery : precondition.Query);
                checks.Add(new CheckAnswer(precondition.Quoted, fires, readBy, precondition.Query));
                if (fires is null)
                {
                    unanswered.Add((precondition, result));
                }

                continue;
            }

            if (precondition.OnNewObject)
            {
                answers.Add(new PreconditionAnswer(precondition.Kind, precondition.Quoted, precondition.Columns, precondition.ReadsNew ? null : false, counts && !precondition.ReadsNew ? 0 : null, precondition.Query));
                if (precondition.ReadsNew)
                {
                    unanswered.Add((precondition, new Measured.Failed(0, "it reads a column the plan adds")));
                }

                continue;
            }

            var measured = session.Scalar(counts ? precondition.CountQuery : precondition.Query);
            answers.Add(new PreconditionAnswer(precondition.Kind, precondition.Quoted, precondition.Columns, measured is Measured.Answered a ? a.Value > 0 : null, measured is Measured.Answered b && counts ? b.Value : null, precondition.Query));
            if (measured is not Measured.Answered)
            {
                unanswered.Add((precondition, measured));
            }
        }

        return new Measurement(checks, answers, unanswered);
    }

    private static EnvironmentAnswer Row(NamedEnvironment environment, Baseline baseline, Reading reading, Measurement measured) =>
        new(environment.Name, measured.Level, null, measured.Outcome, measured.BlockedBy, "extract " + baseline.SchemaFingerprint, baseline.Collation, null, measured.Checks, measured.Preconditions,
            new PlanAnswer(reading.Plan.Operations.Select(o => new PlanOperationAnswer(o.Kind, o.Type, o.Name, null)).ToList(), reading.Shape.Rebuilt.Order(StringComparer.Ordinal).ToList(),
                reading.Shape.Dropped.Order(StringComparer.Ordinal).ToList(), reading.Unexplained),
            reading.Unsettled.Select(u => new NotPredictableAnswer(u.Why, u.Statement.Part, u.Statement.Line, Clip(u.Statement.Text))).ToList(), null);

}
