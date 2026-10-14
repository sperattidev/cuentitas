# Compila y abre Cuentitas con una carpeta de datos de PRUEBA (no toca tus datos reales).
# Ejemplos:
#   powershell -File tools\dev-run.ps1 -Demo                 # datos de ejemplo
#   powershell -File tools\dev-run.ps1 -Demo -AppArgs "--page=transactions --theme=light"
#   powershell -File tools\dev-run.ps1 -Fresh                # base vacía nueva
param([switch]$Demo, [switch]$Fresh, [string]$AppArgs = "", [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:CUENTITAS_DATA_DIR = Join-Path $env:TEMP 'cuentitas-dev'
# Sólo cierra la Cuentitas de desarrollo (la de bin\), nunca la que usás con tus datos reales.
Get-Process Cuentitas -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$root\src\Cuentitas.App\bin\*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500
if ($Fresh -or $Demo) { Remove-Item -Recurse -Force $env:CUENTITAS_DATA_DIR -ErrorAction SilentlyContinue }
if (-not $NoBuild) { dotnet build "$root\src\Cuentitas.App" -nologo -v q | Select-String 'error|Errores' }
$exe = Get-ChildItem "$root\src\Cuentitas.App\bin\x64\Debug" -Recurse -Filter Cuentitas.exe | Select-Object -First 1
$argList = @(); if ($Demo) { $argList += '--demo' }; if ($AppArgs) { $argList += $AppArgs.Split(' ') }
if ($argList.Count -gt 0) { Start-Process $exe.FullName -ArgumentList $argList } else { Start-Process $exe.FullName }
Write-Output "Cuentitas iniciado con datos en $env:CUENTITAS_DATA_DIR"
