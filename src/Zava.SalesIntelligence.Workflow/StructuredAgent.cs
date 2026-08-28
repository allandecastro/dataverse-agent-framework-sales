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

        // The schema constrains the model, but a truncated, empty, or malformed response still
        // happens - and it is usually transient. Retry once before giving up, so a single flaky
        // completion does not fault the whole run. The caller catches the final failure and
        // degrades that one account rather than crashing the stage.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await agent.RunAsync(input, options: options, cancellationToken: cancellationToken);
            if (!string.IsNullOrWhiteSpace(response.Text))
            {
                try
                {
                    if (JsonSerializer.Deserialize<T>(response.Text, Options) is { } result)
                    {
                        return result;
                    }
                }
                catch (JsonException)
                {
                    // Malformed JSON despite the schema - fall through to retry, then throw.
                }
            }
        }

        throw new InvalidOperationException($"The {agent.Name} agent did not return a usable {typeof(T).Name} after 2 attempts.");
    }
}
