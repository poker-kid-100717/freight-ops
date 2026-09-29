using Microsoft.EntityFrameworkCore;
using Portfolio.Freight.Api.Data;

namespace Portfolio.Freight.Api.Endpoints;

public sealed record CustomerRequest(
    string? Name, string? LaneOrigin, string? LaneDestination, string? Stage,
    int MonthlyLoads, decimal MonthlyRevenue, decimal MonthlyGrossMargin, Guid? OwnerId, string? Notes);

public sealed record ContactRequest(string? Name, string? Role, string? Email, string? Phone, bool IsPrimary);
public sealed record ContactDto(Guid Id, Guid CustomerId, string CustomerName, string Name, string? Role, string? Email, string? Phone, bool IsPrimary);

public sealed record ActivityRequest(string? Type, string? Summary, Guid? ContactId, Guid? RepId, DateTime? OccurredAt);
public sealed record ActivityDto(Guid Id, Guid CustomerId, string CustomerName, string Type, string Summary, DateTime OccurredAt,
    Guid? ContactId, string? ContactName, Guid? RepId, string? RepName);

public sealed record FollowUpRequest(DateOnly? DueOn, string? Description, Guid? RepId);
public sealed record FollowUpDto(Guid Id, Guid CustomerId, string CustomerName, DateOnly DueOn, string Description, string Status,
    DateTime? CompletedAt, Guid? RepId, string? RepName, bool Overdue);

