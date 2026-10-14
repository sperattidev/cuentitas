namespace Cuentitas.Core.Domain;

/// <summary>Tope mensual de gasto (en pesos) para una categoría de gastos (v0.4). Uno por categoría.</summary>
public class Budget
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Centavos de pesos por mes.</summary>
    public long MonthlyLimit { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
