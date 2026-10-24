using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DbChangeLite;

public static partial class Predict
{
    internal static IEnumerable<OpenItem> Open(NamedEnvironment environment, Reading reading, Measurement measured, Func<string, string> command, Source to, Source.Ref? onto, string? intent, string tool)
    {
        var prove = "dbchange-lite prove --env " + environment.Name + " --to " + to + (onto is null ? " --onto none" : " --onto " + onto) + " --rows <the rows file written from this answer>";
        foreach (var (precondition, result) in measured.Unanswered)
        {
            yield return result switch
            {
                Measured.Failed when precondition.ReadsNew => new OpenItem(environment.Name, 4, precondition.Spelled + " reads a column the plan adds, so no read-only query settles it", Next.Run(prove, "the developer's agent")),
                Measured.Refused r => new OpenItem(environment.Name, 2, precondition.Spelled + " was not read: SQL Server refuses this login the SELECT", Next.Ask("a DBA", "SQL Server says: " + r.Message + " Grant that SELECT to the reader of " + environment.Name + ", or run predict as a login that holds it.")),
                Measured.TimedOut { Locked: true } t => new OpenItem(environment.Name, 2, precondition.Spelled + " was not read: the query waited " + t.Seconds.ToString(CultureInfo.InvariantCulture) + " seconds for a lock another session holds (Msg 1222)", Next.Run(command(environment.Name), "the developer's agent, when no session holds a lock on " + precondition.Quoted)),
                Measured.TimedOut t => new OpenItem(environment.Name, 2, precondition.Spelled + " was not read: the query ran past " + t.Seconds.ToString(CultureInfo.InvariantCulture) + " seconds", Next.Ask("a DBA", "Run this read-only query on " + environment.Name + " without a timeout and say whether it answers 1: " + precondition.Query)),
                Measured.Failed f => new OpenItem(environment.Name, 2, precondition.Spelled + " was not read: " + (f.Number == 0 ? f.Message : "Msg " + f.Number.ToString(CultureInfo.InvariantCulture) + ", " + f.Message), Next.Ask("a DBA", "Why does the query for " + precondition.Spelled + " fail on " + environment.Name + "?")),
                _ => new OpenItem(environment.Name, 2, precondition.Spelled + " was not read", Next.Run(command(environment.Name), "the developer's agent")),
            };
        }

        foreach (var answer in measured.Preconditions.Where(p => p.Violated == true))
        {
            yield return new OpenItem(environment.Name, 3, answer.Kind + " on " + answer.Table + Columns(answer.Columns) + " holds violating rows" + (answer.Count is { } n ? " (" + n.ToString(CultureInfo.InvariantCulture) + ")" : ""),
                Next.Ask("the developer", "What becomes of the rows of " + answer.Table + " that violate " + answer.Kind + Columns(answer.Columns) + "?"));
        }

        foreach (var group in reading.Unsettled.GroupBy(u => (u.Why, u.Table)))
        {
            var why = group.Key.Why switch
            {
                "rebuild" => "DacFx rebuilds " + group.Key.Table + ": every row is copied under a lock, and its time at production size is unknown",
                "identity" => "the rebuild of " + group.Key.Table + " changes IDENTITY, which reseeds and keeps gaps",
                "re-trust" => "a constraint of " + group.Key.Table + " is validated WITH CHECK CHECK CONSTRAINT against rows already in place",
                "authored" => "a deploy script writes " + group.Key.Table + ", which the plan alters, against its new shape",
                "cdc" => group.Key.Table + " is tracked by Change Data Capture, and the plan touches it",
                _ => "a statement outside the list of statements that move no rows, at line " + group.First().Statement.Line.ToString(CultureInfo.InvariantCulture) + ": " + Clip(group.First().Statement.Text),
            };
            yield return new OpenItem(environment.Name, 4, why, Next.Run(prove, "the developer's agent"));
            if (group.Key.Why == "cdc")
            {
                yield return new OpenItem(environment.Name, 4, group.Key.Table + " is tracked by Change Data Capture", Next.Ask("the owner of the CDC consumer", "Which capture instance of " + group.Key.Table + " is re-enabled after the deploy, and when?"));
            }
        }

        foreach (var change in reading.ConsumerChanges.DistinctBy(c => (c.Table, c.What)))
        {
            yield return new OpenItem(environment.Name, 0, change.Table + ": " + change.What + ", which its OutSystems consumers see", Next.Ask("the lead", "Which release deploys " + change.Table + "'s change, and when is its external entity refreshed in Integration Studio and each consumer republished?"));
        }

        foreach (var operation in reading.Unexplained)
        {
            var plans = "the plan " + operation.Kind.ToLowerInvariant() + "s " + operation.Type + " " + operation.Name;
            yield return operation.Cause switch
            {
                null => new OpenItem(environment.Name, 0, plans + ", which the change does not make", Next.Ask("a lead", "Is " + operation.Name + " drift on " + environment.Name + ", or part of this change?")),
                "outside-intent" => new OpenItem(environment.Name, 0, plans + ", which " + intent + " does not produce", Next.Ask("the developer", "Is " + operation.Name + " part of " + intent + ", or a second operation the change makes?")),
                var cause => new OpenItem(environment.Name, 0, plans + "; cause " + cause, Next.Ask("the developer", Causes.Known(tool).FirstOrDefault(c => c.Name == cause)?.Action ?? "Read the cause " + cause + " in causes.json.")),
            };
        }

        if (reading.MissingIntent is { } missing)
        {
            yield return new OpenItem(environment.Name, 0, "the plan for " + environment.Name + " makes none of the operations " + missing + " produces",
                Next.Ask("the developer", "Is " + missing + " already on " + environment.Name + ", or does the profile's DropObjectsNotInSource=False keep a drop out of the plan?"));
        }
    }

