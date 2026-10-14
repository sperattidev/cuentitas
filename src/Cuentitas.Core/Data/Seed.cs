using Cuentitas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cuentitas.Core.Data;

/// <summary>
/// Datos iniciales pensados para Argentina. Se cargan una sola vez (marca "seeded.v1" en Settings).
/// Los íconos son claves del catálogo de la app (Cuentitas.App/Helpers/AppIcons.cs).
/// </summary>
public static class Seed
{
    private const string SeededKey = "seeded.v1";

    public static async Task EnsureSeededAsync(CuentitasDbContext db)
    {
        if (await db.Settings.AnyAsync(s => s.Key == SeededKey)) return;

        db.Accounts.Add(new Account
        {
            Name = "Efectivo", Type = AccountType.Cash, Color = 0xFF3F8F5A, Icon = "cash", SortOrder = 0,
        });

        var order = 0;
        Category Expense(string name, string icon, uint color, Necessity n) => new()
        {
            Name = name, Kind = CategoryKind.Expense, Icon = icon, Color = color, DefaultNecessity = n, SortOrder = order++,
        };
        Category Income(string name, string icon, uint color) => new()
        {
            Name = name, Kind = CategoryKind.Income, Icon = icon, Color = color, SortOrder = order++,
        };
        const Necessity nec = Necessity.Necessary, opt = Necessity.Optional;

        db.Categories.AddRange(
            // Hogar y servicios
            Expense("Alquiler", "home", 0xFF2F6FB5, nec),
            Expense("Expensas", "building", 0xFF2F6FB5, nec),
            Expense("Luz", "bolt", 0xFFC9962B, nec),
            Expense("Gas", "flame", 0xFFD07A2E, nec),
            Expense("Agua", "drop", 0xFF2B8FA8, nec),
            Expense("Internet y cable", "wifi", 0xFF6B5BB5, nec),
            Expense("Celular", "phone", 0xFF56657A, nec),
            Expense("ABL e inmobiliario", "receipt", 0xFF56657A, nec),
            Expense("Mantenimiento del hogar", "tools", 0xFF6E7781, nec),
            // Comida
            Expense("Supermercado", "cart", 0xFF3F8F5A, nec),
            Expense("Almacén y verdulería", "leaf", 0xFF3F8F5A, nec),
            Expense("Comida afuera", "dining", 0xFFC2603E, opt),
            Expense("Delivery", "package", 0xFFC2603E, opt),
            Expense("Café y kiosco", "coffee", 0xFF8A6A55, opt),
            // Movilidad
            Expense("Transporte (SUBE)", "bus", 0xFF2F6FB5, nec),
            Expense("Combustible", "car", 0xFF56657A, nec),
            Expense("Peajes y estacionamiento", "parking", 0xFF56657A, nec),
            // Salud y educación
            Expense("Prepaga / obra social", "health", 0xFFB04A6E, nec),
            Expense("Farmacia", "medicine", 0xFF1F8A7A, nec),
            Expense("Educación", "education", 0xFF6B5BB5, nec),
            // Personales
            Expense("Ropa y calzado", "shirt", 0xFFB04A6E, opt),
            Expense("Salidas", "people", 0xFFC2603E, opt),
            Expense("Streaming y suscripciones", "tv", 0xFFC2603E, opt),
            Expense("Regalos", "gift", 0xFFB04A6E, opt),
            Expense("Mascotas", "heart", 0xFF8A6A55, nec),
            Expense("Gimnasio y deporte", "sport", 0xFF3F8F5A, opt),
            // Finanzas
            Expense("Impuestos (monotributo, ganancias)", "document", 0xFF56657A, nec),
            Expense("Comisiones bancarias", "bank", 0xFF56657A, nec),
            Expense("Cuotas y préstamos", "card", 0xFF6B5BB5, nec),
            Expense("Otros gastos", "other", 0xFF6E7781, opt),
            // Ingresos
            Income("Sueldo", "work", 0xFF1F8A7A),
            Income("Aguinaldo", "calendar", 0xFF1F8A7A),
            Income("Trabajos extra", "laptop", 0xFF2F6FB5),
            Income("Ventas", "tag", 0xFFD07A2E),
            Income("Intereses y plazo fijo", "growth", 0xFF3F8F5A),
            Income("Reintegros y devoluciones", "undo", 0xFF2B8FA8),
            Income("Otros ingresos", "add", 0xFF6E7781));

        db.Settings.Add(new AppSetting { Key = SeededKey, Value = DateTime.Now.ToString("O") });
        await db.SaveChangesAsync();
    }
}
