namespace Zava.SalesIntelligence.Agents.Contracts;

// The approval gate contracts. The workflow pauses at a RequestPort that emits an
// ApprovalRequest; the seller returns an ApprovalDecision; the approved actions are
// then written. Nothing reaches Dataverse without a decision. The gate is iterative: the
// seller can approve a batch and keep the rest for another round (Next), so approving some
// actions never discards the others.

// One approvable unit: a prepared action with a number for the seller to reference.
public record ActionProposal(int Number, string OpportunityName, PreparedAction Action);

public record ApprovalRequest(
    IReadOnlyList<RankedRecommendation> Recommendations,
    IReadOnlyList<ActionProposal> Proposals);

// Approved is written now. Next, when set, re-opens the gate with the proposals the seller kept
// for another round; null ends the run. So a partial approval loops with the remainder rather
// than throwing it away.
public record ApprovalDecision(IReadOnlyList<ActionProposal> Approved, ApprovalRequest? Next = null);

// One line of the write report. When a write created a record, the entry carries its logical
// name and id so the companion can link straight to it; a plain message (a skip, a phase-4 stub,
// nothing written) leaves both null.
public record WriteResult(string Message, string? EntityLogicalName = null, Guid? RecordId = null);

// What the run did once the approved actions were executed.
public record WriteReport(IReadOnlyList<WriteResult> Lines);
