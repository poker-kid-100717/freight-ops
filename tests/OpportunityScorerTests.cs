using Portfolio.Freight.Api.Models;
using Portfolio.Freight.Api.Services;

namespace Portfolio.Freight.Api.Tests;

public sealed class OpportunityScorerTests
{
    private readonly OpportunityScorer _scorer = new();

    [Fact]
    public void AtRiskInactiveAccountRanksAboveHealthyRecentlyTouchedAccount()
    {
        var atRisk = new CustomerAccount(
            Guid.NewGuid(), "Example Manufacturing", "ABQ → PHX", 28, 84000m, 18000m, 18, "At Risk");
        var healthy = new CustomerAccount(
            Guid.NewGuid(), "Example Foods", "ABQ → DEN", 10, 30000m, 5500m, 2, "Growth");

        var riskScore = _scorer.Score(atRisk);
        var healthyScore = _scorer.Score(healthy);

        Assert.True(riskScore.PriorityScore > healthyScore.PriorityScore);
        Assert.Contains("service-recovery", riskScore.RecommendedAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScoreIsCappedAtOneHundred()
    {
        var extreme = new CustomerAccount(
            Guid.NewGuid(), "Example High Volume", "ABQ → LAX", 500, 900000m, 250000m, 365, "At Risk");

        Assert.Equal(100m, _scorer.Score(extreme).PriorityScore);
    }
}

public sealed class OverdueFollowUpScoringTests
{
    [Fact]
    public void OverdueFollowUpsRaiseTheScoreAndAreExplained()
    {
        var scorer = new OpportunityScorer();
        var baseline = new CustomerAccount(Guid.NewGuid(), "Example Co", "ABQ → PHX", 10, 40000m, 6000m, 3, "Active");

        var withOverdue = scorer.Score(baseline with { OverdueFollowUps = 2 });

        Assert.Equal(scorer.Score(baseline).PriorityScore + 10m, withOverdue.PriorityScore);
        Assert.Contains("2 follow-ups are overdue", withOverdue.WhyNow);
    }
}
