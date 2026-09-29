namespace Portfolio.Freight.Api.Models;

public sealed record CustomerAccount(
    Guid Id,
    string Name,
    string PrimaryLane,
    int MonthlyLoads,
    decimal MonthlyRevenue,
    decimal MonthlyGrossMargin,
    int DaysSinceTouch,
    string Stage);

public sealed record Opportunity(
    Guid CustomerId,
    string CustomerName,
    string PrimaryLane,
    decimal PriorityScore,
    string WhyNow,
    string RecommendedAction);

public sealed record ExternalLoad(
    string LoadNumber,
    string CustomerName,
    string Status,
    DateTimeOffset? ScheduledPickupAt,
    DateTimeOffset? ScheduledDeliveryAt,
    IReadOnlyList<string> RequiredEquipment,
    decimal? Weight,
    string Source);

public sealed record ExternalLoadResult(
    string Provider,
    bool Live,
    bool Degraded,
    string? DegradedReason,
    IReadOnlyList<ExternalLoad> Loads);

public sealed record IntegrationStatus(string Provider, string Mode, bool Configured, string Safety);
