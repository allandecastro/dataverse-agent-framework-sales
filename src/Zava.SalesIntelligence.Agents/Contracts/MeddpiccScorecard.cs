using System.Text.Json.Serialization;

namespace Zava.SalesIntelligence.Agents.Contracts;

// The AE persona's deeper qualification: the eight MEDDPICC elements, produced by a separate
// scorer pass that runs only in the AE persona, over the same account context the Thinker saw.
// In AE it drives the board: the scorer assigns the archetype from the eight elements. The BANT
// triage still runs underneath and still drives the Operator's actions, and the SDR persona
// never sees this record - so the SDR determinism gate is unaffected. Reuses BantStatus and
// Evidence - the status values (Confirmed/Partial/Missing) are rubric-agnostic.
public record MeddpiccScorecard(
    MeddpiccElement Metrics,
    MeddpiccElement EconomicBuyer,
    MeddpiccElement DecisionCriteria,
    MeddpiccElement DecisionProcess,
    MeddpiccElement PaperProcess,
    MeddpiccElement IdentifyPain,
    MeddpiccElement Champion,
    MeddpiccElement Competition,
    // In the AE persona MEDDPICC drives the board: the scorer assigns the archetype from the eight
    // elements (not from BANT) and a one-line rationale to read aloud.
    Archetype Archetype,
    string Rationale)
{
    [JsonIgnore]
    public int Score => new[]
        {
            Metrics, EconomicBuyer, DecisionCriteria, DecisionProcess,
            PaperProcess, IdentifyPain, Champion, Competition,
        }
        .Count(element => element.Status == BantStatus.Confirmed);
}

public record MeddpiccElement(
    BantStatus Status,          // Confirmed, Partial, Missing (shared with BANT)
    string Reasoning,
    IReadOnlyList<Evidence> Evidence);
