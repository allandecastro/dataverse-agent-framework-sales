using Azure.Identity;
using Zava.SalesIntelligence.Dataverse;

// Phase 2, step 1: the Watcher retrieves. This runs the five fixed queries and
// assembles a full AccountContext per account, deterministically, preserving every
// record id. The Thinker (structured judgement) and Operator come next.
string dataverseUrl = RequireEnv("ZAVA_DATAVERSE_URL");
var credential = new AzureCliCredential();

using var dataverse = DataverseClientFactory.Create(dataverseUrl, credential);
var queries = new WatcherQueries(dataverse);
var watcher = new WatcherService(queries);

var accounts = await queries.GetAccountsAsync(CancellationToken.None);
Console.WriteLine($"Watcher: {accounts.Count} accounts owned by the seller.");
Console.WriteLine();

foreach (var account in accounts.OrderBy(a => a.AccountNumber))
{
    var context = await watcher.BuildContextAsync(account, CancellationToken.None);

    Console.WriteLine($"{account.AccountNumber}  {context.AccountName}  ({context.AccountId})");
    Console.WriteLine(
        $"  opportunities {context.Opportunities.Count}, " +
        $"activities {context.Activities.Count}, " +
        $"notes {context.Notes.Count}, " +
        $"contacts {context.Contacts.Count}");

    foreach (var opportunity in context.Opportunities)
    {
        Console.WriteLine(
            $"    opp {opportunity.OpportunityId} | {opportunity.Name} | " +
            $"{opportunity.Stage} | {opportunity.Amount} | close {opportunity.CloseDate}");
    }

    foreach (var contact in context.Contacts.Where(c => !c.IsActive))
    {
        Console.WriteLine($"    inactive contact {contact.ContactId} | {contact.FullName} ({contact.JobTitle})");
    }
}

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable {name} is not set.");
