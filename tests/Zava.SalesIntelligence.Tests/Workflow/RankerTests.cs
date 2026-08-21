using Zava.SalesIntelligence.Agents.Contracts;
using Zava.SalesIntelligence.Workflow;

namespace Zava.SalesIntelligence.Tests.Workflow;

public class RankerTests
{
    static readonly DateOnly Today = new(2026, 8, 20);

    static BantCriterion Criterion(BantStatus status) => new(status, "", []);

    // Builds a BANT with the first `score` axes Confirmed and the rest Missing.
    static BantAssessment Bant(int score) => new(
        score >= 1 ? Criterion(BantStatus.Confirmed) : Criterion(BantStatus.Missing),
        score >= 2 ? Criterion(BantStatus.Confirmed) : Criterion(BantStatus.Missing),
        score >= 3 ? Criterion(BantStatus.Confirmed) : Criterion(BantStatus.Missing),
        score >= 4 ? Criterion(BantStatus.Confirmed) : Criterion(BantStatus.Missing));

    static AssessedOpportunity Item(
        string name,
        int score = 4,
        decimal amount = 100_000,
        DateOnly? closeDate = null,
        DateOnly? lastActivity = null,
        Confidence confidence = Confidence.High) =>
        new(
            new OpportunityAssessment(
                AccountName: name,
                OpportunityName: name,
                Amount: amount,
                CloseDate: closeDate,
                CrmStage: "Propose",
                Bant: Bant(score),
                Archetype: Archetype.ReadyToClose,
                Rationale: "",
                Confidence: confidence),
            lastActivity);

    static List<string> Order(params AssessedOpportunity[] items) =>
        Ranker.Rank(items, Today).Select(i => i.Assessment.OpportunityName).ToList();

    [Fact]
    public void Drops_insufficient_signal()
    {
        var ranked = Ranker.Rank(
            [Item("keep", confidence: Confidence.Medium), Item("drop", confidence: Confidence.InsufficientSignal)],
            Today);

        Assert.Equal(["keep"], ranked.Select(i => i.Assessment.OpportunityName));
    }

    [Fact]
    public void Sorts_by_bant_score_first_descending()
    {
        Assert.Equal(["high", "low"], Order(Item("low", score: 1), Item("high", score: 4)));
    }

    [Fact]
    public void Breaks_bant_ties_by_amount_descending()
    {
        Assert.Equal(
            ["big", "small"],
            Order(Item("small", score: 3, amount: 50_000), Item("big", score: 3, amount: 90_000)));
    }

    [Fact]
    public void Breaks_amount_ties_by_days_to_close_ascending()
    {
        Assert.Equal(
            ["sooner", "later"],
            Order(
                Item("later", score: 3, amount: 100_000, closeDate: Today.AddDays(40)),
                Item("sooner", score: 3, amount: 100_000, closeDate: Today.AddDays(5))));
    }

    [Fact]
    public void Breaks_close_ties_by_days_silent_descending()
    {
        Assert.Equal(
            ["quieter", "recent"],
            Order(
                Item("recent", score: 3, amount: 100_000, closeDate: Today.AddDays(10), lastActivity: Today.AddDays(-5)),
                Item("quieter", score: 3, amount: 100_000, closeDate: Today.AddDays(10), lastActivity: Today.AddDays(-40))));
    }

    [Fact]
    public void Is_deterministic_for_the_same_input()
    {
        AssessedOpportunity[] items =
        [
            Item("a", score: 4, amount: 10_000),
            Item("b", score: 4, amount: 20_000),
            Item("c", score: 2, amount: 90_000),
        ];

        Assert.Equal(Order(items), Order(items));
        Assert.Equal(["b", "a", "c"], Order(items));
    }
}
