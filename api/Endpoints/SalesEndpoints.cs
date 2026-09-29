using Microsoft.EntityFrameworkCore;
using Portfolio.Freight.Api.Data;

namespace Portfolio.Freight.Api.Endpoints;

public sealed record LeadRequest(string? Company, string? ContactName, string? Email, string? Phone, string? Source, string? Status, Guid? OwnerId, string? Notes);
public sealed record LeadDto(Guid Id, string Company, string ContactName, string? Email, string? Phone, string Source, string Status,
    Guid? OwnerId, string? OwnerName, string? Notes, DateTime CreatedAt, Guid? ConvertedCustomerId);
public sealed record ConvertLeadRequest(string? LaneOrigin, string? LaneDestination, Guid? OwnerId);

public sealed record QuoteRequest(Guid CustomerId, Guid? LaneId, string? Origin, string? Destination, string? Equipment,
    int Pallets, int Weight, decimal Rate, Guid? CarrierId, decimal? CarrierCost, Guid? RepId);
public sealed record QuoteDto(Guid Id, string Number, Guid CustomerId, string CustomerName, Guid? LaneId, string Origin, string Destination,
    string Equipment, int Pallets, int Weight, decimal Rate, Guid? CarrierId, string? CarrierName, decimal? CarrierCost, decimal? Margin,
    string Status, Guid? RepId, string? RepName, DateTime CreatedAt, DateTime? SentAt, DateTime? ClosedAt);

public static class SalesEndpoints
{
    // The only moves a quote can make; everything else is a 409.
    private static readonly Dictionary<string, string[]> QuoteTransitions = new()
    {
        [QuoteStatuses.Draft] = [QuoteStatuses.Sent, QuoteStatuses.Lost],
        [QuoteStatuses.Sent] = [QuoteStatuses.Won, QuoteStatuses.Lost],
        [QuoteStatuses.Won] = [],
        [QuoteStatuses.Lost] = []
    };

    public static void MapSalesEndpoints(this RouteGroupBuilder api)
    {
        var leads = api.MapGroup("/leads").WithTags("Leads");
        leads.MapGet("/", ListLeads);
        leads.MapGet("/{id:guid}", GetLead);
        leads.MapPost("/", CreateLead);
        leads.MapPut("/{id:guid}", UpdateLead);
        leads.MapPost("/{id:guid}/convert", ConvertLead);

        var quotes = api.MapGroup("/quotes").WithTags("Quotes");
        quotes.MapGet("/", ListQuotes);
        quotes.MapGet("/{id:guid}", GetQuote);
        quotes.MapPost("/", CreateQuote);
        quotes.MapPut("/{id:guid}", UpdateQuote);
        quotes.MapPost("/{id:guid}/send", (Guid id, FreightDbContext db, TimeProvider clock, CancellationToken ct) => Move(id, QuoteStatuses.Sent, db, clock, ct));
        quotes.MapPost("/{id:guid}/win", (Guid id, FreightDbContext db, TimeProvider clock, CancellationToken ct) => Move(id, QuoteStatuses.Won, db, clock, ct));
        quotes.MapPost("/{id:guid}/lose", (Guid id, FreightDbContext db, TimeProvider clock, CancellationToken ct) => Move(id, QuoteStatuses.Lost, db, clock, ct));
    }

    public static bool CanMove(string from, string to) => QuoteTransitions.TryGetValue(from, out var next) && next.Contains(to);

    // ---------- leads ----------

