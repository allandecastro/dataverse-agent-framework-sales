using Zava.SalesIntelligence.Agents.Contracts;

namespace Zava.SalesIntelligence.Dataverse;

// Assembles a full AccountContext deterministically, preserving every record id.
// The Thinker judges only what this returns, so the retrieval stays reliable and
// the phase 2 gate can hold. Notes are gathered for the account and each of its
// open opportunities, since evidence hangs off both.
public sealed class WatcherService(WatcherQueries queries)
{
    public async Task<AccountContext> BuildContextAsync(AccountRecord account, CancellationToken cancellationToken)
    {
        var opportunities = await queries.GetOpenOpportunitiesAsync(account.AccountId, cancellationToken);
        var activities = await queries.GetActivitiesAsync(account.AccountId, 180, cancellationToken);
        var contacts = await queries.GetContactsAsync(account.AccountId, cancellationToken);

        var notes = new List<NoteRecord>(await queries.GetNotesAsync(account.AccountId, cancellationToken));
        foreach (var opportunity in opportunities)
        {
            notes.AddRange(await queries.GetNotesAsync(opportunity.OpportunityId, cancellationToken));
        }

        return new AccountContext(
            account.AccountId,
            account.Name,
            opportunities,
            activities,
            notes,
            contacts);
    }
}
