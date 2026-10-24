using System;
using System.Collections.Generic;
using System.Globalization;
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
    IReadOnlyList<ContractChange> ConsumerChanges, IReadOnlyList<PlanOperationAnswer> Unexplained, IReadOnlyList<(string Table, string Column, int Line)> SmartDefaults, string? MissingIntent);

public static partial class Predict
{
    public static Answer Run(Invocation invocation, Run run)
    {
        var environments = EnvironmentsFile.Read(run.Root);
        var asked = environments.InDeployOrder(invocation.Values["--env"].Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal));
        if (asked.Count == 0)
        {
            throw Stop.Of("bad-arguments", "env.empty", "--env", "--env names no environment.", "Give --env one or more names from " + EnvironmentsFile.Name + ", separated by commas.");
        }

        var ledger = ToolchainLedger.Read(run.Root);
        ledger.HoldWindow();
        var intent = invocation.Values.GetValueOrDefault("--intent");
        if (intent is not null)
        {
            NotPredictable.Unexplained([], intent, run.Tool);
        }

        var profiles = asked.ToDictionary(e => e.Name, e => PublishProfiles.Load(run.Root, e.Profile));
        var (to, onto) = Source.Change(invocation);
        var change = Ssdt.Package(to, onto, run);
        var ontoBuilt = onto is null ? null : Ssdt.Package(onto, null, run);
        var at = invocation.Values.TryGetValue("--at", out var atText) ? Ssdt.Package(Source.Parse(atText, "--at"), null, run) : ontoBuilt;
        if (at is null && asked.FirstOrDefault(e => e.NeverRead) is { } unread)
        {
            throw Stop.Of("bad-arguments", "at.required", unread.Name, unread.Name + " is never read, so its plan needs the release it was deployed from.", "Give --at dacpac:<the release's package> or --at ref:<its tag>.");
        }

        var counts = invocation.Switches.Contains("--counts");
        var cdc = CdcLedger.Read(run.Root);
        var rows = new List<EnvironmentAnswer>();
        var open = new List<OpenItem>();
        var findings = new List<Finding>(change.Warnings ?? []);
        string Command(string name) => "dbchange-lite predict --env " + name + " --to " + to + (onto is null ? " --onto none" : " --onto " + onto) + (intent is null ? "" : " --intent " + intent);
        foreach (var environment in asked)
        {
            run.Cancel.ThrowIfCancellationRequested();
            if (environment.NeverRead)
            {
                rows.Add(Worst(environment, change, at!, profiles[environment.Name], run, findings));
                continue;
            }

            Baseline baseline;
            try
            {
                baseline = Baselines.Read(environment, run);
            }
            catch (Stop stop)
            {
                rows.Add(EnvironmentAnswer.Unread(environment.Name, stop.Finding.Message));
                open.Add(new OpenItem(environment.Name, 1, "not read: " + stop.Finding.Message, Next.Run(Command(environment.Name), "a lead who reads " + environment.Name)));
                findings.Add(Finding.Warning("environment.not-read", environment.Name, stop.Finding.Message, stop.Finding.Remedy));
                continue;
            }

            using (baseline)
            {
                var reading = Read(environment, profiles[environment.Name], baseline.Database, baseline.ExtractPath, baseline.Model, change, ontoBuilt, intent, run,
                    cdc.Concat(baseline.CdcTracked).ToHashSet(StringComparer.OrdinalIgnoreCase));
                var measured = Measure(baseline.Session.Scalar, reading.Preconditions, counts);
                rows.Add(Row(environment, baseline, reading, measured));
                open.AddRange(Open(environment, reading, measured, Command, to, onto, intent, run.Tool));
                findings.AddRange(Findings(environment, reading, measured, intent));
            }
        }

        var (outcome, message, blocking) = Decide(rows, open.Count);
        var first = profiles[asked[0].Name];
        if (!ledger.Confirms(first.Hash))
        {
            findings.Add(Finding.Note("profile.not-verified", first.Path, "The publish profile's hash " + first.Hash + " has no confirmation row in " + Toolchain.Path + "."));
        }

