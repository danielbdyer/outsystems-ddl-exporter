using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening;

public static class ChangeRiskClassifier
{
    public static ChangeRisk ForNotNull(NullabilityDecision decision)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        if (!decision.MakeNotNull)
        {
            if (decision.Rationales.Contains(TighteningRationales.NullabilityOverride))
            {
                return ChangeRisk.Low("Configuration override keeps this attribute nullable.");
            }

            if (ContainsAny(decision.Rationales, TighteningRationales.ProfileMissing, TighteningRationales.NullBudgetEpsilon))
            {
                return ChangeRisk.High("Profiling evidence is missing or null budget constraints block tightening.");
            }

            if (ContainsAny(decision.Rationales, TighteningRationales.Mandatory, TighteningRationales.DefaultPresent, TighteningRationales.DataNoNulls, TighteningRationales.UniqueNoNulls, TighteningRationales.CompositeUniqueNoNulls))
            {
                return ChangeRisk.Moderate("Evidence suggests the column could be NOT NULL once policy blockers are addressed.");
            }

            return ChangeRisk.Moderate("Collect more evidence before enforcing NOT NULL.");
        }

        if (decision.RequiresRemediation)
        {
            return ChangeRisk.Moderate("Data remediation is required before enforcing NOT NULL.");
        }

        return ChangeRisk.Low("Policy determined the column is safe to enforce as NOT NULL.");
    }

    public static ChangeRisk ForForeignKey(
        ForeignKeyDecision decision,
        bool hasOrphan,
        bool ignoreRule,
        bool crossSchemaBlocked,
        bool crossCatalogBlocked)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        if (!decision.CreateConstraint)
        {
            if (hasOrphan)
            {
                return ChangeRisk.High("Profiling detected orphaned rows that block foreign key creation.");
            }

            if (ignoreRule)
            {
                return ChangeRisk.Moderate("Delete rule 'Ignore' prevents safe enforcement of the foreign key.");
            }

            if (crossSchemaBlocked || crossCatalogBlocked)
            {
                return ChangeRisk.Moderate("Cross-database boundaries block automatic foreign key creation.");
            }

            if (decision.Rationales.Contains(TighteningRationales.ProfileMissing))
            {
                return ChangeRisk.High("Missing foreign key evidence prevents safe constraint creation.");
            }

            return ChangeRisk.Moderate("Enable policy or provide evidence before enforcing the foreign key.");
        }

        return ChangeRisk.Low("Constraint creation is safe based on policy evaluation.");
    }

    public static ChangeRisk ForUniqueIndex(UniqueIndexDecisionStrategy.UniqueIndexAnalysis analysis)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (!analysis.Decision.EnforceUnique)
        {
            if (analysis.HasDuplicates)
            {
                return ChangeRisk.High("Profiling detected duplicate values in the candidate unique index.");
            }

            if (analysis.PolicyDisabled)
            {
                return ChangeRisk.Moderate("Policy configuration disabled unique index enforcement.");
            }

            if (!analysis.HasEvidence)
            {
                return ChangeRisk.Moderate("Missing profiling evidence prevents unique index enforcement.");
            }

            return ChangeRisk.Moderate("Review data quality before enforcing the unique index.");
        }

        if (analysis.Decision.RequiresRemediation)
        {
            return ChangeRisk.Moderate("Remediation is required before enforcing the unique index.");
        }

        if (!analysis.PhysicalReality)
        {
            return ChangeRisk.Low("Unique index enforcement is supported by profiling evidence.");
        }

        return ChangeRisk.Low("Unique index already enforced in the physical database.");
    }

    private static bool ContainsAny(ImmutableArray<string> rationales, params string[] candidates)
    {
        if (rationales.IsDefaultOrEmpty)
        {
            return false;
        }

        var candidateSet = new HashSet<string>(candidates, StringComparer.Ordinal);
        return rationales.Any(candidateSet.Contains);
    }
}
