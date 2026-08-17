namespace Zava.SalesIntelligence.Agents.Contracts;

public record OpportunityAssessment(
    string AccountName,
    string OpportunityName,
    decimal Amount,
    DateOnly? CloseDate,
    string CrmStage,
    BantAssessment Bant,
    Archetype Archetype,
    string Rationale,          // two sentences max, written to be read aloud
    Confidence Confidence);

public record BantAssessment(
    BantCriterion Budget,
    BantCriterion Authority,
    BantCriterion Need,
    BantCriterion Timing)
{
    public int Score => new[] { Budget, Authority, Need, Timing }
        .Count(c => c.Status == BantStatus.Confirmed);
}

public record BantCriterion(
    BantStatus Status,          // Confirmed, Partial, Missing
    string Reasoning,
    IReadOnlyList<Evidence> Evidence);

public record Evidence(
    string EntityLogicalName,   // annotation, appointment, phonecall, email
    Guid RecordId,
    DateOnly Date,
    string Excerpt);            // 20 words max, verbatim from source

public enum Archetype { Reconnect, AtRisk, ReadyToClose, Slipping }

public enum Confidence { High, Medium, InsufficientSignal }

public enum BantStatus { Confirmed, Partial, Missing }
