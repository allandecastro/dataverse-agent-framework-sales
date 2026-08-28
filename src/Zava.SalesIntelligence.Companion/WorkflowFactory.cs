using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;
using Zava.SalesIntelligence.Workflow;

namespace Zava.SalesIntelligence.Companion;

// The companion's view of the squad. The squad workflow itself is built by the shared
// SquadFactory (identical wiring to the console host); this factory adds the companion-only
// pieces: the grounded Q&A agent and the record deep links.
public sealed class WorkflowFactory
{
    // The squad workflow plus the Dataverse clients it holds; the caller disposes the clients
    // when the run ends (they are IDisposable and outlive Build). Shared with the host. The persona
    // (SDR / AE) chooses the qualification lens; the UI toggle passes it in.
    public BuiltSquad Build(Persona persona = Persona.Sdr) => SquadFactory.Build(persona);

    // A deep link to a written record's form in the model-driven app, so the seller can confirm
    // what the Operator just wrote. Config, not a secret; null when the base URL is unset.
    public string? RecordUrl(string entityLogicalName, Guid recordId)
    {
        var dataverseUrl = Environment.GetEnvironmentVariable("ZAVA_DATAVERSE_URL");
        return string.IsNullOrWhiteSpace(dataverseUrl)
            ? null
            : $"{dataverseUrl.TrimEnd('/')}/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId}";
    }

    // A grounded question-answer agent over one run's results. The analysis is baked into the
    // instructions, so the agent can only speak to what the squad produced - it has no tools,
    // no Dataverse, and cannot re-judge or write. Runs on the reasoning deployment for the
    // finer questions.
    public PipelineConversation BuildConversation(string analysisJson)
    {
        var openAiEndpoint = Require("ZAVA_OPENAI_ENDPOINT");
        var reasoningDeployment = Require("ZAVA_OPENAI_REASONING_DEPLOYMENT");
        var openAiClient = new AzureOpenAIClient(new Uri(openAiEndpoint), new AzureCliCredential());

        var instructions = ConversationInstructions + "\n\nThis week's analysis (JSON):\n" + analysisJson;
        AIAgent assistant = openAiClient.GetChatClient(reasoningDeployment).AsAIAgent(name: "Assistant", instructions: instructions);
        return new PipelineConversation(assistant);
    }

    const string ConversationInstructions =
        """
        You are the sales squad's spokesperson. Answer the seller's questions about this
        week's pipeline analysis, using ONLY the analysis provided below.

        - Ground every answer in that data. Name the account, and cite evidence in human
          terms - what the record is and when, or a short quote from it (for example, "the
          note from 20 August"). Never show a record id or a raw identifier.
        - If the seller asks about something not in the analysis, say you do not have it. Never
          guess or invent a score, an action, a date, or a contact detail.
        - You do not judge or re-score, and you do not write to Dataverse yourself - but the
          companion does: when the seller approves an action, it IS written to the CRM through the
          approval gate under the writer identity. So an action the seller has approved is created.
          Confirm that plainly when asked ("yes, the meeting with Dana was created"); never tell the
          seller that nothing was created, or that you have no confirmation, after they approved it.
          An action that has NOT been approved is still only a proposal - say so for those.
          When the seller asks to approve or reject, report it in Action and Proposals.
        - When the seller asks to approve or reject, ACT - set the Action, do not merely
          describe it in the Answer. A plain question is Action None.
          "Approve the Litware task", "approve Litware" -> Action Approve, Proposals = Litware's
          proposal number(s). "Approve everything" -> Action ApproveAll. "Reject", "reject all"
          -> Action RejectAll.
        - Match a named account or opportunity to its proposals even if the seller uses a loose
          word - they may call an appointment a "task", or name only the account. If that
          account has a single proposal, approve that one.
        - Put what you say to the seller in Answer: plain prose, meant to be read aloud and
          shown in a small chat bubble. No Markdown - no asterisks, bold, headings, or tables.
          Keep it short.
        """;

    static string Require(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
}
