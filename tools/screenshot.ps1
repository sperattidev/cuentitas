# Captura SÓLO la ventana de Cuentitas a un PNG (para revisar la UI durante el desarrollo).
# Usa PrintWindow: dibuja la ventana en memoria, sin capturar el resto de la pantalla
# ni requerir que esté al frente.
# Uso: powershell -File tools\screenshot.ps1 -Out salida.png
param([string]$Out = "screenshot.png", [string]$Process = "Cuentitas")
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@
[Win]::SetProcessDPIAware() | Out-Null
$p = Get-Process $Process -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { throw "No hay ventana de $Process" }
if ([Win]::IsIconic($p.MainWindowHandle)) {
    [Win]::ShowWindow($p.MainWindowHandle, 9) | Out-Null  # SW_RESTORE
    Start-Sleep -Milliseconds 800
}
$r = New-Object Win+RECT
[Win]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# 2 = PW_RENDERFULLCONTENT (necesario para contenido DirectComposition/WinUI)
$ok = [Win]::PrintWindow($p.MainWindowHandle, $hdc, 2)
$g.ReleaseHdc($hdc)
if (-not $ok) { throw "PrintWindow falló" }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "$Out ($w x $h)"
