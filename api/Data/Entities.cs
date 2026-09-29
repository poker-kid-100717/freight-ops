namespace Portfolio.Freight.Api.Data;

// Persistence entities. All timestamps are UTC DateTime so both PostgreSQL (timestamptz)
// and the SQLite demo store can sort and filter on them in the database.

public sealed class Rep
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string LaneOrigin { get; set; } = "";
    public string LaneDestination { get; set; } = "";
    public string Stage { get; set; } = Stages.Prospect;
    public int MonthlyLoads { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public decimal MonthlyGrossMargin { get; set; }
    public Guid? OwnerId { get; set; }
    public Rep? Owner { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Contact> Contacts { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
    public List<FollowUp> FollowUps { get; set; } = [];
    public List<Quote> Quotes { get; set; } = [];
    public List<Lane> Lanes { get; set; } = [];
}

public sealed class Contact
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Name { get; set; } = "";
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class Activity
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public Guid? RepId { get; set; }
    public Rep? Rep { get; set; }
    public string Type { get; set; } = ActivityTypes.Note;
    public string Summary { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}

public sealed class FollowUp
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? RepId { get; set; }
    public Rep? Rep { get; set; }
    public DateOnly DueOn { get; set; }
    public string Description { get; set; } = "";
    public string Status { get; set; } = FollowUpStatuses.Open;
    public DateTime? CompletedAt { get; set; }
}

public sealed class Quote
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? LaneId { get; set; }
    public Lane? Lane { get; set; }
    public Guid? CarrierId { get; set; }
    public Carrier? Carrier { get; set; }
    public Guid? RepId { get; set; }
    public Rep? Rep { get; set; }
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Equipment { get; set; } = EquipmentTypes.DryVan;
    public int Pallets { get; set; }
    public int Weight { get; set; }
    public decimal Rate { get; set; }
    public decimal? CarrierCost { get; set; }
    public string Status { get; set; } = QuoteStatuses.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public sealed class Lead
{
    public Guid Id { get; set; }
    public string Company { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Source { get; set; } = LeadSources.Inbound;
    public string Status { get; set; } = LeadStatuses.New;
    public Guid? OwnerId { get; set; }
    public Rep? Owner { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ConvertedCustomerId { get; set; }
}

public sealed class Lane
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Equipment { get; set; } = EquipmentTypes.DryVan;
    public int EstimatedLoadsPerMonth { get; set; }
    public decimal TargetRate { get; set; }
}

public sealed class Carrier
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string McNumber { get; set; } = "";
    public string EquipmentTypes { get; set; } = "";
    public string HomeRegion { get; set; } = "";
    public string Status { get; set; } = CarrierStatuses.Active;
    public int Rating { get; set; }
    public string? Notes { get; set; }
}

public static class Stages
{
    public const string Prospect = "Prospect";
    public const string Active = "Active";
    public const string Growth = "Growth";
    public const string Expansion = "Expansion";
    public const string AtRisk = "At Risk";
    public const string Reactivation = "Reactivation";
    public const string Inactive = "Inactive";
    public static readonly string[] All = [Prospect, Active, Growth, Expansion, AtRisk, Reactivation, Inactive];
}

public static class ActivityTypes
{
    public const string Call = "Call";
    public const string Email = "Email";
    public const string Meeting = "Meeting";
    public const string Note = "Note";
    public static readonly string[] All = [Call, Email, Meeting, Note];
}

public static class FollowUpStatuses
{
    public const string Open = "Open";
    public const string Done = "Done";
}

public static class QuoteStatuses
{
    public const string Draft = "Draft";
    public const string Sent = "Sent";
    public const string Won = "Won";
    public const string Lost = "Lost";
    public static readonly string[] All = [Draft, Sent, Won, Lost];
}

public static class LeadSources
{
    public const string Referral = "Referral";
    public const string Inbound = "Inbound";
    public const string ColdOutreach = "Cold Outreach";
    public const string TradeShow = "Trade Show";
    public static readonly string[] All = [Referral, Inbound, ColdOutreach, TradeShow];
}

public static class LeadStatuses
{
    public const string New = "New";
    public const string Working = "Working";
    public const string Qualified = "Qualified";
    public const string Disqualified = "Disqualified";
    public const string Converted = "Converted";
    public static readonly string[] Editable = [New, Working, Qualified, Disqualified];
    public static readonly string[] All = [New, Working, Qualified, Disqualified, Converted];
}

public static class EquipmentTypes
{
    public const string DryVan = "Dry Van";
    public const string Reefer = "Reefer";
    public const string Flatbed = "Flatbed";
    public static readonly string[] All = [DryVan, Reefer, Flatbed];
}

public static class CarrierStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public static readonly string[] All = [Active, Inactive];
}