public sealed record LaneRequest(string? Origin, string? Destination, string? Equipment, int EstimatedLoadsPerMonth, decimal TargetRate);
public sealed record LaneDto(Guid Id, Guid CustomerId, string CustomerName, string Origin, string Destination, string Equipment,
    int EstimatedLoadsPerMonth, decimal TargetRate, int QuotesWon);

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var customers = api.MapGroup("/customers").WithTags("Accounts");
        customers.MapGet("/", ListCustomers);
        customers.MapGet("/{id:guid}", GetCustomer);
        customers.MapPost("/", CreateCustomer);
        customers.MapPut("/{id:guid}", UpdateCustomer);
        customers.MapPost("/{id:guid}/contacts", AddContact);
        customers.MapPost("/{id:guid}/activities", LogActivity);
        customers.MapPost("/{id:guid}/follow-ups", AddFollowUp);
        customers.MapPost("/{id:guid}/lanes", AddLane);

        api.MapGet("/contacts", ListContacts).WithTags("Contacts");
        api.MapPut("/contacts/{id:guid}", UpdateContact).WithTags("Contacts");

        api.MapGet("/activities", ListActivities).WithTags("Activity");

        api.MapGet("/follow-ups", ListFollowUps).WithTags("Follow-ups");
        api.MapPost("/follow-ups/{id:guid}/complete", CompleteFollowUp).WithTags("Follow-ups");

        api.MapGet("/lanes", ListLanes).WithTags("Lanes");
        api.MapPut("/lanes/{id:guid}", UpdateLane).WithTags("Lanes");
        api.MapDelete("/lanes/{id:guid}", DeleteLane).WithTags("Lanes");
    }

    // ---------- customers ----------

    private static async Task<IResult> ListCustomers(FreightDbContext db, CustomerInsights insights,
        string? search, string? stage, Guid? owner, string? sort, int? page, int? pageSize, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking();
        if (Http.Clean(search) is { } term)
        {
            var t = term.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(t) || c.LaneOrigin.ToLower().Contains(t) || c.LaneDestination.ToLower().Contains(t));
        }
        if (Http.Clean(stage) is { } s) query = query.Where(c => c.Stage == s);
        if (owner is { } o) query = query.Where(c => c.OwnerId == o);

        var rows = await insights.SnapshotsAsync(query, ct);
        IEnumerable<CustomerSnapshot> sorted = sort switch
        {
            "name" => rows.OrderBy(x => x.Name),
            "loads" => rows.OrderByDescending(x => x.MonthlyLoads).ThenBy(x => x.Name),
            "margin" => rows.OrderByDescending(x => x.MonthlyGrossMargin).ThenBy(x => x.Name),
            "touch" => rows.OrderByDescending(x => x.DaysSinceTouch).ThenBy(x => x.Name),
            _ => rows.OrderByDescending(x => x.Score).ThenBy(x => x.Name)
        };
        return Results.Ok(sorted.ToPaged(page, pageSize));
    }

    private static async Task<IResult> GetCustomer(Guid id, FreightDbContext db, CustomerInsights insights, CancellationToken ct)
    {
        var snapshot = (await insights.SnapshotsAsync(db.Customers.AsNoTracking().Where(c => c.Id == id), ct)).SingleOrDefault();
        if (snapshot is null) return Http.NotFound("Customer");
        var today = insights.Today;

        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new { c.Notes, c.CreatedAt, c.UpdatedAt })
            .SingleAsync(ct);

        var contacts = await db.Contacts.AsNoTracking().Where(x => x.CustomerId == id)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name)
            .Select(x => new ContactDto(x.Id, x.CustomerId, snapshot.Name, x.Name, x.Role, x.Email, x.Phone, x.IsPrimary))
            .ToListAsync(ct);

        var activities = await ActivityQuery(db.Activities.Where(x => x.CustomerId == id)).Take(50).ToListAsync(ct);

        var followUps = await FollowUpQuery(db.FollowUps.Where(x => x.CustomerId == id)
                .OrderBy(x => x.Status == FollowUpStatuses.Done).ThenBy(x => x.DueOn).Take(50), today)
            .ToListAsync(ct);

        var quotes = await SalesEndpoints.QuoteQuery(db.Quotes.Where(x => x.CustomerId == id), db).Take(50).ToListAsync(ct);
        var lanes = await LaneQuery(db.Lanes.Where(x => x.CustomerId == id), db).ToListAsync(ct);

        return Results.Ok(new
        {
            customer = snapshot,
            customer.Notes,
            customer.CreatedAt,
            customer.UpdatedAt,
            contacts,
            activities,
            followUps,
            quotes,
            lanes
        });
    }

    private static Checks Validate(CustomerRequest r) => new Checks()
        .Required("name", r.Name, Limits.Name)
        .Required("laneOrigin", r.LaneOrigin, Limits.Place)
        .Required("laneDestination", r.LaneDestination, Limits.Place)
        .OneOf("stage", r.Stage, Stages.All)
        .Range("monthlyLoads", r.MonthlyLoads, 0, 10_000)
        .Range("monthlyRevenue", r.MonthlyRevenue, 0, 100_000_000)
        .Range("monthlyGrossMargin", r.MonthlyGrossMargin, 0, 100_000_000)
        .Optional("notes", r.Notes, Limits.Notes);

    private static async Task<IResult> CreateCustomer(CustomerRequest request, FreightDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var checks = Validate(request);
        await CheckRep(db, checks, "ownerId", request.OwnerId, ct);
        if (!checks.Ok) return checks.Problem();
        var name = request.Name!.Trim();
        if (await db.Customers.AnyAsync(c => c.Name == name, ct)) return Http.Conflict($"A customer named \"{name}\" already exists.");

        var now = clock.GetUtcNow().UtcDateTime;
        var customer = new Customer { Id = Guid.NewGuid(), CreatedAt = now };
        Apply(customer, request, now);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/customers/{customer.Id}", new { customer.Id });
    }

    private static async Task<IResult> UpdateCustomer(Guid id, CustomerRequest request, FreightDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var checks = Validate(request);
        await CheckRep(db, checks, "ownerId", request.OwnerId, ct);
        if (!checks.Ok) return checks.Problem();
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return Http.NotFound("Customer");
        var name = request.Name!.Trim();
        if (await db.Customers.AnyAsync(c => c.Name == name && c.Id != id, ct)) return Http.Conflict($"A customer named \"{name}\" already exists.");

        Apply(customer, request, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static void Apply(Customer c, CustomerRequest r, DateTime now)
    {
        c.Name = r.Name!.Trim();
        c.LaneOrigin = r.LaneOrigin!.Trim();
        c.LaneDestination = r.LaneDestination!.Trim();
        c.Stage = r.Stage!;
        c.MonthlyLoads = r.MonthlyLoads;
        c.MonthlyRevenue = r.MonthlyRevenue;
        c.MonthlyGrossMargin = r.MonthlyGrossMargin;
        c.OwnerId = r.OwnerId;
        c.Notes = Http.Clean(r.Notes);
        c.UpdatedAt = now;
    }

    internal static async Task CheckRep(FreightDbContext db, Checks checks, string field, Guid? repId, CancellationToken ct)
    {
        if (repId is { } id && !await db.Reps.AnyAsync(r => r.Id == id, ct)) checks.Add(field, "Unknown rep.");
    }

    // ---------- contacts ----------

    private static Checks Validate(ContactRequest r) => new Checks()
        .Required("name", r.Name, Limits.Name)
        .Optional("role", r.Role, Limits.Name)
        .Email("email", r.Email)
        .Optional("phone", r.Phone, 40);

    private static async Task<IResult> ListContacts(FreightDbContext db, string? search, Guid? customerId, int? page, int? pageSize, CancellationToken ct)
    {
        var query = db.Contacts.AsNoTracking();
        if (Http.Clean(search) is { } term)
        {
            var t = term.ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(t) || (x.Email != null && x.Email.ToLower().Contains(t)) || x.Customer!.Name.ToLower().Contains(t));
        }
        if (customerId is { } cid) query = query.Where(x => x.CustomerId == cid);
        return Results.Ok(await query.OrderBy(x => x.Customer!.Name).ThenByDescending(x => x.IsPrimary).ThenBy(x => x.Name)
            .Select(x => new ContactDto(x.Id, x.CustomerId, x.Customer!.Name, x.Name, x.Role, x.Email, x.Phone, x.IsPrimary))
            .ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> AddContact(Guid id, ContactRequest request, FreightDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        if (!await db.Customers.AnyAsync(c => c.Id == id, ct)) return Http.NotFound("Customer");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var first = !await db.Contacts.AnyAsync(x => x.CustomerId == id, ct);
        var contact = new Contact { Id = Guid.NewGuid(), CustomerId = id };
        ApplyContact(contact, request);
        contact.IsPrimary = request.IsPrimary || first;
        if (contact.IsPrimary) await ClearPrimary(db, id, ct);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Created($"/api/contacts/{contact.Id}", new { contact.Id });
    }

    private static async Task<IResult> UpdateContact(Guid id, ContactRequest request, FreightDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var contact = await db.Contacts.FindAsync([id], ct);
        if (contact is null) return Http.NotFound("Contact");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        ApplyContact(contact, request);
        if (request.IsPrimary && !contact.IsPrimary)
        {
            await ClearPrimary(db, contact.CustomerId, ct);
            contact.IsPrimary = true;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static void ApplyContact(Contact c, ContactRequest r)
    {
        c.Name = r.Name!.Trim();
        c.Role = Http.Clean(r.Role);
        c.Email = Http.Clean(r.Email);
        c.Phone = Http.Clean(r.Phone);
    }

    private static Task ClearPrimary(FreightDbContext db, Guid customerId, CancellationToken ct) =>
        db.Contacts.Where(x => x.CustomerId == customerId && x.IsPrimary)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPrimary, false), ct);

    // ---------- activities ----------

    // Filters are applied to entities before projecting: EF cannot translate predicates on record constructors.
    internal static IQueryable<ActivityDto> ActivityQuery(IQueryable<Activity> source) =>
        source.AsNoTracking()
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => new ActivityDto(a.Id, a.CustomerId, a.Customer!.Name, a.Type, a.Summary, a.OccurredAt,
                a.ContactId, a.Contact != null ? a.Contact.Name : null, a.RepId, a.Rep != null ? a.Rep.Name : null));

    private static async Task<IResult> ListActivities(FreightDbContext db, Guid? rep, string? type, Guid? customerId,
        DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct)
    {
        IQueryable<Activity> query = db.Activities;
        if (rep is { } r) query = query.Where(a => a.RepId == r);
        if (Http.Clean(type) is { } t) query = query.Where(a => a.Type == t);
        if (customerId is { } c) query = query.Where(a => a.CustomerId == c);
        if (from is { } f) { var start = f.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(a => a.OccurredAt >= start); }
        if (to is { } e) { var end = e.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(a => a.OccurredAt < end); }
        return Results.Ok(await ActivityQuery(query).ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> LogActivity(Guid id, ActivityRequest request, FreightDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var checks = new Checks().OneOf("type", request.Type, ActivityTypes.All).Required("summary", request.Summary, Limits.Notes);
        var occurredAt = request.OccurredAt?.ToUniversalTime() ?? now;
        if (occurredAt > now.AddMinutes(5)) checks.Add("occurredAt", "Cannot be in the future.");
        await CheckRep(db, checks, "repId", request.RepId, ct);
        if (request.ContactId is { } contactId && !await db.Contacts.AnyAsync(x => x.Id == contactId && x.CustomerId == id, ct))
            checks.Add("contactId", "Contact does not belong to this customer.");
        if (!checks.Ok) return checks.Problem();

        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return Http.NotFound("Customer");

        var activity = new Activity
        {
            Id = Guid.NewGuid(), CustomerId = id, ContactId = request.ContactId, RepId = request.RepId ?? customer.OwnerId,
            Type = request.Type!, Summary = request.Summary!.Trim(), OccurredAt = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc)
        };
        db.Activities.Add(activity);
        customer.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/activities/{activity.Id}", new { activity.Id });
    }

    internal static Activity SystemActivity(Guid customerId, Guid? repId, string type, string summary, DateTime at) =>
        new() { Id = Guid.NewGuid(), CustomerId = customerId, RepId = repId, Type = type, Summary = summary, OccurredAt = at };

    // ---------- follow-ups ----------

    internal static IQueryable<FollowUpDto> FollowUpQuery(IQueryable<FollowUp> source, DateOnly today) =>
        source.AsNoTracking()
            .Select(f => new FollowUpDto(f.Id, f.CustomerId, f.Customer!.Name, f.DueOn, f.Description, f.Status, f.CompletedAt,
                f.RepId, f.Rep != null ? f.Rep.Name : null, f.Status == FollowUpStatuses.Open && f.DueOn < today));

    private static async Task<IResult> ListFollowUps(FreightDbContext db, CustomerInsights insights, Guid? rep, string? status, string? due,
        int? page, int? pageSize, CancellationToken ct)
    {
        var today = insights.Today;
        IQueryable<FollowUp> query = db.FollowUps;
        if (rep is { } r) query = query.Where(f => f.RepId == r);
        if (Http.Clean(status) is { } s) query = query.Where(f => f.Status == s);
        query = due switch
        {
            "overdue" => query.Where(f => f.Status == FollowUpStatuses.Open && f.DueOn < today),
            "today" => query.Where(f => f.Status == FollowUpStatuses.Open && f.DueOn == today),
            "upcoming" => query.Where(f => f.Status == FollowUpStatuses.Open && f.DueOn > today),
            _ => query
        };
        return Results.Ok(await FollowUpQuery(query.OrderBy(f => f.DueOn).ThenBy(f => f.Customer!.Name), today).ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> AddFollowUp(Guid id, FollowUpRequest request, FreightDbContext db, CancellationToken ct)
    {
        var checks = new Checks().Required("description", request.Description, Limits.Notes);
        if (request.DueOn is null) checks.Add("dueOn", "Required.");
        await CheckRep(db, checks, "repId", request.RepId, ct);
        if (!checks.Ok) return checks.Problem();
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return Http.NotFound("Customer");

        var followUp = new FollowUp
        {
            Id = Guid.NewGuid(), CustomerId = id, DueOn = request.DueOn!.Value, Description = request.Description!.Trim(),
            RepId = request.RepId ?? customer.OwnerId
        };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/follow-ups/{followUp.Id}", new { followUp.Id });
    }

    private static async Task<IResult> CompleteFollowUp(Guid id, FreightDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var followUp = await db.FollowUps.FindAsync([id], ct);
        if (followUp is null) return Http.NotFound("Follow-up");
        if (followUp.Status == FollowUpStatuses.Done) return Http.Conflict("This follow-up is already done.");
        followUp.Status = FollowUpStatuses.Done;
        followUp.CompletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------- lanes ----------

    internal static IQueryable<LaneDto> LaneQuery(IQueryable<Lane> source, FreightDbContext db) =>
        source.AsNoTracking()
            .OrderBy(l => l.Customer!.Name).ThenBy(l => l.Origin)
            .Select(l => new LaneDto(l.Id, l.CustomerId, l.Customer!.Name, l.Origin, l.Destination, l.Equipment,
                l.EstimatedLoadsPerMonth, l.TargetRate, db.Quotes.Count(q => q.LaneId == l.Id && q.Status == QuoteStatuses.Won)));

    private static Checks Validate(LaneRequest r) => new Checks()
        .Required("origin", r.Origin, Limits.Place)
        .Required("destination", r.Destination, Limits.Place)
        .OneOf("equipment", r.Equipment, EquipmentTypes.All)
        .Range("estimatedLoadsPerMonth", r.EstimatedLoadsPerMonth, 0, 10_000)
        .Range("targetRate", r.TargetRate, 0, 1_000_000);

    private static async Task<IResult> ListLanes(FreightDbContext db, Guid? customerId, string? equipment, string? search, int? page, int? pageSize, CancellationToken ct)
    {
        IQueryable<Lane> query = db.Lanes;
        if (customerId is { } c) query = query.Where(l => l.CustomerId == c);
        if (Http.Clean(equipment) is { } e) query = query.Where(l => l.Equipment == e);
        if (Http.Clean(search) is { } term)
        {
            var t = term.ToLower();
            query = query.Where(l => l.Origin.ToLower().Contains(t) || l.Destination.ToLower().Contains(t) || l.Customer!.Name.ToLower().Contains(t));
        }
        return Results.Ok(await LaneQuery(query, db).ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> AddLane(Guid id, LaneRequest request, FreightDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        if (!await db.Customers.AnyAsync(c => c.Id == id, ct)) return Http.NotFound("Customer");
        var lane = new Lane { Id = Guid.NewGuid(), CustomerId = id };
        ApplyLane(lane, request);
        db.Lanes.Add(lane);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/lanes/{lane.Id}", new { lane.Id });
    }

    private static async Task<IResult> UpdateLane(Guid id, LaneRequest request, FreightDbContext db, CancellationToken ct)
    {
        var checks = Validate(request);
        if (!checks.Ok) return checks.Problem();
        var lane = await db.Lanes.FindAsync([id], ct);
        if (lane is null) return Http.NotFound("Lane");
        ApplyLane(lane, request);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteLane(Guid id, FreightDbContext db, CancellationToken ct)
    {
        var deleted = await db.Lanes.Where(l => l.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Http.NotFound("Lane") : Results.NoContent();
    }

    private static void ApplyLane(Lane l, LaneRequest r)
    {
        l.Origin = r.Origin!.Trim();
        l.Destination = r.Destination!.Trim();
        l.Equipment = r.Equipment!;
        l.EstimatedLoadsPerMonth = r.EstimatedLoadsPerMonth;
        l.TargetRate = r.TargetRate;
    }
}