    private static IQueryable<LeadDto> LeadQuery(IQueryable<Lead> source) =>
        source.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new LeadDto(l.Id, l.Company, l.ContactName, l.Email, l.Phone, l.Source, l.Status, l.OwnerId,
                l.Owner != null ? l.Owner.Name : null, l.Notes, l.CreatedAt, l.ConvertedCustomerId));

    private static async Task<IResult> ListLeads(FreightDbContext db, string? status, string? search, Guid? owner, int? page, int? pageSize, CancellationToken ct)
    {
        IQueryable<Lead> query = db.Leads;
        if (Http.Clean(status) is { } s) query = query.Where(l => l.Status == s);
        if (owner is { } o) query = query.Where(l => l.OwnerId == o);
        if (Http.Clean(search) is { } term)
        {
            var t = term.ToLower();
            query = query.Where(l => l.Company.ToLower().Contains(t) || l.ContactName.ToLower().Contains(t));
        }
        return Results.Ok(await LeadQuery(query).ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> GetLead(Guid id, FreightDbContext db, CancellationToken ct) =>
        await LeadQuery(db.Leads.Where(l => l.Id == id)).SingleOrDefaultAsync(ct) is { } lead ? Results.Ok(lead) : Http.NotFound("Lead");

    private static Checks Validate(LeadRequest r) => new Checks()
        .Required("company", r.Company, Limits.Name)
        .Required("contactName", r.ContactName, Limits.Name)
        .Email("email", r.Email)
        .Optional("phone", r.Phone, 40)
        .OneOf("source", r.Source, LeadSources.All)
        .OneOf("status", r.Status, LeadStatuses.Editable)
        .Optional("notes", r.Notes, Limits.Notes);

    private static async Task<IResult> CreateLead(LeadRequest request, FreightDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var checks = Validate(request);
        await AccountEndpoints.CheckRep(db, checks, "ownerId", request.OwnerId, ct);
        if (!checks.Ok) return checks.Problem();
        var lead = new Lead { Id = Guid.NewGuid(), CreatedAt = clock.GetUtcNow().UtcDateTime };
        Apply(lead, request);
        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/leads/{lead.Id}", new { lead.Id });
    }

    private static async Task<IResult> UpdateLead(Guid id, LeadRequest request, FreightDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        await AccountEndpoints.CheckRep(db, checks, "ownerId", request.OwnerId, ct);
        if (!checks.Ok) return checks.Problem();
        var lead = await db.Leads.FindAsync([id], ct);
        if (lead is null) return Http.NotFound("Lead");
        if (lead.Status == LeadStatuses.Converted) return Http.Conflict("Converted leads are read-only; edit the account instead.");
        Apply(lead, request);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static void Apply(Lead l, LeadRequest r)
    {
        l.Company = r.Company!.Trim();
        l.ContactName = r.ContactName!.Trim();
        l.Email = Http.Clean(r.Email);
        l.Phone = Http.Clean(r.Phone);
        l.Source = r.Source!;
        l.Status = r.Status!;
        l.OwnerId = r.OwnerId;
        l.Notes = Http.Clean(r.Notes);
    }

    /// <summary>A qualified lead becomes a Prospect account with its primary contact, in one transaction.</summary>
    private static async Task<IResult> ConvertLead(Guid id, ConvertLeadRequest request, FreightDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var checks = new Checks()
            .Required("laneOrigin", request.LaneOrigin, Limits.Place)
            .Required("laneDestination", request.LaneDestination, Limits.Place);
        await AccountEndpoints.CheckRep(db, checks, "ownerId", request.OwnerId, ct);
        if (!checks.Ok) return checks.Problem();

        var lead = await db.Leads.FindAsync([id], ct);
        if (lead is null) return Http.NotFound("Lead");
        if (lead.Status != LeadStatuses.Qualified) return Http.Conflict($"Only qualified leads can be converted; this lead is {lead.Status}.");
        if (await db.Customers.AnyAsync(c => c.Name == lead.Company, ct)) return Http.Conflict($"A customer named \"{lead.Company}\" already exists.");

        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var customer = new Customer
        {
            Id = Guid.NewGuid(), Name = lead.Company, LaneOrigin = request.LaneOrigin!.Trim(), LaneDestination = request.LaneDestination!.Trim(),
            Stage = Stages.Prospect, OwnerId = request.OwnerId ?? lead.OwnerId, Notes = lead.Notes, CreatedAt = now, UpdatedAt = now
        };
        db.Customers.Add(customer);
        db.Contacts.Add(new Contact
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, Name = lead.ContactName, Email = lead.Email, Phone = lead.Phone, IsPrimary = true
        });
        db.Activities.Add(AccountEndpoints.SystemActivity(customer.Id, customer.OwnerId, ActivityTypes.Note,
            $"Converted from a {lead.Source.ToLowerInvariant()} lead.", now));
        lead.Status = LeadStatuses.Converted;
        lead.ConvertedCustomerId = customer.Id;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { customerId = customer.Id });
    }

    // ---------- quotes ----------

    internal static IQueryable<QuoteDto> QuoteQuery(IQueryable<Quote> source, FreightDbContext db) =>
        source.AsNoTracking()
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new QuoteDto(q.Id, q.Number, q.CustomerId, q.Customer!.Name, q.LaneId, q.Origin, q.Destination, q.Equipment,
                q.Pallets, q.Weight, q.Rate, q.CarrierId, q.Carrier != null ? q.Carrier.Name : null, q.CarrierCost,
                q.CarrierCost != null ? q.Rate - q.CarrierCost : null, q.Status, q.RepId, q.Rep != null ? q.Rep.Name : null,
                q.CreatedAt, q.SentAt, q.ClosedAt));

    private static async Task<IResult> ListQuotes(FreightDbContext db, string? status, Guid? customerId, Guid? rep, string? search,
        int? page, int? pageSize, CancellationToken ct)
    {
        IQueryable<Quote> query = db.Quotes;
        if (Http.Clean(status) is { } s) query = query.Where(q => q.Status == s);
        if (customerId is { } c) query = query.Where(q => q.CustomerId == c);
        if (rep is { } r) query = query.Where(q => q.RepId == r);
        if (Http.Clean(search) is { } term)
        {
            var t = term.ToLower();
            query = query.Where(q => q.Number.ToLower().Contains(t) || q.Customer!.Name.ToLower().Contains(t) ||
                                     q.Origin.ToLower().Contains(t) || q.Destination.ToLower().Contains(t));
        }
        return Results.Ok(await QuoteQuery(query, db).ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> GetQuote(Guid id, FreightDbContext db, CancellationToken ct) =>
        await QuoteQuery(db.Quotes.Where(q => q.Id == id), db).SingleOrDefaultAsync(ct) is { } quote ? Results.Ok(quote) : Http.NotFound("Quote");

    private static async Task<Checks> ValidateAsync(QuoteRequest r, FreightDbContext db, CancellationToken ct)
    {
        var checks = new Checks()
            .Range("pallets", r.Pallets, 1, 30)
            .Range("weight", r.Weight, 1, 48_000)
            .Range("rate", r.Rate, 1, 100_000);
        if (r.CarrierCost is { } cost) checks.Range("carrierCost", cost, 0, 100_000);
        if (!await db.Customers.AnyAsync(c => c.Id == r.CustomerId, ct)) checks.Add("customerId", "Unknown customer.");
        if (r.LaneId is { } laneId && !await db.Lanes.AnyAsync(l => l.Id == laneId && l.CustomerId == r.CustomerId, ct))
            checks.Add("laneId", "Lane does not belong to this customer.");
        if (r.CarrierId is { } carrierId && !await db.Carriers.AnyAsync(c => c.Id == carrierId && c.Status == CarrierStatuses.Active, ct))
            checks.Add("carrierId", "Choose an active carrier.");
        if (r.CarrierCost is not null && r.CarrierId is null) checks.Add("carrierId", "Choose the carrier this cost is from.");
        await AccountEndpoints.CheckRep(db, checks, "repId", r.RepId, ct);
        if (r.LaneId is null)
        {
            checks.Required("origin", r.Origin, Limits.Place).Required("destination", r.Destination, Limits.Place)
                .OneOf("equipment", r.Equipment, EquipmentTypes.All);
        }
        return checks;
    }

    private static async Task Apply(Quote q, QuoteRequest r, FreightDbContext db, CancellationToken ct)
    {
        var lane = r.LaneId is { } laneId ? await db.Lanes.FindAsync([laneId], ct) : null;
        q.CustomerId = r.CustomerId;
        q.LaneId = r.LaneId;
        q.Origin = Http.Clean(r.Origin) ?? lane!.Origin;
        q.Destination = Http.Clean(r.Destination) ?? lane!.Destination;
        q.Equipment = EquipmentTypes.All.Contains(r.Equipment) ? r.Equipment! : lane!.Equipment;
        q.Pallets = r.Pallets;
        q.Weight = r.Weight;
        q.Rate = r.Rate;
        q.CarrierId = r.CarrierId;
        q.CarrierCost = r.CarrierCost;
        q.RepId = r.RepId ?? await db.Customers.Where(c => c.Id == r.CustomerId).Select(c => c.OwnerId).SingleAsync(ct);
    }

    private static async Task<IResult> CreateQuote(QuoteRequest request, FreightDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var checks = await ValidateAsync(request, db, ct);
        if (!checks.Ok) return checks.Problem();

        var numbers = await db.Quotes.Select(q => q.Number).ToListAsync(ct);
        var next = numbers.Select(n => int.TryParse(n.AsSpan(2), out var v) ? v : 1000).DefaultIfEmpty(1000).Max() + 1;
        var quote = new Quote { Id = Guid.NewGuid(), Number = $"Q-{next}", CreatedAt = clock.GetUtcNow().UtcDateTime };
        await Apply(quote, request, db, ct);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/quotes/{quote.Id}", new { quote.Id, quote.Number });
    }

    private static async Task<IResult> UpdateQuote(Guid id, QuoteRequest request, FreightDbContext db, CancellationToken ct)
    {
        var quote = await db.Quotes.FindAsync([id], ct);
        if (quote is null) return Http.NotFound("Quote");
        if (quote.Status != QuoteStatuses.Draft) return Http.Conflict($"Only draft quotes can be edited; {quote.Number} is {quote.Status}.");
        var checks = await ValidateAsync(request, db, ct);
        if (!checks.Ok) return checks.Problem();
        await Apply(quote, request, db, ct);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Moves a quote through Draft → Sent → Won/Lost and logs it on the account timeline.</summary>
    private static async Task<IResult> Move(Guid id, string to, FreightDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var quote = await db.Quotes.Include(q => q.Customer).SingleOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null) return Http.NotFound("Quote");
        if (!CanMove(quote.Status, to))
        {
            var allowed = QuoteTransitions[quote.Status];
            return Http.Conflict(allowed.Length == 0
                ? $"{quote.Number} is {quote.Status} and can no longer change."
                : $"{quote.Number} is {quote.Status}; it can move to {string.Join(" or ", allowed)}.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        quote.Status = to;
        if (to == QuoteStatuses.Sent) quote.SentAt = now;
        else quote.ClosedAt = now;

        var (type, summary) = to switch
        {
            QuoteStatuses.Sent => (ActivityTypes.Email, $"Sent quote {quote.Number}: {quote.Origin} to {quote.Destination} at ${quote.Rate:N0}."),
            QuoteStatuses.Won => (ActivityTypes.Note, $"Quote {quote.Number} won at ${quote.Rate:N0}."),
            _ => (ActivityTypes.Note, $"Quote {quote.Number} lost.")
        };
        db.Activities.Add(AccountEndpoints.SystemActivity(quote.CustomerId, quote.RepId, type, summary, now));

        // A first win turns a prospect into an active account.
        if (to == QuoteStatuses.Won && quote.Customer!.Stage == Stages.Prospect)
        {
            quote.Customer.Stage = Stages.Active;
            quote.Customer.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }
}
