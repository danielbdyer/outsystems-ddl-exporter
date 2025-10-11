using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening;

public sealed record PolicyDecision<TDecision>(
    string RuleId,
    TDecision Outcome,
    ImmutableArray<PolicyEvidenceLink> Evidence,
    ImmutableArray<string> PreRemediationSql)
{
    public static PolicyDecision<TDecision> Create(
        string ruleId,
        TDecision outcome,
        IEnumerable<PolicyEvidenceLink>? evidence = null,
        IEnumerable<string>? preRemediationSql = null)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new ArgumentException("Rule identifier must be provided.", nameof(ruleId));
        }

        if (outcome is null)
        {
            throw new ArgumentNullException(nameof(outcome));
        }

        var evidenceArray = evidence is null
            ? ImmutableArray<PolicyEvidenceLink>.Empty
            : evidence.ToImmutableArray();

        var remediationArray = preRemediationSql is null
            ? ImmutableArray<string>.Empty
            : preRemediationSql.ToImmutableArray();

        return new PolicyDecision<TDecision>(ruleId, outcome, evidenceArray, remediationArray);
    }
}
