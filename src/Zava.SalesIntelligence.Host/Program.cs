using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;
using Zava.SalesIntelligence.Agents;
using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Dataverse;

// Phase 2: Watcher retrieves, Thinker judges. Called by hand, one account at a time.
// The Thinker has no tools and works only from the AccountContext it is handed.
string dataverseUrl = RequireEnv("ZAVA_DATAVERSE_URL");
string openAiEndpoint = RequireEnv("ZAVA_OPENAI_ENDPOINT");
string reasoningDeployment = RequireEnv("ZAVA_OPENAI_REASONING_DEPLOYMENT");
var credential = new AzureCliCredential();

using var dataverse = DataverseClientFactory.Create(dataverseUrl, credential);
var queries = new WatcherQueries(dataverse);
var watcher = new WatcherService(queries);

AIAgent thinker = new AzureOpenAIClient(new Uri(openAiEndpoint), credential)
    .GetChatClient(reasoningDeployment)
    .AsAIAgent(name: "Thinker", instructions: Thinker.BuildSystemPrompt());

var accounts = (await queries.GetAccountsAsync(CancellationToken.None))
    .OrderBy(a => a.AccountNumber)
    .ToList();

Console.WriteLine($"Thinker over {accounts.Count} accounts on {reasoningDeployment}");
Console.WriteLine();

foreach (var account in accounts)
{
    var context = await watcher.BuildContextAsync(account, CancellationToken.None);
    var contextJson = JsonSerializer.Serialize(context);

    var response = await thinker.RunAsync<OpportunityAssessment>(contextJson, cancellationToken: CancellationToken.None);
    var assessment = response.Result;

    var knownIds = CollectIds(context);
    var citedIds = new[] { assessment.Bant.Budget, assessment.Bant.Authority, assessment.Bant.Need, assessment.Bant.Timing }
        .SelectMany(criterion => criterion.Evidence)
        .Select(evidence => evidence.RecordId)
        .ToList();
    var invented = citedIds.Where(id => !knownIds.Contains(id)).ToList();

    Console.WriteLine($"{account.AccountNumber}  {account.Name}");
    Console.WriteLine($"  archetype={assessment.Archetype}  bant={assessment.Bant.Score}  confidence={assessment.Confidence}");
    Console.WriteLine($"  evidence cited={citedIds.Count}  invented ids={invented.Count}");
    Console.WriteLine($"  rationale: {assessment.Rationale}");
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
