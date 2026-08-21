using Zava.SalesIntelligence.Agents.Contracts;

namespace Zava.SalesIntelligence.Workflow;

// The Ranker is plain code, not an agent. Ordering is deterministic so the demo
// produces the same result every run. It drops assessments the Thinker refused to
// conclude, then sorts on BANT score, then amount, then days to close, then days
// silent. "today" is passed in rather than read from the clock so the ordering is
// pure and testable.
public static class Ranker
{
    public static IReadOnlyList<AssessedOpportunity> Rank(
        IEnumerable<AssessedOpportunity> assessments, DateOnly today) =>
        assessments
            .Where(item => item.Assessment.Confidence != Confidence.InsufficientSignal)
            .OrderByDescending(item => item.Assessment.Bant.Score)
            .ThenByDescending(item => item.Assessment.Amount)
            .ThenBy(item => DaysToClose(item.Assessment.CloseDate, today))
            .ThenByDescending(item => DaysSilent(item.LastActivityDate, today))
            .ToList();

    // No close date sorts last; a deal with no known activity is treated as the most silent.
    static int DaysToClose(DateOnly? closeDate, DateOnly today) =>
        closeDate is { } date ? date.DayNumber - today.DayNumber : int.MaxValue;

    static int DaysSilent(DateOnly? lastActivity, DateOnly today) =>
        lastActivity is { } date ? today.DayNumber - date.DayNumber : int.MaxValue;
}
