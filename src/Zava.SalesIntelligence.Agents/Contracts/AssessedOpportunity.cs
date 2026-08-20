namespace Zava.SalesIntelligence.Agents.Contracts;

// An assessment paired with the one ranking signal the Thinker's output does not
// carry: the date of the most recent activity, used for days silent. Produced after
// the Thinker, consumed by the Ranker.
public record AssessedOpportunity(
    OpportunityAssessment Assessment,
    DateOnly? LastActivityDate);
