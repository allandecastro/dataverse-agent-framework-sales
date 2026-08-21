namespace Zava.SalesIntelligence.Agents.Contracts;

// The approval gate contracts. The workflow pauses at a RequestPort that emits an
// ApprovalRequest; the seller returns an ApprovalDecision; the approved actions are
// then written. Nothing reaches Dataverse without a decision.

// One approvable unit: a prepared action with a number for the seller to reference.
public record ActionProposal(int Number, string OpportunityName, PreparedAction Action);

public record ApprovalRequest(
    IReadOnlyList<RankedRecommendation> Recommendations,
    IReadOnlyList<ActionProposal> Proposals);

public record ApprovalDecision(IReadOnlyList<ActionProposal> Approved);

// What the run did once the approved actions were executed.
public record WriteReport(IReadOnlyList<string> Lines);
