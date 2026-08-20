namespace Zava.SalesIntelligence.Agents.Contracts;

// What the Watcher retrieves and hands to the Thinker. Every record keeps its
// Dataverse id and date so the Thinker can cite evidence that provably exists.
public record AccountContext(
    Guid AccountId,
    string AccountName,
    IReadOnlyList<OpportunityRecord> Opportunities,
    IReadOnlyList<ActivityRecord> Activities,
    IReadOnlyList<NoteRecord> Notes,
    IReadOnlyList<ContactRecord> Contacts);

public record AccountRecord(
    Guid AccountId,
    string Name,
    string? AccountNumber);

public record OpportunityRecord(
    Guid OpportunityId,
    string Name,
    decimal? Amount,
    string? Stage,
    DateOnly? CloseDate);

public record ActivityRecord(
    Guid ActivityId,
    string Type,
    string Subject,
    DateOnly Date,
    string? Description);

public record NoteRecord(
    Guid NoteId,
    string Subject,
    string Body,
    DateOnly Date,
    Guid RegardingId,
    string RegardingType);

public record ContactRecord(
    Guid ContactId,
    string FullName,
    string? JobTitle,
    bool IsActive);
