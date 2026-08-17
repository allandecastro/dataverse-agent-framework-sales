using Zava.SalesIntelligence.Agents.Contracts;

namespace Zava.SalesIntelligence.Tests.Contracts;

public class BantAssessmentTests
{
    static BantCriterion Criterion(BantStatus status) => new(status, "reason", []);

    [Fact]
    public void Score_counts_only_confirmed_axes()
    {
        var bant = new BantAssessment(
            Budget: Criterion(BantStatus.Confirmed),
            Authority: Criterion(BantStatus.Partial),
            Need: Criterion(BantStatus.Confirmed),
            Timing: Criterion(BantStatus.Missing));

        Assert.Equal(2, bant.Score);
    }

    [Fact]
    public void Score_is_zero_when_no_axis_is_confirmed()
    {
        var bant = new BantAssessment(
            Criterion(BantStatus.Missing),
            Criterion(BantStatus.Partial),
            Criterion(BantStatus.Missing),
            Criterion(BantStatus.Partial));

        Assert.Equal(0, bant.Score);
    }

    [Fact]
    public void Score_is_four_when_every_axis_is_confirmed()
    {
        var confirmed = Criterion(BantStatus.Confirmed);
        var bant = new BantAssessment(confirmed, confirmed, confirmed, confirmed);

        Assert.Equal(4, bant.Score);
    }
}
