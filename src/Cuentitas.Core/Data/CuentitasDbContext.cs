using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Data;

/// <summary>Par clave/valor para preferencias y metadatos (ej. tema, "seeded").</summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class CuentitasDbContext(DbContextOptions<CuentitasDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<RecurringItem> RecurringItems => Set<RecurringItem>();
    public DbSet<RecurringSkip> RecurringSkips => Set<RecurringSkip>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalContribution> GoalContributions => Set<GoalContribution>();
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Currency).HasConversion<string>().HasMaxLength(3);
            e.Property(x => x.Color).HasConversion<long>();
            e.Property(x => x.Icon).HasMaxLength(40);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(40).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DefaultNecessity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Color).HasConversion<long>();
            e.Property(x => x.Icon).HasMaxLength(40);
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Necessity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DollarType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.ToTable(t => t.HasCheckConstraint("CK_Transaction_Amount", "Amount > 0"));

            // Restrict: no se puede borrar una cuenta/categoría con movimientos (se archiva).
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToAccount).WithMany().HasForeignKey(x => x.ToAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.InstallmentGroup).HasMaxLength(36);

            // Si se borra un pago recurrente, sus movimientos quedan (sin el vínculo).
            e.HasOne(x => x.RecurringItem).WithMany().HasForeignKey(x => x.RecurringItemId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.Date);
            e.HasIndex(x => x.AccountId);
            e.HasIndex(x => x.ToAccountId);
            e.HasIndex(x => new { x.RecurringItemId, x.DueDate });
            e.HasIndex(x => x.InstallmentGroup);
        });

        b.Entity<RecurringItem>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.ToTable(t => t.HasCheckConstraint("CK_Recurring_Amount", "EstimatedAmount >= 0"));
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Goal>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.Color).HasConversion<long>();
            e.Property(x => x.Currency).HasConversion<string>().HasMaxLength(3);
            e.Property(x => x.Tracking).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.ToTable(t => t.HasCheckConstraint("CK_Goal_Target", "TargetAmount > 0"));
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<GoalContribution>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => x.GoalId);
            e.HasOne<Goal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Budget>(e =>
        {
            e.HasIndex(x => x.CategoryId).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Budget_Limit", "MonthlyLimit > 0"));
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecurringSkip>(e =>
        {
            e.HasIndex(x => new { x.RecurringItemId, x.DueDate }).IsUnique();
            e.HasOne<RecurringItem>().WithMany().HasForeignKey(x => x.RecurringItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(60);
        });
    }
}
