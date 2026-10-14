# Genera la versión para instalar/compartir:
#   dist\Cuentitas\                     carpeta autocontenida (no requiere .NET ni Windows App SDK)
#   dist\Cuentitas-win-x64.zip          la misma carpeta comprimida (versión portable)
#   dist\Cuentitas-Setup-X.Y.Z.exe      instalador (si está Inno Setup 6)
# Uso: powershell -ExecutionPolicy Bypass -File tools\publish.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'dist\Cuentitas'
$zip = Join-Path $root 'dist\Cuentitas-win-x64.zip'

# Versión: la del proyecto (<Version> en Cuentitas.App.csproj).
[xml]$proj = Get-Content "$root\src\Cuentitas.App\Cuentitas.App.csproj"
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

if (Test-Path (Join-Path $root 'dist')) { Remove-Item -Recurse -Force (Join-Path $root 'dist') }
dotnet publish "$root\src\Cuentitas.App" -c Release -r win-x64 --self-contained true `
    -p:Platform=x64 -p:PublishReadyToRun=true -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación" }

Compress-Archive -Path "$out\*" -DestinationPath $zip
$size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Output "Listo: $out\Cuentitas.exe (versión $version)"
Write-Output "Zip portable: $zip ($size MB)"

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" "$root\installer\Cuentitas.iss"
    if ($LASTEXITCODE -ne 0) { throw "Falló el instalador" }
    $setup = Join-Path $root "dist\Cuentitas-Setup-$version.exe"
    Write-Output "Instalador: $setup ($([Math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
} else {
    Write-Output "Inno Setup no está instalado: no se generó el instalador (winget install JRSoftware.InnoSetup)."
}
