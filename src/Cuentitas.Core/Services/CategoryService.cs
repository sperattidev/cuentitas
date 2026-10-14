using Cuentitas.Core.Data;
using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Services;

public sealed class CategoryService(IDbContextFactory<CuentitasDbContext> factory, DataChanges changes)
{
    public async Task<List<Category>> GetAsync(CategoryKind? kind = null, bool includeArchived = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Categories.AsNoTracking()
            .Where(c => kind == null || c.Kind == kind)
            .Where(c => includeArchived || !c.Archived)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<int> CreateAsync(Category category)
    {
        Validate(category);
        await using var db = await factory.CreateDbContextAsync();
        category.Id = 0;
        category.Name = category.Name.Trim();
        if (category.Kind == CategoryKind.Income) category.DefaultNecessity = null;
        category.SortOrder = (await db.Categories.MaxAsync(c => (int?)c.SortOrder) ?? 0) + 1;
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        changes.Notify();
        return category.Id;
    }

    /// <summary>Actualiza nombre, ícono, color y necesidad. El tipo (gasto/ingreso) no cambia.</summary>
    public async Task UpdateAsync(Category category)
    {
        Validate(category);
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.Categories.FindAsync(category.Id)
                       ?? throw new InvalidOperationException("La categoría no existe.");
        existing.Name = category.Name.Trim();
        existing.Icon = category.Icon;
        existing.Color = category.Color;
        existing.DefaultNecessity = existing.Kind == CategoryKind.Expense ? category.DefaultNecessity : null;
        existing.Archived = category.Archived;
        await db.SaveChangesAsync();
        changes.Notify();
    }

    public async Task SetArchivedAsync(int id, bool archived)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Categories.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Archived, archived));
        changes.Notify();
    }

    private static void Validate(Category c)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("La categoría necesita un nombre.");
        if (c.Name.Trim().Length > 40) throw new ArgumentException("El nombre es demasiado largo (máx. 40).");
    }
}
