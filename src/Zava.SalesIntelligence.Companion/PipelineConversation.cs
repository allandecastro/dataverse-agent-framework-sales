using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Zava.SalesIntelligence.Companion;

// What the seller asked to do with the approval gate. None for a plain question.
public enum TurnAction
{
    None,
    Approve,     // approve the listed proposals
    ApproveAll,
    RejectAll,
}

// One turn of the conversation. The model always returns this shape (structured output), so a
// command is never missed the way an optional tool call can be: the answer to show, plus what
// the seller asked to do and which proposals it concerns.
public sealed record ConversationTurn(string Answer, TurnAction Action, IReadOnlyList<int> Proposals);

// The squad's spokesperson: answers the seller's questions grounded only in the current run,
// and reports any approve/reject the seller asked for. The companion applies that through the
// existing approval gate - the conversation itself never writes, never reaches Dataverse, and
// never touches anything outside this run's proposals.
public sealed class PipelineConversation(AIAgent agent)
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ConversationTurn> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var options = new AgentRunOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema<ConversationTurn>() };
        var response = await agent.RunAsync(question, options: options, cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(response.Text)
            ? new ConversationTurn("I could not answer that.", TurnAction.None, [])
            : JsonSerializer.Deserialize<ConversationTurn>(response.Text, Options)
              ?? new ConversationTurn("I could not answer that.", TurnAction.None, []);
    }
}
