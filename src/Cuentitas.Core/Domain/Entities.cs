namespace Cuentitas.Core.Domain;

// Modelo de datos documentado en docs/MODELO_DE_DATOS.md.
// Si cambiás algo acá: generá una migración EF (ver docs/DESARROLLO.md) y actualizá la doc.

public enum AccountType
{
    Cash,
    Bank,
    Wallet,
    CreditCard,
    Investment,
}

public enum CategoryKind
{
    Expense,
    Income,
}

public enum TxType
{
    Expense,
    Income,
    Transfer,
}

/// <summary>Clasificación de un gasto para las proyecciones de metas (ADR-008).</summary>
public enum Necessity
{
    Necessary,
    Optional,
}

/// <summary>Tipo de dólar de una compra/venta (contexto argentino, ADR-009).</summary>
public enum DollarType
{
    Oficial,
    Mep,
    Blue,
    Tarjeta,
    Cripto,
    Otro,
}

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public Currency Currency { get; set; } = Currency.ARS;

    /// <summary>Saldo al crear la cuenta, en centavos.</summary>
    public long InitialBalance { get; set; }

    /// <summary>Color ARGB (ej. 0xFF0F9D8A).</summary>
    public uint Color { get; set; }

    /// <summary>Clave de ícono del catálogo de la app (ADR-007), ej. "bank".</summary>
    public string Icon { get; set; } = "wallet";

    /// <summary>Si es false, no suma al patrimonio total.</summary>
    public bool IncludeInTotal { get; set; } = true;
    public bool Archived { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CategoryKind Kind { get; set; }

    /// <summary>Clave de ícono del catálogo de la app (ADR-007), ej. "cart".</summary>
    public string Icon { get; set; } = "other";
    public uint Color { get; set; }

    /// <summary>Necesidad sugerida al cargar un gasto de esta categoría. Null en ingresos.</summary>
    public Necessity? DefaultNecessity { get; set; }
    public bool Archived { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Un movimiento: gasto, ingreso o transferencia entre cuentas.</summary>
public class Transaction
{
    public int Id { get; set; }
    public TxType Type { get; set; }

    /// <summary>
    /// Monto positivo en centavos, en la moneda de <see cref="AccountId"/>.
    /// Gasto: sale · Ingreso: entra · Transferencia: sale de la cuenta origen.
    /// </summary>
    public long Amount { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Sólo transferencias: cuenta destino.</summary>
    public int? ToAccountId { get; set; }
    public Account? ToAccount { get; set; }

    /// <summary>
    /// Sólo transferencias: monto que entra al destino, en su moneda. Igual a <see cref="Amount"/>
    /// si ambas cuentas tienen la misma moneda; distinto en una compra/venta de dólares
    /// (de ahí sale el tipo de cambio pagado, ADR-004).
    /// </summary>
    public long? ToAmount { get; set; }

    /// <summary>Sólo compra/venta de dólares: qué dólar fue (oficial, MEP, blue...).</summary>
    public DollarType? DollarType { get; set; }

    /// <summary>Gastos e ingresos: categoría. Transferencias: null.</summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Fecha del movimiento (sólo se usa la parte de fecha).</summary>
    public DateTime Date { get; set; }
    public string Note { get; set; } = "";

    /// <summary>Sólo gastos.</summary>
    public Necessity? Necessity { get; set; }

    /// <summary>Si este movimiento paga/cobra un vencimiento: el pago recurrente…</summary>
    public int? RecurringItemId { get; set; }
    public RecurringItem? RecurringItem { get; set; }

    /// <summary>…y la fecha de vencimiento que salda (identifica la cuota del período).</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Compras en cuotas: identificador común a todas las cuotas de la misma compra.</summary>
    public string? InstallmentGroup { get; set; }

    /// <summary>Número de cuota (1..<see cref="InstallmentCount"/>).</summary>
    public int? InstallmentNumber { get; set; }
    public int? InstallmentCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>Es una compra o venta de moneda (transferencia entre monedas distintas).</summary>
    public bool IsCurrencyExchange =>
        Type == TxType.Transfer && Account is not null && ToAccount is not null && Account.Currency != ToAccount.Currency;
}
