using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cuentitas.App.Helpers;
using Cuentitas.Core;
using Cuentitas.Core.Domain;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;

namespace Cuentitas.App.ViewModels;

/// <summary>
/// Valores iniciales opcionales al abrir el editor: "Comprar dólares", o pagar/cobrar un vencimiento
/// (<see cref="RecurringItemId"/> + <see cref="DueDate"/>).
/// </summary>
public sealed record TransactionPreset(TxType Type, int? AccountId = null, int? ToAccountId = null)
{
    public int? CategoryId { get; init; }
    public long? Amount { get; init; }
    public string? Note { get; init; }
    public int? RecurringItemId { get; init; }
    public DateTime? DueDate { get; init; }

    /// <summary>Título del diálogo (ej. "Pagar Luz").</summary>
    public string? Title { get; init; }
}

/// <summary>Formulario de alta/edición de un movimiento (diálogo "Nuevo movimiento").</summary>
public sealed partial class TransactionEditorViewModel(
    AccountService accountService,
    CategoryService categoryService,
    TransactionService transactionService) : ObservableObject
{
    private List<CategoryOption> _allExpense = [];
    private List<CategoryOption> _allIncome = [];
    private Transaction? _editing;
    private TransactionPreset? _preset;

    public ObservableCollection<AccountOption> Accounts { get; } = [];
    public ObservableCollection<AccountOption> TargetAccounts { get; } = [];
    public ObservableCollection<CategoryOption> Categories { get; } = [];

    public bool IsEditing => _editing is not null;
    public string Title => _preset?.Title ?? (IsEditing ? "Editar movimiento" : "Nuevo movimiento");

    /// <summary>Vinculado a un vencimiento (pago/cobro recurrente): el tipo no se puede cambiar.</summary>
    public bool IsRecurringPayment => (_editing?.RecurringItemId ?? _preset?.RecurringItemId) is not null;
    public bool CanChangeType => !IsRecurringPayment && _editing?.InstallmentGroup is null;

    /// <summary>Opciones de cuotas: 0 = "1 pago", luego 2, 3, 6, 9, 12, 18, 24.</summary>
    public static readonly int[] InstallmentChoices = [1, 2, 3, 6, 9, 12, 18, 24];
    public List<string> InstallmentOptions { get; } =
        InstallmentChoices.Select(n => n == 1 ? "En 1 pago" : $"En {n} cuotas").ToList();

    [ObservableProperty]
    public partial int InstallmentIndex { get; set; }

    public int InstallmentCount => InstallmentChoices[Math.Clamp(InstallmentIndex, 0, InstallmentChoices.Length - 1)];

    /// <summary>Sólo al cargar un gasto nuevo que no es un vencimiento.</summary>
    public Visibility InstallmentsVisibility =>
        Type == TxType.Expense && !IsEditing && !IsRecurringPayment ? Visibility.Visible : Visibility.Collapsed;

    public string InstallmentHint
    {
        get
        {
            if (InstallmentCount == 1) return "";
            var total = Money.Parse(AmountText);
            var each = total is > 0 ? Money.Format(total.Value / InstallmentCount, SelectedAccount?.Currency ?? Currency.ARS) : "…";
            return $"{InstallmentCount} cuotas de {each}, una por mes desde la fecha de compra. Ingresá el total.";
        }
    }

    public Visibility InstallmentHintVisibility =>
        InstallmentsVisibility == Visibility.Visible && InstallmentCount > 1 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Aviso al editar una cuota o un pago de vencimiento.</summary>
    public string InfoText => _editing switch
    {
        { InstallmentGroup: not null } e =>
            $"Cuota {e.InstallmentNumber} de {e.InstallmentCount}. Los cambios se aplican sólo a esta cuota.",
        { RecurringItemId: not null, DueDate: { } d } => $"Pago del vencimiento del {d:dd/MM/yyyy}.",
        _ when _preset?.DueDate is { } due => $"Vencimiento del {due:dd/MM/yyyy}. Corregí el monto si fue distinto.",
        _ => "",
    };

    public Visibility InfoVisibility => InfoText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>0 = Gasto, 1 = Ingreso, 2 = Transferencia.</summary>
    [ObservableProperty]
    public partial int TypeIndex { get; set; }

    [ObservableProperty]
    public partial string AmountText { get; set; } = "";

    [ObservableProperty]
    public partial string ToAmountText { get; set; } = "";

    [ObservableProperty]
    public partial AccountOption? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial AccountOption? SelectedToAccount { get; set; }

    [ObservableProperty]
    public partial CategoryOption? SelectedCategory { get; set; }

    /// <summary>0 = Necesario, 1 = Prescindible.</summary>
    [ObservableProperty]
    public partial int NecessityIndex { get; set; }

    /// <summary>Índice en <see cref="DollarTypes.All"/> (compra/venta de dólares). Por defecto MEP.</summary>
    [ObservableProperty]
    public partial int DollarTypeIndex { get; set; } = (int)DollarType.Mep;

    public List<string> DollarTypeOptions { get; } = DollarTypes.All.Select(DollarTypes.Description).ToList();

    [ObservableProperty]
    public partial DateTimeOffset Date { get; set; } = DateTimeOffset.Now.Date;

    [ObservableProperty]
    public partial string Note { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    public TxType Type => (TxType)TypeIndex;
    public bool IsTransfer => Type == TxType.Transfer;
    public Visibility CategoryVisibility => IsTransfer ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TransferVisibility => IsTransfer ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NecessityVisibility => Type == TxType.Expense ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => ErrorMessage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool NeedsToAmount => IsTransfer && SelectedAccount is not null && SelectedToAccount is not null &&
                                 SelectedAccount.Currency != SelectedToAccount.Currency;

    public Visibility ToAmountVisibility => NeedsToAmount ? Visibility.Visible : Visibility.Collapsed;

    public string AmountLabel => Type switch
    {
        TxType.Income => "¿Cuánto entró?",
        TxType.Transfer => NeedsToAmount ? "¿Cuánto pagaste?" : "¿Cuánto transferís?",
        _ => "¿Cuánto gastaste?",
    };

    public string AccountLabel => Type switch
    {
        TxType.Income => "Entró en",
        TxType.Transfer => "Desde",
        _ => "Pagaste con",
    };

    public string CurrencySymbol => (SelectedAccount?.Currency ?? Currency.ARS).Symbol();
    public string ToCurrencySymbol => (SelectedToAccount?.Currency ?? Currency.USD).Symbol();
    public string ToAmountLabel => $"¿Cuánto recibiste en {SelectedToAccount?.Currency.Label().ToLowerInvariant()}?";

    /// <summary>Tipo de cambio que surge de los dos montos de una compra/venta de dólares.</summary>
    public string RateText
    {
        get
        {
            if (!NeedsToAmount) return "";
            var paid = Money.Parse(AmountText);
            var received = Money.Parse(ToAmountText);
            if (paid is not > 0 || received is not > 0) return "Completá ambos montos para ver el tipo de cambio.";
            var rate = SelectedToAccount!.Currency == Currency.USD
                ? Money.ImpliedRate(paid.Value, received.Value)
                : Money.ImpliedRate(received.Value, paid.Value);
            return $"Tipo de cambio pagado: {Money.FormatRate(rate)} por dólar";
        }
    }

    public async Task LoadAsync(Transaction? editing, TransactionPreset? preset)
    {
        _editing = editing;
        _preset = editing is null ? preset : null;
        var accounts = await accountService.GetAllWithBalancesAsync(includeArchived: editing is not null);
        foreach (var a in accounts.Where(a => !a.Account.Archived || a.Account.Id == editing?.AccountId ||
                                              a.Account.Id == editing?.ToAccountId))
            Accounts.Add(AccountOption.From(a));

        var categories = await categoryService.GetAsync(includeArchived: editing is not null);
        _allExpense = categories.Where(c => c.Kind == CategoryKind.Expense &&
                                            (!c.Archived || c.Id == editing?.CategoryId))
            .Select(c => new CategoryOption(c)).ToList();
        _allIncome = categories.Where(c => c.Kind == CategoryKind.Income &&
                                           (!c.Archived || c.Id == editing?.CategoryId))
            .Select(c => new CategoryOption(c)).ToList();

        if (editing is not null)
        {
            TypeIndex = (int)editing.Type;
            RefreshCategories();
            AmountText = Money.ToEditable(editing.Amount);
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == editing.AccountId);
            SelectedToAccount = TargetAccounts.FirstOrDefault(a => a.Id == editing.ToAccountId);
            ToAmountText = editing.ToAmount is { } to ? Money.ToEditable(to) : "";
            if (editing.DollarType is { } dt) DollarTypeIndex = (int)dt;
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == editing.CategoryId);
            NecessityIndex = editing.Necessity == Necessity.Optional ? 1 : 0;
            Date = editing.Date;
            Note = editing.Note;
        }
        else
        {
            TypeIndex = (int)(preset?.Type ?? TxType.Expense);
            RefreshCategories();
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == preset?.AccountId) ?? Accounts.FirstOrDefault();
            RefreshTargets();
            SelectedToAccount = TargetAccounts.FirstOrDefault(a => a.Id == preset?.ToAccountId)
                                ?? TargetAccounts.FirstOrDefault();
            if (preset?.CategoryId is { } cat) SelectedCategory = Categories.FirstOrDefault(c => c.Id == cat);
            if (preset?.Amount is > 0) AmountText = Money.ToEditable(preset.Amount.Value);
            if (preset?.Note is { Length: > 0 } note) Note = note;
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsRecurringPayment));
        OnPropertyChanged(nameof(CanChangeType));
        OnPropertyChanged(nameof(InfoText));
        OnPropertyChanged(nameof(InfoVisibility));
        OnPropertyChanged(nameof(InstallmentsVisibility));
    }

    /// <summary>Guarda. Devuelve false (y completa <see cref="ErrorMessage"/>) si hay datos inválidos.</summary>
    public async Task<bool> SaveAsync()
    {
        ErrorMessage = "";
        var amount = Money.Parse(AmountText);
        if (amount is not > 0)
        {
            ErrorMessage = "Ingresá un monto mayor a cero (ej. 1.500 o 1500,50).";
            return false;
        }
        if (SelectedAccount is null)
        {
            ErrorMessage = "Elegí una cuenta.";
            return false;
        }

        var tx = new Transaction
        {
            Id = _editing?.Id ?? 0,
            Type = Type,
            Amount = amount.Value,
            AccountId = SelectedAccount.Id,
            Date = Date.Date,
            Note = Note,
            RecurringItemId = _editing?.RecurringItemId ?? _preset?.RecurringItemId,
            DueDate = _editing?.DueDate ?? _preset?.DueDate,
        };

        if (IsTransfer)
        {
            tx.ToAccountId = SelectedToAccount?.Id;
            if (NeedsToAmount)
            {
                var to = Money.Parse(ToAmountText);
                if (to is not > 0)
                {
                    ErrorMessage = $"Ingresá cuánto recibiste en {SelectedToAccount!.Currency.Label().ToLowerInvariant()}.";
                    return false;
                }
                tx.ToAmount = to;
                tx.DollarType = DollarTypes.All[Math.Clamp(DollarTypeIndex, 0, DollarTypes.All.Length - 1)];
            }
        }
        else
        {
            tx.CategoryId = SelectedCategory?.Id;
            if (Type == TxType.Expense) tx.Necessity = NecessityIndex == 1 ? Necessity.Optional : Necessity.Necessary;
        }

        try
        {
            if (IsEditing) await transactionService.UpdateAsync(tx);
            else if (InstallmentsVisibility == Visibility.Visible && InstallmentCount > 1)
                await transactionService.AddInstallmentsAsync(tx, InstallmentCount);
            else await transactionService.AddAsync(tx);
            return true;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>Deja el formulario listo para cargar otro movimiento (mantiene tipo, cuenta y fecha).</summary>
    public void ResetForNext()
    {
        _editing = null;
        _preset = null;
        AmountText = "";
        ToAmountText = "";
        Note = "";
        SelectedCategory = null;
        InstallmentIndex = 0;
        ErrorMessage = "";
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsRecurringPayment));
        OnPropertyChanged(nameof(CanChangeType));
        OnPropertyChanged(nameof(InfoText));
        OnPropertyChanged(nameof(InfoVisibility));
        OnPropertyChanged(nameof(InstallmentsVisibility));
    }

    partial void OnInstallmentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(InstallmentHint));
        OnPropertyChanged(nameof(InstallmentHintVisibility));
    }

    partial void OnTypeIndexChanged(int value)
    {
        if (value < 0) return;
        RefreshCategories();
        RefreshTargets();
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(IsTransfer));
        OnPropertyChanged(nameof(CategoryVisibility));
        OnPropertyChanged(nameof(TransferVisibility));
        OnPropertyChanged(nameof(NecessityVisibility));
        OnPropertyChanged(nameof(AmountLabel));
        OnPropertyChanged(nameof(AccountLabel));
        OnPropertyChanged(nameof(InstallmentsVisibility));
        OnPropertyChanged(nameof(InstallmentHintVisibility));
        NotifyTransferState();
    }

    partial void OnSelectedAccountChanged(AccountOption? value)
    {
        RefreshTargets();
        OnPropertyChanged(nameof(CurrencySymbol));
        NotifyTransferState();
    }

    partial void OnSelectedToAccountChanged(AccountOption? value) => NotifyTransferState();

    partial void OnSelectedCategoryChanged(CategoryOption? value)
    {
        if (value?.Category.DefaultNecessity is { } n) NecessityIndex = n == Necessity.Optional ? 1 : 0;
    }

    partial void OnAmountTextChanged(string value)
    {
        OnPropertyChanged(nameof(RateText));
        OnPropertyChanged(nameof(InstallmentHint));
    }
    partial void OnToAmountTextChanged(string value) => OnPropertyChanged(nameof(RateText));
    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(ErrorVisibility));

    private void NotifyTransferState()
    {
        OnPropertyChanged(nameof(NeedsToAmount));
        OnPropertyChanged(nameof(ToAmountVisibility));
        OnPropertyChanged(nameof(ToCurrencySymbol));
        OnPropertyChanged(nameof(ToAmountLabel));
        OnPropertyChanged(nameof(AmountLabel));
        OnPropertyChanged(nameof(RateText));
    }

    private void RefreshCategories()
    {
        var source = Type == TxType.Income ? _allIncome : _allExpense;
        var keep = SelectedCategory;
        Categories.Clear();
        foreach (var c in source) Categories.Add(c);
        SelectedCategory = keep is not null && Categories.Contains(keep) ? keep : null;
    }

    private void RefreshTargets()
    {
        var keep = SelectedToAccount?.Id;
        TargetAccounts.Clear();
        foreach (var a in Accounts.Where(a => a.Id != SelectedAccount?.Id)) TargetAccounts.Add(a);
        SelectedToAccount = TargetAccounts.FirstOrDefault(a => a.Id == keep) ?? TargetAccounts.FirstOrDefault();
        // Al vaciar la lista el ComboBox pierde la selección aunque el valor no cambie: forzamos el aviso.
        OnPropertyChanged(nameof(SelectedToAccount));
    }
}
