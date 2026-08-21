using Zava.SalesIntelligence.Agents.Contracts;

namespace Zava.SalesIntelligence.Workflow;

// Messages that flow between executors. Concrete record types so the workflow routes
// them unambiguously by type across the superstep barriers.
internal sealed record ContextBatch(IReadOnlyList<AccountContext> Contexts);

internal sealed record AssessmentBatch(IReadOnlyList<AssessedOpportunity> Assessments);

internal sealed record RankedBatch(IReadOnlyList<AssessedOpportunity> Ranked);
