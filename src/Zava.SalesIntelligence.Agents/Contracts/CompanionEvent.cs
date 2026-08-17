namespace Zava.SalesIntelligence.Agents.Contracts;

// ProgressEvent is emitted by the workflow and is deterministic. AgentMessageEvent
// comes from a model and varies between runs. The companion renders both as dialogue
// but they are not equally reliable and must not share a code path.
public abstract record CompanionEvent(string AgentName, DateTimeOffset At);

public record ProgressEvent(string AgentName, DateTimeOffset At,
    string Message, int? Current, int? Total) : CompanionEvent(AgentName, At);

public record AgentMessageEvent(string AgentName, DateTimeOffset At,
    string Text, IReadOnlyList<Evidence>? Citations) : CompanionEvent(AgentName, At);

public record AssessmentReadyEvent(string AgentName, DateTimeOffset At,
    OpportunityAssessment Assessment) : CompanionEvent(AgentName, At);

public record ApprovalRequestEvent(string AgentName, DateTimeOffset At,
    PreparedAction Action, string RequestId) : CompanionEvent(AgentName, At);
