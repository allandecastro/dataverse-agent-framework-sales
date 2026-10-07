using Zava.SalesIntelligence.Agents.Contracts;

namespace Zava.SalesIntelligence.Workflow;

// Messages that flow between executors. Concrete record types so the workflow routes
// them unambiguously by type across the superstep barriers.

// An assessment carried with its account context, so the challenge round can
// re-examine the evidence, and its last-activity date for ranking. Meddpicc is the AE persona's
// MEDDPICC scorecard, merged onto the judgment by the fan-in Join after the Scorer branch
// finishes; it stays null in SDR, where there is no Scorer and no Join.
internal sealed record Judged(
    AccountContext Context,
    OpportunityAssessment Assessment,
    DateOnly? LastActivity,
    MeddpiccScorecard? Meddpicc = null)
{
    // An action resting on a Missing axis is what triggers a challenge to the Thinker.
    public bool HasMissingAxis
    {
        get
        {
            var bant = Assessment.Bant;
            return new[] { bant.Budget, bant.Authority, bant.Need, bant.Timing }
                .Any(criterion => criterion.Status == BantStatus.Missing);
        }
    }
}

internal sealed record ContextBatch(IReadOnlyList<AccountContext> Contexts);

internal sealed record JudgedBatch(IReadOnlyList<Judged> Items);

// AE persona: one account's MEDDPICC scorecard, carried with its account name so the fan-in Join
// can merge it back onto that account's BANT judgment. The Scorer branch produces a batch of
// these in parallel with the Thinker's JudgedBatch; the two meet at the barrier.
internal sealed record Scored(string AccountName, MeddpiccScorecard Scorecard);

internal sealed record ScoredBatch(IReadOnlyList<Scored> Items);

internal sealed record RankedBatch(IReadOnlyList<Judged> Items);

// Operator -> Thinker: the ranked opportunities, sent for a challenge when at least one
// action would rest on a Missing axis. The conditional edge, capped at one round trip.
internal sealed record ChallengeBatch(IReadOnlyList<Judged> Items);

// The ranked opportunities ready for action preparation, either straight from the
// Operator (no challenge) or after the Thinker re-examined them.
internal sealed record ChallengedBatch(IReadOnlyList<Judged> Items);
