using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Zava.SalesIntelligence.Agents.Contracts;

namespace Zava.SalesIntelligence.Workflow;

// Runs an agent for a typed result: the response-format schema constrains the model,
// then we deserialize with our own options so an out-of-range archetype on a refusal
// falls back rather than crashing. Confidence is authoritative for those cases.
static class StructuredAgent
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        Converters = { new ArchetypeJsonConverter(), new JsonStringEnumConverter() },
    };

    public static async Task<T> RunAsync<T>(AIAgent agent, string input, CancellationToken cancellationToken)
    {
        var options = new AgentRunOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema<T>() };
        var response = await agent.RunAsync(input, options: options, cancellationToken: cancellationToken);
        return JsonSerializer.Deserialize<T>(response.Text, Options)!;
    }
}
