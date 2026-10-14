using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Cuentitas.App.Dialogs;

/// <summary>Elegir un PIN nuevo (dos veces).</summary>
public sealed partial class SetPinDialog : ContentDialog
{
    private readonly PinService _pins;
    private readonly PasswordBox _first = Box("PIN nuevo (4 a 8 números)");
    private readonly PasswordBox _second = Box("Repetí el PIN");
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };

    public SetPinDialog(PinService pins)
    {
        _pins = pins;
        Title = "Elegí un PIN para Cuentitas";
        PrimaryButtonText = "Guardar";
        CloseButtonText = "Cancelar";
        DefaultButton = ContentDialogButton.Primary;
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];
        _error.Foreground = Helpers.Ui.ToneBrush(Helpers.Tone.Expense);
        Content = new StackPanel
        {
            Width = 360,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["LabelStyle"],
                    Text = "Se pide al abrir la app (y con Ctrl+L). Evita que otra persona vea tus cuentas en esta sesión de Windows. " +
                           "No cifra el archivo de datos.",
                },
                _first,
                _second,
                _error,
            },
        };
        Opened += (_, _) => _first.Focus(FocusState.Programmatic);
        PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            if (PinService.Validate(_first.Password) is { } msg) Fail(msg, args);
            else if (_first.Password != _second.Password) Fail("Los dos PIN no coinciden.", args);
            else await _pins.SetPinAsync(_first.Password);
            deferral.Complete();
        };
    }

    private void Fail(string message, ContentDialogButtonClickEventArgs args)
    {
        _error.Text = message;
        args.Cancel = true;
    }

    internal static PasswordBox Box(string header) => new()
    {
        Header = header,
        MaxLength = PinService.MaxLength,
        InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.NumericPin) } },
    };
}

/// <summary>Pedir el PIN actual (antes de desactivarlo o cambiarlo).</summary>
public sealed partial class VerifyPinDialog : ContentDialog
{
    private readonly PasswordBox _box = SetPinDialog.Box("PIN actual");
    private readonly TextBlock _error = new();

    public VerifyPinDialog(PinService pins, string title)
    {
        Title = title;
        PrimaryButtonText = "Continuar";
        CloseButtonText = "Cancelar";
        DefaultButton = ContentDialogButton.Primary;
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];
        _error.Foreground = Helpers.Ui.ToneBrush(Helpers.Tone.Expense);
        Content = new StackPanel { Width = 320, Spacing = 12, Children = { _box, _error } };
        Opened += (_, _) => _box.Focus(FocusState.Programmatic);
        PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            if (!await pins.VerifyAsync(_box.Password))
            {
                _error.Text = "PIN incorrecto.";
                _box.Password = "";
                args.Cancel = true;
            }
            deferral.Complete();
        };
    }
}
