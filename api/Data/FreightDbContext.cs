using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Portfolio.Freight.Api.Data;

public sealed class FreightDbContext(DbContextOptions<FreightDbContext> options) : DbContext(options)
{
    public DbSet<Rep> Reps => Set<Rep>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Lane> Lanes => Set<Lane>();
    public DbSet<Carrier> Carriers => Set<Carrier>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Rep>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(120);
        });

        model.Entity<Customer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.Name);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.LaneOrigin).HasMaxLength(Limits.Place);
            e.Property(x => x.LaneDestination).HasMaxLength(Limits.Place);
            e.Property(x => x.Stage).HasMaxLength(30);
            e.HasIndex(x => x.Stage);
            e.Property(x => x.MonthlyRevenue).HasPrecision(14, 2);
            e.Property(x => x.MonthlyGrossMargin).HasPrecision(14, 2);
            e.Property(x => x.Notes).HasMaxLength(Limits.Notes);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Contact>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.Name);
            e.Property(x => x.Role).HasMaxLength(Limits.Name);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.HasOne(x => x.Customer).WithMany(x => x.Contacts).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Activity>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(20);
            e.Property(x => x.Summary).HasMaxLength(Limits.Notes);
            e.HasIndex(x => new { x.CustomerId, x.OccurredAt });
            e.HasIndex(x => x.OccurredAt);
            e.HasOne(x => x.Customer).WithMany(x => x.Activities).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Rep).WithMany().HasForeignKey(x => x.RepId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<FollowUp>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(Limits.Notes);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.DueOn });
            e.HasOne(x => x.Customer).WithMany(x => x.FollowUps).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Rep).WithMany().HasForeignKey(x => x.RepId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Quote>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Origin).HasMaxLength(Limits.Place);
            e.Property(x => x.Destination).HasMaxLength(Limits.Place);
            e.Property(x => x.Equipment).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Rate).HasPrecision(12, 2);
            e.Property(x => x.CarrierCost).HasPrecision(12, 2);
            e.HasOne(x => x.Customer).WithMany(x => x.Quotes).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Lane).WithMany().HasForeignKey(x => x.LaneId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Carrier).WithMany().HasForeignKey(x => x.CarrierId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Rep).WithMany().HasForeignKey(x => x.RepId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Lead>(e =>
        {
            e.Property(x => x.Company).HasMaxLength(Limits.Name);
            e.Property(x => x.ContactName).HasMaxLength(Limits.Name);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Source).HasMaxLength(30);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Notes).HasMaxLength(Limits.Notes);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Lane>(e =>
        {
            e.Property(x => x.Origin).HasMaxLength(Limits.Place);
            e.Property(x => x.Destination).HasMaxLength(Limits.Place);
            e.Property(x => x.Equipment).HasMaxLength(20);
            e.Property(x => x.TargetRate).HasPrecision(12, 2);
            e.HasOne(x => x.Customer).WithMany(x => x.Lanes).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Carrier>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.Name);
            e.Property(x => x.McNumber).HasMaxLength(20);
            e.HasIndex(x => x.McNumber).IsUnique();
            e.Property(x => x.EquipmentTypes).HasMaxLength(100);
            e.Property(x => x.HomeRegion).HasMaxLength(Limits.Place);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(Limits.Notes);
        });

        // SQLite (the no-database demo mode) cannot sort or aggregate decimals in SQL.
        // Store them as REAL there; PostgreSQL keeps exact numeric columns.
        if (Database.IsSqlite())
        {
            foreach (var property in model.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetProviderClrType(typeof(double));
            }
        }
    }
}

public static class Limits
{
    public const int Name = 120;
    public const int Place = 80;
    public const int Notes = 2000;
}

/// <summary>Used by `dotnet ef migrations add`; migrations target PostgreSQL.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<FreightDbContext>
{
    public FreightDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FreightDbContext>()
            .UseNpgsql("Host=localhost;Database=freight;Username=freight;Password=design-time")
            .Options);
}
