namespace Cuentitas.Core.Domain;

// Metas de ahorro (v0.3). Documentado en docs/MODELO_DE_DATOS.md § Metas.

/// <summary>Cómo se mide cuánto llevás juntado.</summary>
public enum GoalTracking
{
    /// <summary>Sumando los aportes que registrás a mano (la plata queda en tus cuentas).</summary>
    Contributions,

    /// <summary>Con el saldo de una cuenta dedicada (ej. "Dólares ahorro").</summary>
    LinkedAccount,
}

public class Goal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Clave de ícono (ADR-007).</summary>
    public string Icon { get; set; } = "star";
    public uint Color { get; set; }

    public Currency Currency { get; set; } = Currency.ARS;

    /// <summary>Monto objetivo en centavos, a valores de hoy (si se ajusta por inflación, crece con el tiempo).</summary>
    public long TargetAmount { get; set; }

    /// <summary>Fecha a la que querés llegar (opcional).</summary>
    public DateTime? TargetDate { get; set; }

    public GoalTracking Tracking { get; set; } = GoalTracking.Contributions;

    /// <summary>Sólo <see cref="GoalTracking.LinkedAccount"/>: cuenta (misma moneda que la meta).</summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Sólo metas en pesos: el objetivo sube con la inflación estimada (ADR-014).</summary>
    public bool AdjustForInflation { get; set; } = true;

    /// <summary>Aporte mensual que pensás hacer. Si es null se usa tu ahorro promedio.</summary>
    public long? PlannedMonthly { get; set; }

    public string Note { get; set; } = "";
    public DateTime? CompletedAt { get; set; }
    public bool Archived { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Un aporte (positivo) o retiro (negativo) a una meta medida por aportes.</summary>
public class GoalContribution
{
    public int Id { get; set; }
    public int GoalId { get; set; }

    /// <summary>Centavos en la moneda de la meta. Negativo = retiro.</summary>
    public long Amount { get; set; }
    public DateTime Date { get; set; }
    public string Note { get; set; } = "";
}