        return Answer.Of("predict", Contract.Of(outcome), message, findings, run) with
        {
            BlockedBy = blocking?.BlockedBy,
            Provenance = new Provenance(change.Commit ?? change.Fingerprint, change.OntoCommit, change.Tree, first.Hash, ledger.Confirms(first.Hash), run.Stamp),
            Confidence = Confidence(rows),
            Environments = rows,
            Open = open,
        };
    }

    public static (string Outcome, string Message, EnvironmentAnswer? Blocking) Decide(IReadOnlyList<EnvironmentAnswer> rows, int open)
    {
        var blocking = rows.FirstOrDefault(r => r.Outcome == "blocks");
        var read = rows.Count(r => r.Level >= 2);
        var outcome = blocking is not null ? "blocked" : read == 0 && rows.All(r => r.NotRead is not null) ? "unreachable" : open > 0 ? "needs-input" : "done";
        var message = outcome switch
        {
            "blocked" => blocking!.Env + " blocks: " + (blocking.BlockedBy == "block-on-possible-data-loss"
                ? "BlockOnPossibleDataLoss stops the publish because " + (blocking.Checks?.FirstOrDefault(c => c.Fires == true)?.Table ?? "a table the change alters") + " has rows."
                : "SQL Server refuses the change because existing rows violate " + (blocking.Preconditions?.FirstOrDefault(p => p.Violated == true) is { } p ? p.Kind + " on " + p.Table + Columns(p.Columns) : "a constraint") + "."),
            "unreachable" => "No environment answered; each is an open item.",
            "needs-input" => "Nothing read blocks; " + open.ToString(CultureInfo.InvariantCulture) + " item" + (open == 1 ? " is" : "s are") + " open.",
            _ => read == 0 ? "No environment was read; " + string.Join("; ", rows.Select(r => r.Env + " " + r.Outcome)) + "." : "Every environment read applies, and nothing is open.",
        };
        return (outcome, message, blocking);
    }

    public static Reading Read(NamedEnvironment environment, PublishProfile profile, string database, string package, Model model, Built change, Built? onto, string? intent, Run run,
        IReadOnlySet<string> cdcTracked)
    {
        var changePackage = DacFx.Load(change.PackagePath);
        var plan = DacFx.Script(changePackage, DacFx.Load(package), database, profile.Options);
        plan.Write(run, environment.Name);
        var script = DeployScripts.Read(plan.Script, DacFx.Text(changePackage.PreDeploymentScript), DacFx.Text(changePackage.PostDeploymentScript), Baselines.SqlCmd(environment, profile, run));
        var shape = NotPredictable.Shape(script);
        var changeModel = Model.Load(change.PackagePath);
        var own = onto is null ? null : DacFx.Script(changePackage, DacFx.Load(onto.PackagePath), database, profile.Options).Operations.Select(o => o.Type + " " + o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexplained = plan.Operations.Where(o => own is not null && !own.Contains(o.Type + " " + o.Name) && !NotPredictable.IsHousekeeping(o.Name))
            .Select(o => new PlanOperationAnswer(o.Kind, o.Type, o.Name, Causes.Of(o, plan.Operations, changeModel, plan.Script)))
            .Concat((intent is null ? [] : NotPredictable.Unexplained(plan.Operations, intent, run.Tool)).Select(o => new PlanOperationAnswer(o.Kind, o.Type, o.Name, Causes.Of(o, plan.Operations, changeModel, plan.Script) ?? "outside-intent")))
            .DistinctBy(o => o.Kind + " " + o.Type + " " + o.Name)
            .ToList();
        return new Reading(profile, plan, script, Preconditions.Of(script, BlockOnPossibleDataLoss.Checks(script), model), shape, NotPredictable.Of(script, shape, model, cdcTracked),
            NotPredictable.ConsumerChanges(script, shape, model), unexplained, NotPredictable.SmartDefaults(script, changeModel),
            intent is not null && !NotPredictable.Produces(plan.Operations, intent, run.Tool) ? intent : null);
    }

    public static Measurement Measure(Func<string, Measured> scalar, IReadOnlyList<Precondition> preconditions, bool counts)
    {
        var checks = new List<CheckAnswer>();
        var answers = new List<PreconditionAnswer>();
        var unanswered = new List<(Precondition, Measured)>();
        foreach (var precondition in preconditions)
        {
            if (precondition.Kind == "TableEmpty")
            {
                var (fires, readBy, result) = RowPresence.Read(scalar, precondition.Schema, precondition.Table, counts ? precondition.CountQuery : precondition.Query);
                checks.Add(new CheckAnswer(precondition.Quoted, fires, readBy, precondition.Query, counts && result is Measured.Answered counted ? counted.Value : null));
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

            var measured = scalar(counts ? precondition.CountQuery : precondition.Query);
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
