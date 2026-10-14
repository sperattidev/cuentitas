using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml;

namespace Cuentitas.App.Styles;

public sealed partial class Templates : ResourceDictionary
{
    public Templates()
    {
        InitializeComponent();
    }

    /// <summary>Botón "Pagar"/"Cobrar" de la plantilla de vencimiento (Inicio y Vencimientos).</summary>
    private async void OnPayClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DueItem item) await App.Get<DialogService>().PayAsync(item.Occurrence);
    }
}
