namespace Zava.SalesIntelligence.Agents.Contracts;

// The workflow's final output: the ranked opportunities the seller should focus on,
// each with its prepared actions. The companion renders this; here it is the console
// and DevUI result.
public record SalesBrief(IReadOnlyList<RankedRecommendation> Recommendations);

public record RankedRecommendation(
    int Rank,
    OpportunityAssessment Assessment,
    IReadOnlyList<PreparedAction> Actions);