    private static IEnumerable<Finding> Findings(NamedEnvironment environment, Reading reading, Measurement measured, string? intent)
    {
        var shape = intent is null ? "the operation" : intent;
        foreach (var check in measured.Checks.Where(c => c.Fires == true))
        {
            yield return Finding.Error("check.fires", check.Table, "On " + environment.Name + " the table has rows, so the BlockOnPossibleDataLoss check DacFx writes for it stops the publish.", "Ship " + shape + " in two releases, as its skill states.");
        }

        foreach (var precondition in measured.Preconditions.Where(p => p.Violated == true))
        {
            yield return Finding.Error("precondition.violated", precondition.Table, "On " + environment.Name + " rows violate " + precondition.Kind + Columns(precondition.Columns) + ", so SQL Server refuses the change at the deploy.", "Correct or remove the violating rows in a pre-deploy step the developer approves, then run predict again.");
        }

        foreach (var unsettled in reading.Unsettled.DistinctBy(u => (u.Why, u.Table)))
        {
            yield return Finding.Warning("predict.not-predictable", unsettled.Table ?? environment.Name, "No read-only query settles " + unsettled.Why + (unsettled.Table is null ? "" : " on " + unsettled.Table) + " at line " + unsettled.Statement.Line.ToString(CultureInfo.InvariantCulture) + ".", "Run dbchange-lite prove as the open item says.");
        }

        foreach (var change in reading.ConsumerChanges.DistinctBy(c => (c.Table, c.What)))
        {
            yield return Finding.Warning("contract.change", change.Table, change.What + " on " + change.Table + ", which each environment's external entity must be refreshed for.", "Ask the lead as the open item says.");
        }

        foreach (var operation in reading.Unexplained)
        {
            yield return Finding.Warning("plan.unexplained", operation.Name, "The plan for " + environment.Name + " " + operation.Kind.ToLowerInvariant() + "s " + operation.Type + " " + operation.Name
                + (operation.Cause == "outside-intent" ? ", which " + intent + " does not produce." : ", which the change does not make" + (operation.Cause is null ? "." : "; cause " + operation.Cause + ".")), "Ask the person the open item names.");
        }

        if (reading.MissingIntent is { } missing)
        {
            yield return Finding.Warning("plan.intent-absent", environment.Name, "The plan for " + environment.Name + " makes none of the operations " + missing + " produces.", "Ask the developer as the open item says.");
        }

        foreach (var (table, column, line) in reading.SmartDefaults)
        {
            yield return Finding.Warning("smart-default", table, "GenerateSmartDefaults adds " + Names.Quote(column) + " to " + table + " with a default the project does not declare, so every existing row is stamped with a value nobody chose (line "
                + line.ToString(CultureInfo.InvariantCulture) + ").", "Declare the default in the project, or confirm the stamped value with the lead.");
        }

        foreach (var name in reading.Script.Unresolved)
        {
            yield return Finding.Note("script.unresolved-variable", name, "The deploy script reads $(" + name + "), which neither the script, the profile nor the environments file values.");
        }
    }

    private static EnvironmentAnswer Worst(NamedEnvironment environment, Built change, Built at, PublishProfile profile, Run run, List<Finding> findings)
    {
        var reading = Read(environment, profile, environment.Name, at.PackagePath, Model.Load(at.PackagePath), change, null, null, run, new HashSet<string>());
        var preconditions = reading.Preconditions.Where(p => !p.OnNewObject || p.ReadsNew).ToList();
        var clauses = preconditions.Select(p => p.Kind == "TableEmpty" ? p.Quoted + " has rows" : p.Spelled + " is violated").ToList();
        var outcome = clauses.Count == 0 ? "applies" : "blocks if " + string.Join(", or ", clauses);
        findings.Add(Finding.Warning("environment.assumed", environment.Name, environment.Name + " is never read, so the worst case is assumed: " + outcome + ".", "Run the beforePromoting queries on " + environment.Name + " under an identity with its rights just before promoting."));
        return EnvironmentAnswer.Worst(environment.Name, outcome, at.Describe, preconditions.Where(p => !p.ReadsNew).Select(p => p.Query).ToList());
    }

    public static string Confidence(IReadOnlyList<EnvironmentAnswer> rows) => string.Join("; ", rows.Select(r => r.Assumed is not null ? r.Env + " assumed"
        : r.Level switch { 4 => "Proven on " + r.Env, 3 => "Measured on " + r.Env, 2 => "Planned on " + r.Env, _ => r.Env + " not read" })) + ".";

    private static string Columns(IReadOnlyList<string> columns) => columns.Count == 0 ? "" : " (" + string.Join(", ", columns.Select(Names.Quote)) + ")";

    private static string Clip(string text) => text.Length <= 200 ? text : text[..197] + "...";
}
