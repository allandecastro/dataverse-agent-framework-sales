namespace Zava.SalesIntelligence.Agents.Contracts;

// The Operator's output. A wrapper around the prepared actions, because structured
// output needs an object at the top level rather than a bare array.
public record OperatorPlan(IReadOnlyList<PreparedAction> Actions);
