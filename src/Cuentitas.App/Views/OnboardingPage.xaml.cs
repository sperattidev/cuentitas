using Cuentitas.App.Services;
using Cuentitas.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cuentitas.App.Views;

public sealed partial class OnboardingPage : Page
{
    public OnboardingPage()
    {
        ViewModel = App.Get<OnboardingViewModel>();
        InitializeComponent();
    }

    public OnboardingViewModel ViewModel { get; }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        var finished = await ViewModel.NextAsync();
        if (ViewModel.Step == OnboardingViewModel.Steps - 1) ViewModel.RefreshSummary();
        if (!finished) return;

        // Avisos con la app cerrada: crea la tarea programada (no en modo desarrollo).
        if (ViewModel.EnableReminders && !Reminders.IsDevMode) await Task.Run(Reminders.EnableTask);
        App.MainWindow.EndOnboarding();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => ViewModel.Back();

    private async void OnSkipClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SkipAsync();
        App.MainWindow.EndOnboarding();
    }
}
