namespace Zava.SalesIntelligence.Agents.Contracts;

// One ranked opportunity the seller should focus on, with its prepared actions. The Ranker
// orders these; the approval gate carries them (ApprovalRequest.Recommendations) and the
// companion renders them. Meddpicc is the AE persona's deeper read, filled by a separate scorer
// pass; it is null in the SDR (BANT) persona and never affects the ranking or the actions.
public record RankedRecommendation(
    int Rank,
    OpportunityAssessment Assessment,
    IReadOnlyList<PreparedAction> Actions,
    MeddpiccScorecard? Meddpicc = null);
