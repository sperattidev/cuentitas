namespace Cuentitas.Core.Services;

/// <summary>
/// Aviso global de "cambiaron los datos". Los servicios lo disparan después de guardar;
/// las pantallas se suscriben y recargan. Singleton.
/// </summary>
public sealed class DataChanges
{
    public event EventHandler? Changed;

    public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
