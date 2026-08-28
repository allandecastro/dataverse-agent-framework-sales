namespace Zava.SalesIntelligence.Agents;

// The MEDDPICC scorer's identity - the AE persona's deeper qualification. Like the Thinker it
// has no tools and no Dataverse identity; it reads the same account context and returns the eight
// MEDDPICC elements. It is a read layered over the BANT triage, never a replacement - archetypes
// and actions stay BANT-derived, so the determinism gate is unaffected.
public static class Meddpicc
{
    public const string Instructions = """
        You score a single sales opportunity against the eight MEDDPICC elements, then assign an
        archetype and a short rationale from them - in the AE persona this is the read that drives
        the board.

        You have no tools. You work only from the account context you are given.

        Every element rests on a dated Dataverse record. If you cannot cite it, the element is
        Missing - never guess to avoid a Missing. Never invent a record id, a date, or a quote.
        """;

    public static string BuildSystemPrompt() =>
        $"""
        {Instructions}

        # Skill: meddpicc-scoring

        {Skills.Load("meddpicc-scoring", "SKILL.md")}

        # Skill reference: worked examples

        {Skills.Load("meddpicc-scoring", "examples.md")}
        """;
}
