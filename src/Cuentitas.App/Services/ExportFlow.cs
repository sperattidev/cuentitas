using System.Diagnostics;
using CommunityToolkit.WinUI.Controls;
using Cuentitas.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Cuentitas.App.Services;

/// <summary>"Exportar": elegir formato y período → elegir dónde guardar → exportar → ofrecer abrir.</summary>
public sealed class ExportFlow(ExportService export, DialogService dialogs)
{
    private static readonly (string Label, int Months)[] Periods =
        [("Este mes", 0), ("Últimos 3 meses", 3), ("Últimos 12 meses", 12), ("Todo", 1200)];

    public async Task RunAsync()
    {
        var format = new Segmented { HorizontalAlignment = HorizontalAlignment.Stretch };
        format.Items.Add(new SegmentedItem { Content = "Excel (.xlsx)" });
        format.Items.Add(new SegmentedItem { Content = "CSV" });
        format.SelectedIndex = 0;
        var period = new ComboBox { Header = "Período", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var p in Periods) period.Items.Add(p.Label);
        period.SelectedIndex = 2;

        var dialog = new ContentDialog
        {
            Title = "Exportar movimientos",
            PrimaryButtonText = "Elegir dónde guardar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Width = 420,
                Spacing = 14,
                Children =
                {
                    format,
                    period,
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Style = (Style)Application.Current.Resources["LabelStyle"],
                        Text = "Excel incluye tres hojas: movimientos, resumen mensual y cuentas. " +
                               "El CSV usa «;» y coma decimal, como el Excel en español.",
                    },
                },
            },
        };
        if (await dialogs.ShowCustomAsync(dialog) != ContentDialogResult.Primary) return;

        var excel = format.SelectedIndex == 0;
        var today = DateTime.Today;
        var months = Periods[Math.Max(0, period.SelectedIndex)].Months;
        var from = months == 0 ? new DateTime(today.Year, today.Month, 1) : today.AddMonths(-months).AddDays(1);

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Cuentitas {today:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add(excel ? "Libro de Excel" : "CSV (separado por punto y coma)", [excel ? ".xlsx" : ".csv"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            if (excel) await export.ExportExcelAsync(file.Path, from, today, today);
            else await export.ExportCsvAsync(file.Path, from, today);
        }
        catch (IOException ex)
        {
            await dialogs.ShowMessageAsync("No se pudo exportar",
                $"¿El archivo está abierto en Excel? Cerralo y probá de nuevo.\n\n{ex.Message}");
            return;
        }

        if (await dialogs.ConfirmAsync("Listo", $"Se exportó a {file.Name}.", "Abrir archivo"))
            Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
    }
}
