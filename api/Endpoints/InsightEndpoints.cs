using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Portfolio.Freight.Api.Data;
using Portfolio.Freight.Api.Integrations.Alvys;

namespace Portfolio.Freight.Api.Endpoints;

public sealed record CarrierRequest(string? Name, string? McNumber, IReadOnlyList<string>? EquipmentTypes, string? HomeRegion, string? Status, int Rating, string? Notes);
public sealed record CarrierDto(Guid Id, string Name, string McNumber, IReadOnlyList<string> EquipmentTypes, string HomeRegion, string Status,
    int Rating, string? Notes, int QuotesWon);
public sealed record SearchHit(string Type, Guid Id, string Title, string Subtitle, string Url);
public sealed record ReportColumn(string Key, string Label, string Kind);
public sealed record Report(string Name, string Title, string Description, IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows, string ChartLabel, string ChartValue);

public static class InsightEndpoints
{
    public static readonly string[] ReportNames = ["revenue-by-customer", "quotes-by-month", "activity-by-rep", "stage-distribution"];

    public static void MapInsightEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", Dashboard).WithTags("Home");
        api.MapGet("/my-day", MyDay).WithTags("Home");
        api.MapGet("/opportunities", async (FreightDbContext db, CustomerInsights insights, CancellationToken ct) =>
            Results.Ok((await insights.SnapshotsAsync(db.Customers.AsNoTracking().Where(c => c.Stage != Stages.Inactive), ct))
                .OrderByDescending(x => x.Score).Select(CustomerInsights.ToOpportunity))).WithTags("Home");
        api.MapGet("/search", Search).WithTags("Search");
        api.MapGet("/reps", async (FreightDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Reps.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct))).WithTags("Settings");
        api.MapGet("/reports/{name}", GetReport).WithTags("Reports");

        var carriers = api.MapGroup("/carriers").WithTags("Carriers");
        carriers.MapGet("/", ListCarriers);
        carriers.MapGet("/{id:guid}", async (Guid id, FreightDbContext db, CancellationToken ct) =>
            await CarrierQuery(db.Carriers.Where(c => c.Id == id), db).SingleOrDefaultAsync(ct) is { } c ? Results.Ok(Map(c)) : Http.NotFound("Carrier"));
        carriers.MapPost("/", CreateCarrier);
        carriers.MapPut("/{id:guid}", UpdateCarrier);
    }

    /// <summary>Endpoints that must work even when the database is down.</summary>
    public static void MapPlatformEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/meta", Meta).WithTags("Settings");
        api.MapPost("/admin/reset-demo", ResetDemo).WithTags("Settings").ExcludeFromDescription();
    }

    // ---------- home ----------

    private static async Task<IResult> Dashboard(FreightDbContext db, CustomerInsights insights, TimeProvider clock, CancellationToken ct)
    {
        var today = insights.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var snapshots = await insights.SnapshotsAsync(db.Customers.AsNoTracking(), ct);
        var active = snapshots.Where(s => s.Stage is not (Stages.Prospect or Stages.Inactive)).ToList();

        var open = await db.Quotes.AsNoTracking().Where(q => q.Status == QuoteStatuses.Draft || q.Status == QuoteStatuses.Sent)
            .Select(q => new { q.Status, q.Rate }).ToListAsync(ct);
        var wonThisMonth = await db.Quotes.AsNoTracking()
            .Where(q => q.Status == QuoteStatuses.Won && q.ClosedAt >= monthStart).Select(q => q.Rate).ToListAsync(ct);
        var followUps = await db.FollowUps.AsNoTracking()
            .Where(f => f.Status == FollowUpStatuses.Open && f.DueOn <= today).Select(f => f.DueOn).ToListAsync(ct);

        return Results.Ok(new
        {
            activeCustomers = active.Count,
            openOpportunities = open.Count,
            monthlyLoads = active.Sum(s => s.MonthlyLoads),
            monthlyRevenue = active.Sum(s => s.MonthlyRevenue),
            grossMargin = active.Sum(s => s.MonthlyGrossMargin),
            followUpsDue = followUps.Count,
            followUpsOverdue = followUps.Count(d => d < today),
            quotesWonThisMonth = wonThisMonth.Count,
            wonValueThisMonth = wonThisMonth.Sum(),
            pipelineValue = open.Where(q => q.Status == QuoteStatuses.Sent).Sum(q => q.Rate),
            atRiskAccounts = snapshots.Count(s => s.Stage == Stages.AtRisk),
            topOpportunities = snapshots.Where(s => s.Stage != Stages.Inactive)
                .OrderByDescending(s => s.Score).ThenBy(s => s.Name).Take(5).Select(CustomerInsights.ToOpportunity)
        });
    }

    private static async Task<IResult> MyDay(FreightDbContext db, CustomerInsights insights, Guid? rep, CancellationToken ct)
    {
        var today = insights.Today;
        var horizon = today.AddDays(7);
        var followUps = db.FollowUps.Where(f => f.Status == FollowUpStatuses.Open && f.DueOn <= horizon);
        IQueryable<Activity> activities = db.Activities;
        var quotes = db.Quotes.Where(q => q.Status == QuoteStatuses.Draft || q.Status == QuoteStatuses.Sent);
        var customers = db.Customers.AsNoTracking().Where(c => c.Stage != Stages.Inactive);
        if (rep is { } r)
        {
            followUps = followUps.Where(f => f.RepId == r);
            activities = activities.Where(a => a.RepId == r);
            quotes = quotes.Where(q => q.RepId == r);
            customers = customers.Where(c => c.OwnerId == r);
        }

        var due = await AccountEndpoints.FollowUpQuery(followUps.OrderBy(f => f.DueOn).ThenBy(f => f.Customer!.Name), today).ToListAsync(ct);
        return Results.Ok(new
        {
            today,
            overdue = due.Where(f => f.DueOn < today),
            dueToday = due.Where(f => f.DueOn == today),
            upcoming = due.Where(f => f.DueOn > today),
            openQuotes = await SalesEndpoints.QuoteQuery(quotes, db).Take(20).ToListAsync(ct),
            recentActivity = await AccountEndpoints.ActivityQuery(activities).Take(10).ToListAsync(ct),
            priorityAccounts = (await insights.SnapshotsAsync(customers, ct)).OrderByDescending(s => s.Score).Take(5)
        });
    }

    // ---------- search ----------

    private static async Task<IResult> Search(FreightDbContext db, string? q, CancellationToken ct)
    {
        var term = Http.Clean(q);
        if (term is null || term.Length < 2) return Results.Ok(Array.Empty<SearchHit>());
        if (term.Length > 80) term = term[..80];
        var t = term.ToLower();

        var hits = new List<SearchHit>();
        hits.AddRange(await db.Customers.AsNoTracking().Where(c => c.Name.ToLower().Contains(t)).OrderBy(c => c.Name).Take(5)
            .Select(c => new SearchHit("Account", c.Id, c.Name, c.Stage + " · " + c.LaneOrigin + " → " + c.LaneDestination, "/accounts/" + c.Id)).ToListAsync(ct));
        hits.AddRange(await db.Contacts.AsNoTracking().Where(c => c.Name.ToLower().Contains(t) || (c.Email != null && c.Email.ToLower().Contains(t)))
            .OrderBy(c => c.Name).Take(5)
            .Select(c => new SearchHit("Contact", c.Id, c.Name, (c.Role ?? "Contact") + " at " + c.Customer!.Name, "/accounts/" + c.CustomerId)).ToListAsync(ct));
        hits.AddRange(await db.Leads.AsNoTracking().Where(l => l.Company.ToLower().Contains(t) || l.ContactName.ToLower().Contains(t))
            .OrderBy(l => l.Company).Take(5)
            .Select(l => new SearchHit("Lead", l.Id, l.Company, l.Status + " · " + l.ContactName, "/leads?open=" + l.Id)).ToListAsync(ct));
        hits.AddRange(await db.Quotes.AsNoTracking().Where(x => x.Number.ToLower().Contains(t)).OrderBy(x => x.Number).Take(5)
            .Select(x => new SearchHit("Quote", x.Id, x.Number, x.Status + " · " + x.Customer!.Name, "/quotes?open=" + x.Id)).ToListAsync(ct));
        hits.AddRange(await db.Carriers.AsNoTracking().Where(c => c.Name.ToLower().Contains(t) || c.McNumber.ToLower().Contains(t))
            .OrderBy(c => c.Name).Take(5)
            .Select(c => new SearchHit("Carrier", c.Id, c.Name, c.McNumber + " · " + c.HomeRegion, "/carriers?open=" + c.Id)).ToListAsync(ct));
        return Results.Ok(hits);
    }

    // ---------- carriers ----------

    private static IQueryable<CarrierRow> CarrierQuery(IQueryable<Carrier> source, FreightDbContext db) =>
        source.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new CarrierRow(c.Id, c.Name, c.McNumber, c.EquipmentTypes, c.HomeRegion, c.Status, c.Rating, c.Notes,
                db.Quotes.Count(q => q.CarrierId == c.Id && q.Status == QuoteStatuses.Won)));

    private sealed record CarrierRow(Guid Id, string Name, string McNumber, string EquipmentTypes, string HomeRegion, string Status,
        int Rating, string? Notes, int QuotesWon);

    private static CarrierDto Map(CarrierRow c) => new(c.Id, c.Name, c.McNumber,
        c.EquipmentTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), c.HomeRegion, c.Status,
        c.Rating, c.Notes, c.QuotesWon);

    private static async Task<IResult> ListCarriers(FreightDbContext db, string? search, string? status, string? equipment,
        int? page, int? pageSize, CancellationToken ct)
    {
        IQueryable<Carrier> query = db.Carriers;
        if (Http.Clean(search) is { } term)
        {
            var t = term.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(t) || c.McNumber.ToLower().Contains(t) || c.HomeRegion.ToLower().Contains(t));
        }
        if (Http.Clean(status) is { } s) query = query.Where(c => c.Status == s);
        if (Http.Clean(equipment) is { } e) query = query.Where(c => c.EquipmentTypes.Contains(e));
        var paged = await CarrierQuery(query, db).ToPagedAsync(page, pageSize, ct);
        return Results.Ok(new Paged<CarrierDto>(paged.Items.Select(Map).ToList(), paged.Total, paged.Page, paged.PageSize));
    }

    private static Checks Validate(CarrierRequest r)
    {
        var checks = new Checks()
            .Required("name", r.Name, Limits.Name)
            .Required("mcNumber", r.McNumber, 20)
            .Required("homeRegion", r.HomeRegion, Limits.Place)
            .OneOf("status", r.Status, CarrierStatuses.All)
            .Range("rating", r.Rating, 1, 5)
            .Optional("notes", r.Notes, Limits.Notes);
        if (r.EquipmentTypes is not { Count: > 0 } || r.EquipmentTypes.Any(e => !EquipmentTypes.All.Contains(e)))
            checks.Add("equipmentTypes", $"Choose one or more of: {string.Join(", ", EquipmentTypes.All)}.");
        return checks;
    }

    private static async Task<IResult> CreateCarrier(CarrierRequest request, FreightDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var mc = request.McNumber!.Trim().ToUpperInvariant();
        if (await db.Carriers.AnyAsync(c => c.McNumber == mc, ct)) return Http.Conflict($"A carrier with {mc} already exists.");
        var carrier = new Carrier { Id = Guid.NewGuid() };
        Apply(carrier, request);
        db.Carriers.Add(carrier);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/carriers/{carrier.Id}", new { carrier.Id });
    }

    private static async Task<IResult> UpdateCarrier(Guid id, CarrierRequest request, FreightDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var carrier = await db.Carriers.FindAsync([id], ct);
        if (carrier is null) return Http.NotFound("Carrier");
        var mc = request.McNumber!.Trim().ToUpperInvariant();
        if (await db.Carriers.AnyAsync(c => c.McNumber == mc && c.Id != id, ct)) return Http.Conflict($"A carrier with {mc} already exists.");
        Apply(carrier, request);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static void Apply(Carrier c, CarrierRequest r)
    {
        c.Name = r.Name!.Trim();
        c.McNumber = r.McNumber!.Trim().ToUpperInvariant();
        c.EquipmentTypes = string.Join(", ", EquipmentTypes.All.Where(e => r.EquipmentTypes!.Contains(e)));
        c.HomeRegion = r.HomeRegion!.Trim();
        c.Status = r.Status!;
        c.Rating = r.Rating;
        c.Notes = Http.Clean(r.Notes);
    }

    // ---------- reports ----------

    private static async Task<IResult> GetReport(string name, string? format, FreightDbContext db, CustomerInsights insights, CancellationToken ct)
    {
        Report? report = name switch
        {
            "revenue-by-customer" => await RevenueByCustomer(db, ct),
            "quotes-by-month" => await QuotesByMonth(db, insights.Today, ct),
            "activity-by-rep" => await ActivityByRep(db, insights.Today, ct),
            "stage-distribution" => await StageDistribution(db, ct),
            _ => null
        };
        if (report is null) return Http.NotFound("Report");
        return format == "csv"
            ? Results.File(Encoding.UTF8.GetBytes(ToCsv(report)), "text/csv", $"{report.Name}.csv")
            : Results.Ok(report);
    }

    private static async Task<Report> RevenueByCustomer(FreightDbContext db, CancellationToken ct)
    {
        var rows = await db.Customers.AsNoTracking().Where(c => c.MonthlyRevenue > 0)
            .OrderByDescending(c => c.MonthlyRevenue)
            .Select(c => new { c.Name, c.Stage, c.MonthlyLoads, c.MonthlyRevenue, c.MonthlyGrossMargin }).ToListAsync(ct);
        return new Report("revenue-by-customer", "Revenue by customer", "Monthly revenue and gross margin for every billing account.",
            [new("customer", "Customer", "text"), new("stage", "Stage", "text"), new("loads", "Loads / month", "number"),
             new("revenue", "Revenue / month", "money"), new("margin", "Gross margin", "money"), new("marginPct", "Margin %", "percent")],
            rows.Select(r => new Dictionary<string, object?>
            {
                ["customer"] = r.Name, ["stage"] = r.Stage, ["loads"] = r.MonthlyLoads, ["revenue"] = r.MonthlyRevenue,
                ["margin"] = r.MonthlyGrossMargin, ["marginPct"] = Math.Round(r.MonthlyGrossMargin / r.MonthlyRevenue * 100, 1)
            }).ToList(), "customer", "revenue");
    }

    private static async Task<Report> QuotesByMonth(FreightDbContext db, DateOnly today, CancellationToken ct)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
        var since = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var closed = await db.Quotes.AsNoTracking()
            .Where(q => q.ClosedAt != null && q.ClosedAt >= since && (q.Status == QuoteStatuses.Won || q.Status == QuoteStatuses.Lost))
            .Select(q => new { q.Status, q.Rate, ClosedAt = q.ClosedAt!.Value }).ToListAsync(ct);
        var rows = Enumerable.Range(0, 6).Select(i => first.AddMonths(i)).Select(month =>
        {
            var inMonth = closed.Where(q => q.ClosedAt.Year == month.Year && q.ClosedAt.Month == month.Month).ToList();
            var won = inMonth.Where(q => q.Status == QuoteStatuses.Won).ToList();
            var decided = inMonth.Count;
            return new Dictionary<string, object?>
            {
                ["month"] = month.ToString("MMM yyyy", CultureInfo.InvariantCulture), ["won"] = won.Count,
                ["lost"] = decided - won.Count, ["winRate"] = decided == 0 ? 0m : Math.Round(won.Count * 100m / decided, 1),
                ["wonValue"] = won.Sum(q => q.Rate)
            };
        }).ToList();
        return new Report("quotes-by-month", "Quotes won and lost by month", "Closed quotes over the last six months.",
            [new("month", "Month", "text"), new("won", "Won", "number"), new("lost", "Lost", "number"),
             new("winRate", "Win rate", "percent"), new("wonValue", "Won value", "money")],
            rows, "month", "wonValue");
    }

    private static async Task<Report> ActivityByRep(FreightDbContext db, DateOnly today, CancellationToken ct)
    {
        var since = today.AddDays(-29).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var reps = await db.Reps.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var activity = await db.Activities.AsNoTracking().Where(a => a.OccurredAt >= since && a.RepId != null)
            .GroupBy(a => new { a.RepId, a.Type }).Select(g => new { g.Key.RepId, g.Key.Type, Count = g.Count() }).ToListAsync(ct);
        int Count(Guid rep, string type) => activity.Where(a => a.RepId == rep && a.Type == type).Sum(a => a.Count);
        return new Report("activity-by-rep", "Activity by rep", "Logged calls, emails, meetings and notes in the last 30 days.",
            [new("rep", "Rep", "text"), new("calls", "Calls", "number"), new("emails", "Emails", "number"),
             new("meetings", "Meetings", "number"), new("notes", "Notes", "number"), new("total", "Total", "number")],
            reps.Select(r => new Dictionary<string, object?>
            {
                ["rep"] = r.Name, ["calls"] = Count(r.Id, ActivityTypes.Call), ["emails"] = Count(r.Id, ActivityTypes.Email),
                ["meetings"] = Count(r.Id, ActivityTypes.Meeting), ["notes"] = Count(r.Id, ActivityTypes.Note),
                ["total"] = activity.Where(a => a.RepId == r.Id).Sum(a => a.Count)
            }).ToList(), "rep", "total");
    }

    private static async Task<Report> StageDistribution(FreightDbContext db, CancellationToken ct)
    {
        // Totalled in memory: one row per account, and the SQLite demo store cannot SUM decimals.
        var groups = (await db.Customers.AsNoTracking().Select(c => new { c.Stage, c.MonthlyRevenue }).ToListAsync(ct))
            .GroupBy(c => c.Stage)
            .Select(g => new { Stage = g.Key, Count = g.Count(), Revenue = g.Sum(c => c.MonthlyRevenue) }).ToList();
        return new Report("stage-distribution", "Accounts by stage", "How many accounts sit in each stage, and the monthly revenue they carry.",
            [new("stage", "Stage", "text"), new("accounts", "Accounts", "number"), new("revenue", "Revenue / month", "money")],
            Stages.All.Select(s => new Dictionary<string, object?>
            {
                ["stage"] = s, ["accounts"] = groups.FirstOrDefault(g => g.Stage == s)?.Count ?? 0,
                ["revenue"] = groups.FirstOrDefault(g => g.Stage == s)?.Revenue ?? 0m
            }).ToList(), "stage", "accounts");
    }

    public static string ToCsv(Report report)
    {
        static string Cell(object? value)
        {
            var text = value switch
            {
                null => "",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
            // Quote everything that needs it, and neutralise spreadsheet formula injection.
            if (text.Length > 0 && "=+-@".Contains(text[0]) && value is string) text = "'" + text;
            return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
        }

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', report.Columns.Select(c => Cell(c.Label))));
        foreach (var row in report.Rows) csv.AppendLine(string.Join(',', report.Columns.Select(c => Cell(row.GetValueOrDefault(c.Key)))));
        return csv.ToString();
    }

    // ---------- platform ----------

    private static async Task<IResult> Meta(DatabaseStatus database, IExternalLoadReader loads, IConfiguration config, FreightDbContext db, CancellationToken ct)
    {
        var reps = database.Ready ? await db.Reps.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct) : [];
        return Results.Ok(new
        {
            stages = Stages.All,
            activityTypes = ActivityTypes.All,
            quoteStatuses = QuoteStatuses.All,
            leadSources = LeadSources.All,
            leadStatuses = LeadStatuses.Editable,
            equipmentTypes = EquipmentTypes.All,
            carrierStatuses = CarrierStatuses.All,
            reports = ReportNames,
            reps,
            storage = new { mode = database.Mode, persistent = database.Persistent, ready = database.Ready },
            demoReset = new { scheduled = !string.IsNullOrEmpty(ResetToken(config)), schedule = "Daily at 08:17 UTC" },
            integration = loads.Status
        });
    }

    private static string? ResetToken(IConfiguration config) => config["Demo:ResetToken"] ?? config["DEMO_RESET_TOKEN"];

    private static async Task<IResult> ResetDemo(HttpRequest request, IConfiguration config, FreightDbContext db, DemoSeeder seeder,
        DatabaseGate gate, CancellationToken ct)
    {
        var expected = ResetToken(config);
        if (string.IsNullOrEmpty(expected)) return Results.NotFound();
        var supplied = request.Headers["X-Demo-Reset-Token"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid reset token.");
        if (!await gate.EnsureReadyAsync(ct))
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The database is unavailable.");
        await seeder.ResetAsync(db, ct);
        return Results.Ok(new { reset = true });
    }
}
