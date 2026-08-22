using Microsoft.Agents.AI;

namespace Zava.SalesIntelligence.Companion;

// The squad's spokesperson: answers the seller's questions about the current run, grounded
// only in what the squad produced (the analysis is baked into the agent's instructions).
//
// This is a deliberately thin, bounded layer - the contract of what the conversation may do:
//   - it explains, filters, and summarises the run's assessments and prepared actions;
//   - it cites the evidence the Thinker cited;
//   - it does NOT judge, re-score, query Dataverse, or write anything.
//
// Read-only for now. TODO (step 2): bounded commands - approve/reject a proposal through the
// existing approval gate (never a new write surface), which would give the conversation the
// same authority as the approval buttons and nothing more.
public sealed class PipelineConversation(AIAgent agent)
{
    public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var response = await agent.RunAsync(question, cancellationToken: cancellationToken);
        return response.Text;
    }
}
