using CommunityToolkit.Mvvm.ComponentModel;
using Cuentitas.Core.Services;

namespace Cuentitas.App.ViewModels;

/// <summary>
/// Base de las páginas: carga al entrar y recarga sola cuando cambian los datos
/// (<see cref="DataChanges"/>). Las páginas llaman Activate/Deactivate en Loaded/Unloaded.
/// </summary>
public abstract partial class PageViewModel(DataChanges changes) : ObservableObject
{
    private bool _reloadQueued;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public async Task ActivateAsync()
    {
        changes.Changed += OnDataChanged;
        await ReloadAsync();
    }

    public void Deactivate() => changes.Changed -= OnDataChanged;

    public async Task ReloadAsync()
    {
        if (IsLoading)
        {
            _reloadQueued = true;
            return;
        }
        IsLoading = true;
        try
        {
            do
            {
                _reloadQueued = false;
                await LoadAsync();
            } while (_reloadQueued);
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected abstract Task LoadAsync();

    private async void OnDataChanged(object? sender, EventArgs e) => await ReloadAsync();
}
