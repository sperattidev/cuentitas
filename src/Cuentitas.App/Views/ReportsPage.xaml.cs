using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Views;

public sealed partial class ReportsPage : Page
{
    public ReportsPage()
    {
        ViewModel = App.Get<ReportsViewModel>();
        InitializeComponent();
    }

    public ReportsViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.ActivateAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();

    private async void OnExportClick(object sender, RoutedEventArgs e) => await App.Get<ExportFlow>().RunAsync();
}
