namespace Cuentitas.Core.Domain;

// Pagos y cobros recurrentes (v0.2). Documentado en docs/MODELO_DE_DATOS.md § Vencimientos.

/// <summary>Cada cuánto vence. El valor es la cantidad de meses entre vencimientos.</summary>
public enum Frequency
{
    Monthly = 1,
    Bimonthly = 2,
    Quarterly = 3,
    Semiannual = 6,
    Yearly = 12,
}

/// <summary>
/// Un pago (luz, expensas, prepaga…) o cobro (sueldo, aguinaldo…) que se repite.
/// No es un movimiento: cada vencimiento se "paga" creando un <see cref="Transaction"/>
/// con <see cref="Transaction.RecurringItemId"/> y <see cref="Transaction.DueDate"/>.
/// </summary>
public class RecurringItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Gasto (pago) o ingreso (cobro). Nunca transferencia.</summary>
    public TxType Type { get; set; } = TxType.Expense;

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Cuenta con la que se paga / en la que se cobra habitualmente.</summary>
    public int AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Monto estimado en centavos (en la moneda de la cuenta). Se puede corregir al pagar.</summary>
    public long EstimatedAmount { get; set; }

    /// <summary>El monto cambia cada vez (ej. luz): al pagar se pide confirmarlo.</summary>
    public bool VariableAmount { get; set; }

    public Frequency Frequency { get; set; } = Frequency.Monthly;

    /// <summary>Primer vencimiento. Define el mes de arranque y el día (si el mes es más corto, se usa el último día).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Opcional: después de esta fecha no hay más vencimientos (ej. un préstamo).</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Días de anticipación para el aviso.</summary>
    public int RemindDaysBefore { get; set; } = 3;

    public string Note { get; set; } = "";
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Un vencimiento omitido a propósito (ej. "este mes no pago el gimnasio").</summary>
public class RecurringSkip
{
    public int Id { get; set; }
    public int RecurringItemId { get; set; }
    public DateTime DueDate { get; set; }
}
