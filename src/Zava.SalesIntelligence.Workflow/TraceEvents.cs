using Microsoft.Agents.AI.Workflows;

namespace Zava.SalesIntelligence.Workflow;

// A live trace of what the squad is doing, surfaced through the workflow event stream so the
// companion can show it in the activity log. Two kinds, kept as distinct types so the
// deterministic and the model-driven traces never share a code path (see CLAUDE.md).

// Deterministic work: a Dataverse query and its result count, a stage starting, the Ranker's
// order. Same input, same line, every run.
public sealed class AgentProgressEvent(string agent, string message) : WorkflowEvent(message)
{
    public string Agent { get; } = agent;
    public string Message { get; } = message;
}

// A model's output: an archetype call with its rationale, a re-examination, a proposed
// action. Varies between runs - it is the intelligence, not the plumbing.
public sealed class AgentThoughtEvent(string agent, string text) : WorkflowEvent(text)
{
    public string Agent { get; } = agent;
    public string Text { get; } = text;
}
