using System.Text.Json;
using System.Text.Json.Serialization;

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
    [JsonIgnore]
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

// When the model refuses to conclude it sets Confidence to InsufficientSignal, and
// the archetype has no value for that. If a model still writes a non-archetype string
// here, fall back rather than crash - Confidence is authoritative for those cases.
// Registered on the deserialization options, not as a type attribute, so the response
// schema keeps its enum constraint.
public sealed class ArchetypeJsonConverter : JsonConverter<Archetype>
{
    public override Archetype Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<Archetype>(reader.GetString(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        if (reader.TokenType == JsonTokenType.Number
            && reader.TryGetInt32(out var number)
            && Enum.IsDefined((Archetype)number))
        {
            return (Archetype)number;
        }

        return Archetype.Reconnect;
    }

    public override void Write(Utf8JsonWriter writer, Archetype value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public enum Confidence { High, Medium, InsufficientSignal }

public enum BantStatus { Confirmed, Partial, Missing }
