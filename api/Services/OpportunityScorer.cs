using Portfolio.Freight.Api.Models;

namespace Portfolio.Freight.Api.Services;

public sealed class OpportunityScorer
{
    // Portfolio-only scoring: intentionally simple and explainable. It is not copied
    // from any employer system and should not be interpreted as a production model.
    public Opportunity Score(CustomerAccount customer)
    {
        var inactivity = Math.Min(customer.DaysSinceTouch * 2.5m, 35m);
        var volume = Math.Min(customer.MonthlyLoads * 0.8m, 30m);
        var margin = Math.Min(customer.MonthlyGrossMargin / 1000m, 25m);
        var stage = customer.Stage switch
        {
            "At Risk" => 10m,
            "Reactivation" => 8m,
            "Expansion" => 7m,
            "Growth" => 5m,
            _ => 2m
        };
        var overdue = Math.Min(customer.OverdueFollowUps * 5m, 10m);

        var score = Math.Round(Math.Min(inactivity + volume + margin + stage + overdue, 100m), 1);
        var why = customer.DaysSinceTouch >= 10
            ? $"No recorded touch in {customer.DaysSinceTouch} days; account also carries meaningful recurring volume."
            : "Healthy recent activity with enough recurring volume to justify proactive account work.";
        if (customer.OverdueFollowUps > 0)
        {
            why += customer.OverdueFollowUps == 1
                ? " One follow-up is overdue."
                : $" {customer.OverdueFollowUps} follow-ups are overdue.";
        }
        var action = customer.Stage switch
        {
            "At Risk" => "Schedule a service-recovery call and review the last three shipments.",
            "Reactivation" => "Re-open the account with a lane-specific capacity update.",
            "Expansion" => "Ask for adjacent lanes and forecasted volume.",
            "Prospect" => "Book an intro call and qualify lanes, volume and equipment.",
            _ => "Review upcoming capacity and confirm the next shipment window."
        };

        return new(customer.Id, customer.Name, customer.PrimaryLane, score, why, action);
    }
}
