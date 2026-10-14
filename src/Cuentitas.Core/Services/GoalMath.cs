namespace Cuentitas.Core.Services;

/// <summary>De dónde sale el ritmo de ahorro usado en la proyección.</summary>
public enum PaceSource
{
    /// <summary>El aporte mensual que el usuario planificó para la meta.</summary>
    Planned,

    /// <summary>El ahorro promedio de los últimos meses (ingresos − gastos).</summary>
    AverageSavings,

    /// <summary>Sin datos suficientes ni plan.</summary>
    None,
}

/// <summary>Resultado de proyectar una meta. Montos en centavos de la moneda de la meta.</summary>
public sealed record GoalProjection
{
    public long Current { get; init; }

    /// <summary>Objetivo a valores de hoy.</summary>
    public long Target { get; init; }

    public double Progress => Target <= 0 ? 0 : Math.Clamp((double)Current / Target, 0, 1);
    public bool Reached => Current >= Target;
    public long Remaining => Math.Max(0, Target - Current);

    public long MonthlyPace { get; init; }
    public PaceSource PaceSource { get; init; }

    /// <summary>Meses para llegar al ritmo actual (0 = ya llegaste). Null = a este ritmo no llegás.</summary>
    public int? MonthsToReach { get; init; }
    public DateTime? Eta { get; init; }

    /// <summary>Objetivo ajustado por inflación al momento de llegar (igual a <see cref="Target"/> sin inflación).</summary>
    public long? TargetAtEta { get; init; }

    /// <summary>Si hay fecha deseada: meses que faltan y cuánto hay que ahorrar por mes para llegar.</summary>
    public int? MonthsToTargetDate { get; init; }
    public long? RequiredMonthly { get; init; }

    /// <summary>Si hay fecha deseada: ¿al ritmo actual llegás a tiempo?</summary>
    public bool? OnTrack => MonthsToTargetDate is null ? null : MonthsToReach is { } m && m <= MonthsToTargetDate;
}

/// <summary>
/// Cálculos de metas, sin base de datos. La inflación es mensual y compuesta: el objetivo en pesos
/// del mes n es <c>objetivo × (1 + i)^n</c> (ADR-014). Los aportes se suponen al final de cada mes.
/// </summary>
public static class GoalMath
{
    /// <summary>Horizonte máximo (50 años). Más allá se considera "no llegás".</summary>
    public const int MaxMonths = 600;

    public static long TargetAt(long target, int months, double monthlyInflation) =>
        monthlyInflation <= 0 ? target : (long)Math.Round(target * Math.Pow(1 + monthlyInflation, months));

    /// <summary>Menor n ≥ 0 tal que <c>ahorrado + aporte × n ≥ objetivo(n)</c>. Null si no se alcanza nunca.</summary>
    public static int? MonthsToReach(long saved, long target, long monthly, double monthlyInflation)
    {
        if (saved >= target) return 0;
        if (monthly <= 0) return null;
        for (var n = 1; n <= MaxMonths; n++)
        {
            if (saved + monthly * (long)n >= TargetAt(target, n, monthlyInflation)) return n;
        }
        return null;
    }

    /// <summary>Aporte mensual necesario para llegar en <paramref name="months"/> meses (redondeado hacia arriba).</summary>
    public static long RequiredMonthly(long saved, long target, int months, double monthlyInflation)
    {
        var goal = TargetAt(target, Math.Max(0, months), monthlyInflation);
        var missing = goal - saved;
        if (missing <= 0) return 0;
        if (months <= 0) return missing;
        return (missing + months - 1) / months;
    }

    /// <summary>Meses completos entre hoy y la fecha (0 si es este mes o ya pasó).</summary>
    public static int MonthsUntil(DateTime today, DateTime date)
    {
        var months = (date.Year - today.Year) * 12 + date.Month - today.Month;
        if (date.Day < today.Day) months--;
        return Math.Max(0, months);
    }

    public static GoalProjection Project(long current, long target, long monthlyPace, PaceSource source,
        double monthlyInflation, DateTime today, DateTime? targetDate)
    {
        var months = MonthsToReach(current, target, monthlyPace, monthlyInflation);
        int? monthsToDate = targetDate is { } d ? MonthsUntil(today, d) : null;
        return new GoalProjection
        {
            Current = current,
            Target = target,
            MonthlyPace = monthlyPace,
            PaceSource = source,
            MonthsToReach = months,
            Eta = months is { } m ? today.Date.AddMonths(m) : null,
            TargetAtEta = months is { } m2 ? TargetAt(target, m2, monthlyInflation) : null,
            MonthsToTargetDate = monthsToDate,
            RequiredMonthly = monthsToDate is { } md ? RequiredMonthly(current, target, md, monthlyInflation) : null,
        };
    }
}
