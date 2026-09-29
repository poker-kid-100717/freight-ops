using Microsoft.EntityFrameworkCore;

namespace Portfolio.Freight.Api.Data;

/// <summary>
/// Fictional demo data, dated relative to "now" so the dashboard always looks current.
/// Every name, number and lane here is invented for the portfolio.
/// </summary>
public sealed class DemoSeeder(TimeProvider clock)
{
    public async Task ResetAsync(FreightDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Activities.ExecuteDeleteAsync(ct);
        await db.FollowUps.ExecuteDeleteAsync(ct);
        await db.Quotes.ExecuteDeleteAsync(ct);
        await db.Lanes.ExecuteDeleteAsync(ct);
        await db.Contacts.ExecuteDeleteAsync(ct);
        await db.Leads.ExecuteDeleteAsync(ct);
        await db.Customers.ExecuteDeleteAsync(ct);
        await db.Carriers.ExecuteDeleteAsync(ct);
        await db.Reps.ExecuteDeleteAsync(ct);
        await SeedCoreAsync(db, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SeedAsync(FreightDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SeedCoreAsync(db, ct);
        await tx.CommitAsync(ct);
    }

    private async Task SeedCoreAsync(FreightDbContext db, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        DateTime Ago(int days, int hour = 15) => now.Date.AddDays(-days).AddHours(hour);

        var avery = new Rep { Id = Id(1), Name = "Avery Brooks", Email = "avery@example.com", Title = "Account Executive" };
        var jordan = new Rep { Id = Id(2), Name = "Jordan Reyes", Email = "jordan@example.com", Title = "Senior Account Executive" };
        var casey = new Rep { Id = Id(3), Name = "Casey Nguyen", Email = "casey@example.com", Title = "Sales Manager" };
        db.Reps.AddRange(avery, jordan, casey);

        Customer C(int n, string name, string from, string to, string stage, int loads, decimal revenue, decimal margin, Rep owner, int createdDaysAgo) =>
            new()
            {
                Id = Id(100 + n), Name = name, LaneOrigin = from, LaneDestination = to, Stage = stage,
                MonthlyLoads = loads, MonthlyRevenue = revenue, MonthlyGrossMargin = margin, OwnerId = owner.Id,
                CreatedAt = Ago(createdDaysAgo), UpdatedAt = Ago(Math.Min(createdDaysAgo, 5))
            };

        var customers = new[]
        {
            C(1, "Mesa Solar Components", "Albuquerque, NM", "Phoenix, AZ", Stages.Expansion, 28, 118_000m, 18_400m, jordan, 420),
            C(2, "High Desert Foods", "Santa Fe, NM", "Denver, CO", Stages.Active, 19, 91_000m, 13_900m, avery, 380),
            C(3, "Canyon Packaging", "El Paso, TX", "Dallas, TX", Stages.AtRisk, 34, 142_000m, 21_300m, jordan, 610),
            C(4, "Rio Valley Medical Supply", "Las Cruces, NM", "Tucson, AZ", Stages.Growth, 13, 76_000m, 11_200m, avery, 240),
            C(5, "Sandstone Home Goods", "Denver, CO", "Albuquerque, NM", Stages.Reactivation, 11, 64_000m, 8_900m, casey, 700),
            C(6, "Juniper Craft Beverages", "Flagstaff, AZ", "Las Vegas, NV", Stages.Active, 16, 72_500m, 10_100m, avery, 300),
            C(7, "Red Mesa Building Supply", "Amarillo, TX", "Oklahoma City, OK", Stages.Growth, 22, 98_000m, 14_600m, jordan, 190),
            C(8, "Pinon Pet Nutrition", "Albuquerque, NM", "Salt Lake City, UT", Stages.Active, 9, 47_000m, 7_300m, casey, 150),
            C(9, "Turquoise Trail Apparel", "Santa Fe, NM", "Los Angeles, CA", Stages.Prospect, 0, 0m, 0m, avery, 20),
            C(10, "Big Sky Ag Equipment", "Lubbock, TX", "Wichita, KS", Stages.Inactive, 0, 0m, 0m, casey, 900),
            C(11, "Chaco Industrial Coatings", "Farmington, NM", "Denver, CO", Stages.Expansion, 17, 83_000m, 12_900m, jordan, 260),
            C(12, "Sonoran Fresh Produce", "Nogales, AZ", "Phoenix, AZ", Stages.AtRisk, 25, 104_000m, 15_200m, casey, 480)
        };
        db.Customers.AddRange(customers);

        string[] firstNames = ["Morgan", "Riley", "Taylor", "Jamie", "Drew", "Quinn", "Parker", "Rowan", "Skyler", "Emerson", "Hayden", "Reese"];
        string[] lastNames = ["Patel", "Garcia", "Kim", "Okafor", "Silva", "Novak", "Haddad", "Larsen", "Moreno", "Chen", "Ibrahim", "Walsh"];
        string[] roles = ["Logistics Manager", "Shipping Supervisor", "Procurement Lead", "Operations Director"];
        var contacts = new List<Contact>();
        for (var i = 0; i < customers.Length; i++)
        {
            var slug = customers[i].Name.Split(' ')[0].ToLowerInvariant();
            contacts.Add(new Contact
            {
                Id = Id(200 + i * 2), CustomerId = customers[i].Id, IsPrimary = true,
                Name = $"{firstNames[i]} {lastNames[i]}", Role = roles[i % roles.Length],
                Email = $"{firstNames[i].ToLowerInvariant()}@{slug}.example.com", Phone = $"555-01{i:00}"
            });
            if (i % 2 == 0)
            {
                var j = (i + 5) % firstNames.Length;
                contacts.Add(new Contact
                {
                    Id = Id(201 + i * 2), CustomerId = customers[i].Id,
                    Name = $"{firstNames[j]} {lastNames[(i + 3) % lastNames.Length]}", Role = "Accounts Payable",
                    Email = $"ap@{slug}.example.com", Phone = $"555-02{i:00}"
                });
            }
        }
        db.Contacts.AddRange(contacts);
        Contact PrimaryOf(Customer c) => contacts.First(x => x.CustomerId == c.Id && x.IsPrimary);

        // Days since last touch per customer drive the opportunity scores.
        int[] lastTouch = [9, 3, 12, 6, 15, 2, 4, 8, 1, 45, 5, 11];
        string[] summaries =
        [
            "Reviewed upcoming volume and confirmed pickup windows.",
            "Sent updated lane pricing for the next quarter.",
            "Discussed a service issue on a late delivery; agreed on a recovery plan.",
            "Quarterly business review: on-time performance and claims.",
            "Introduced reefer capacity for the summer season.",
            "Left voicemail about a new backhaul lane."
        ];
        var activities = new List<Activity>();
        var n = 0;
        for (var i = 0; i < customers.Length; i++)
        {
            for (var k = 0; k < 3; k++)
            {
                var type = ActivityTypes.All[(i + k) % ActivityTypes.All.Length];
                activities.Add(new Activity
                {
                    Id = Id(1000 + n++), CustomerId = customers[i].Id, ContactId = PrimaryOf(customers[i]).Id,
                    RepId = customers[i].OwnerId, Type = type, Summary = summaries[(i + k) % summaries.Length],
                    OccurredAt = Ago(lastTouch[i] + k * 9, 9 + (i + k) % 7)
                });
            }
        }
        db.Activities.AddRange(activities);

        db.FollowUps.AddRange(
            new FollowUp { Id = Id(300), CustomerId = customers[2].Id, RepId = jordan.Id, DueOn = today.AddDays(-2), Description = "Service-recovery call about the late Dallas delivery." },
            new FollowUp { Id = Id(301), CustomerId = customers[0].Id, RepId = jordan.Id, DueOn = today, Description = "Send pricing for the Phoenix to Albuquerque backhaul." },
            new FollowUp { Id = Id(302), CustomerId = customers[4].Id, RepId = casey.Id, DueOn = today.AddDays(-1), Description = "Re-open account with a capacity update." },
            new FollowUp { Id = Id(303), CustomerId = customers[3].Id, RepId = avery.Id, DueOn = today.AddDays(2), Description = "Confirm Q3 volume forecast." },
            new FollowUp { Id = Id(304), CustomerId = customers[8].Id, RepId = avery.Id, DueOn = today, Description = "Intro call with the logistics manager." },
            new FollowUp { Id = Id(305), CustomerId = customers[11].Id, RepId = casey.Id, DueOn = today.AddDays(-3), Description = "Review claims history before renewal." },
            new FollowUp { Id = Id(306), CustomerId = customers[6].Id, RepId = jordan.Id, DueOn = today.AddDays(5), Description = "Ask for adjacent lanes into Tulsa." },
            new FollowUp { Id = Id(307), CustomerId = customers[1].Id, RepId = avery.Id, DueOn = today.AddDays(-6), Description = "Send monthly scorecard.", Status = FollowUpStatuses.Done, CompletedAt = Ago(6) });

        var lanes = new List<Lane>();
        for (var i = 0; i < customers.Length; i++)
        {
            if (customers[i].Stage == Stages.Inactive) continue;
            lanes.Add(new Lane
            {
                Id = Id(400 + i), CustomerId = customers[i].Id, Origin = customers[i].LaneOrigin, Destination = customers[i].LaneDestination,
                Equipment = i is 1 or 11 ? EquipmentTypes.Reefer : i == 6 ? EquipmentTypes.Flatbed : EquipmentTypes.DryVan,
                EstimatedLoadsPerMonth = Math.Max(customers[i].MonthlyLoads, 4), TargetRate = 1_400m + i * 115m
            });
        }
        lanes.Add(new Lane { Id = Id(450), CustomerId = customers[0].Id, Origin = "Phoenix, AZ", Destination = "Albuquerque, NM", Equipment = EquipmentTypes.DryVan, EstimatedLoadsPerMonth = 8, TargetRate = 1_250m });
        db.Lanes.AddRange(lanes);

        var carriers = new[]
        {
            new Carrier { Id = Id(500), Name = "Roadrunner Line Haul", McNumber = "MC-900101", EquipmentTypes = "Dry Van", HomeRegion = "New Mexico", Rating = 5 },
            new Carrier { Id = Id(501), Name = "Coldchain Coyote Transport", McNumber = "MC-900102", EquipmentTypes = "Reefer", HomeRegion = "Arizona", Rating = 4 },
            new Carrier { Id = Id(502), Name = "Llano Flatbed Co.", McNumber = "MC-900103", EquipmentTypes = "Flatbed", HomeRegion = "West Texas", Rating = 4 },
            new Carrier { Id = Id(503), Name = "Four Corners Freight", McNumber = "MC-900104", EquipmentTypes = "Dry Van, Reefer", HomeRegion = "Four Corners", Rating = 3 },
            new Carrier { Id = Id(504), Name = "Rio Grande Express", McNumber = "MC-900105", EquipmentTypes = "Dry Van", HomeRegion = "El Paso", Rating = 4 },
            new Carrier { Id = Id(505), Name = "Front Range Haulers", McNumber = "MC-900106", EquipmentTypes = "Dry Van, Flatbed", HomeRegion = "Colorado", Rating = 5 },
            new Carrier { Id = Id(506), Name = "Saguaro Reefer Lines", McNumber = "MC-900107", EquipmentTypes = "Reefer", HomeRegion = "Arizona", Rating = 2, Status = CarrierStatuses.Inactive, Notes = "Paused after two late deliveries." },
            new Carrier { Id = Id(507), Name = "Panhandle Logistics", McNumber = "MC-900108", EquipmentTypes = "Dry Van, Flatbed", HomeRegion = "Texas Panhandle", Rating = 3 }
        };
        db.Carriers.AddRange(carriers);

        Quote Q(int number, int customer, string status, decimal rate, decimal? cost, int carrier, int createdDaysAgo, int? closedDaysAgo = null)
        {
            var lane = lanes.First(l => l.CustomerId == customers[customer].Id);
            return new Quote
            {
                Id = Id(600 + number), Number = $"Q-{1000 + number}", CustomerId = customers[customer].Id, LaneId = lane.Id,
                Origin = lane.Origin, Destination = lane.Destination, Equipment = lane.Equipment,
                Pallets = 8 + number % 14, Weight = 6_000 + number * 700, Rate = rate, CarrierCost = cost,
                CarrierId = cost is null ? null : carriers[carrier].Id, RepId = customers[customer].OwnerId, Status = status,
                CreatedAt = Ago(createdDaysAgo),
                SentAt = status == QuoteStatuses.Draft ? null : Ago(createdDaysAgo - 1),
                ClosedAt = closedDaysAgo is null ? null : Ago(closedDaysAgo.Value)
            };
        }

        db.Quotes.AddRange(
            Q(1, 0, QuoteStatuses.Won, 1_650m, 1_380m, 0, 70, 64),
            Q(2, 2, QuoteStatuses.Won, 1_980m, 1_700m, 4, 48, 41),
            Q(3, 1, QuoteStatuses.Lost, 2_300m, 2_050m, 1, 40, 33),
            Q(4, 3, QuoteStatuses.Won, 1_720m, 1_450m, 0, 26, 20),
            Q(5, 6, QuoteStatuses.Won, 2_150m, 1_800m, 2, 12, 6),
            Q(6, 10, QuoteStatuses.Won, 1_890m, 1_560m, 5, 9, 3),
            Q(7, 5, QuoteStatuses.Sent, 1_540m, 1_300m, 3, 5),
            Q(8, 11, QuoteStatuses.Sent, 2_480m, 2_150m, 1, 4),
            Q(9, 7, QuoteStatuses.Sent, 1_760m, null, 0, 3),
            Q(10, 4, QuoteStatuses.Draft, 1_600m, null, 0, 2),
            Q(11, 8, QuoteStatuses.Draft, 2_950m, null, 0, 1),
            Q(12, 0, QuoteStatuses.Lost, 1_700m, 1_500m, 0, 90, 84));

        db.Leads.AddRange(
            new Lead { Id = Id(700), Company = "Zia Outdoor Gear", ContactName = "Harper Lin", Email = "harper@zia.example.com", Phone = "555-0301", Source = LeadSources.Inbound, Status = LeadStatuses.New, OwnerId = avery.Id, CreatedAt = Ago(1) },
            new Lead { Id = Id(701), Company = "Gila Copper Works", ContactName = "Sage Ortiz", Email = "sage@gila.example.com", Phone = "555-0302", Source = LeadSources.TradeShow, Status = LeadStatuses.Working, OwnerId = jordan.Id, CreatedAt = Ago(6), Notes = "Met at a regional shippers expo; ships flatbed out of Silver City." },
            new Lead { Id = Id(702), Company = "Bosque Bakery Supply", ContactName = "Kai Romero", Email = "kai@bosque.example.com", Phone = "555-0303", Source = LeadSources.Referral, Status = LeadStatuses.Qualified, OwnerId = avery.Id, CreatedAt = Ago(10), Notes = "Referred by High Desert Foods. Needs reefer to Denver twice a week." },
            new Lead { Id = Id(703), Company = "Cimarron Tile", ContactName = "Blake Duran", Email = "blake@cimarron.example.com", Phone = "555-0304", Source = LeadSources.ColdOutreach, Status = LeadStatuses.Working, OwnerId = casey.Id, CreatedAt = Ago(14) },
            new Lead { Id = Id(704), Company = "Hatch Chile Co-op", ContactName = "Devon Baca", Email = "devon@hatch.example.com", Phone = "555-0305", Source = LeadSources.Referral, Status = LeadStatuses.Qualified, OwnerId = jordan.Id, CreatedAt = Ago(18), Notes = "Seasonal reefer volume August through October." },
            new Lead { Id = Id(705), Company = "Mogollon Timber", ContactName = "Ari Stone", Email = "ari@mogollon.example.com", Phone = "555-0306", Source = LeadSources.ColdOutreach, Status = LeadStatuses.Disqualified, OwnerId = casey.Id, CreatedAt = Ago(30), Notes = "Uses an asset carrier under contract through next year." },
            new Lead { Id = Id(706), Company = "Organ Mountain Robotics", ContactName = "Lane Whitaker", Email = "lane@organ.example.com", Phone = "555-0307", Source = LeadSources.Inbound, Status = LeadStatuses.New, OwnerId = null, CreatedAt = Ago(0) });

        await db.SaveChangesAsync(ct);
    }

    // Stable ids keep seeded links (for example /accounts/{id}) the same after every reset.
    private static Guid Id(int n) => new($"00000000-0000-4000-8000-{n:000000000000}");
}
