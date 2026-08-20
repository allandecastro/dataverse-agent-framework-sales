using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Zava.SalesIntelligence.Agents;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Dataverse;

// Phase 2: Watcher retrieves, Thinker judges. Called by hand, one account at a time.
// Set ZAVA_RUNS above 1 to run the determinism check: the same data must produce the
// same archetype every run. Confidence of InsufficientSignal is authoritative and
// overrides the archetype (the enum has no "none" value), matching the Ranker.
string dataverseUrl = RequireEnv("ZAVA_DATAVERSE_URL");
string openAiEndpoint = RequireEnv("ZAVA_OPENAI_ENDPOINT");
string reasoningDeployment = RequireEnv("ZAVA_OPENAI_REASONING_DEPLOYMENT");
int runs = int.TryParse(Environment.GetEnvironmentVariable("ZAVA_RUNS"), out var parsed) ? parsed : 1;
var credential = new AzureCliCredential();

using var dataverse = DataverseClientFactory.Create(dataverseUrl, credential);
var queries = new WatcherQueries(dataverse);
var watcher = new WatcherService(queries);

AIAgent thinker = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
    .GetChatClient(reasoningDeployment)
    .AsAIAgent(name: "Thinker", instructions: Thinker.BuildSystemPrompt());

// The response schema constrains the model; deserialization is tolerant of an
// out-of-range archetype (Confidence governs those), so refusals never crash.
var runOptions = new AgentRunOptions
{
    ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<OpportunityAssessment>(),
};
// ArchetypeJsonConverter is listed first so it wins for Archetype; the string enum
// converter handles Confidence and BantStatus, which the model returns as names.
var deserializeOptions = new JsonSerializerOptions(JsonSerializerOptions.Web)
{
    Converters = { new ArchetypeJsonConverter(), new JsonStringEnumConverter() },
};

// The Watcher is deterministic, so build every context once and reuse it across runs.
var accounts = (await queries.GetAccountsAsync(CancellationToken.None))
    .OrderBy(a => a.AccountNumber)
    .ToList();

var contexts = new List<(AccountRecord Account, string Json, HashSet<Guid> KnownIds)>();
foreach (var account in accounts)
{
    var context = await watcher.BuildContextAsync(account, CancellationToken.None);
    contexts.Add((account, JsonSerializer.Serialize(context), CollectIds(context)));
}

Console.WriteLine($"Thinker over {accounts.Count} accounts, {runs} run(s), on {reasoningDeployment}");
Console.WriteLine();

var observed = accounts.ToDictionary(a => a.AccountNumber!, _ => new List<string>());
var inventedTotal = 0;

for (var run = 1; run <= runs; run++)
{
    foreach (var (account, json, knownIds) in contexts)
    {
        var response = await thinker.RunAsync(json, options: runOptions, cancellationToken: CancellationToken.None);
        var assessment = JsonSerializer.Deserialize<OpportunityAssessment>(response.Text, deserializeOptions)!;

        var effective = assessment.Confidence == Confidence.InsufficientSignal
            ? "InsufficientSignal"
            : assessment.Archetype.ToString();
        observed[account.AccountNumber!].Add(effective);

        var invented = new[] { assessment.Bant.Budget, assessment.Bant.Authority, assessment.Bant.Need, assessment.Bant.Timing }
            .SelectMany(criterion => criterion.Evidence)
            .Count(evidence => !knownIds.Contains(evidence.RecordId));
        inventedTotal += invented;

        if (runs == 1)
        {
            Console.WriteLine($"{account.AccountNumber}  {account.Name}");
            Console.WriteLine($"  archetype={effective}  bant={assessment.Bant.Score}  confidence={assessment.Confidence}  invented={invented}");
            Console.WriteLine($"  rationale: {assessment.Rationale}");
        }
    }

    if (runs > 1)
    {
        Console.WriteLine($"run {run} done");
    }
}

if (runs > 1)
{
    Console.WriteLine();
    var unstable = 0;
    foreach (var account in accounts)
    {
        var values = observed[account.AccountNumber!];
        var distinct = values.Distinct().ToList();
        var stable = distinct.Count == 1;
        if (!stable) unstable++;
        var marker = stable ? "stable  " : "UNSTABLE";
        Console.WriteLine($"  {marker}  {account.AccountNumber}  {string.Join(" | ", distinct)}");
    }

    Console.WriteLine();
    Console.WriteLine($"Gate: {(unstable == 0 ? "STABLE" : $"{unstable} account(s) UNSTABLE")} across {runs} runs. Invented ids total: {inventedTotal}.");
}

static HashSet<Guid> CollectIds(AccountContext context)
{
    var ids = new HashSet<Guid> { context.AccountId };
    foreach (var opportunity in context.Opportunities) ids.Add(opportunity.OpportunityId);
    foreach (var activity in context.Activities) ids.Add(activity.ActivityId);
    foreach (var note in context.Notes) ids.Add(note.NoteId);
    foreach (var contact in context.Contacts) ids.Add(contact.ContactId);
    return ids;
}

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
