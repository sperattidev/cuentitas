# Guía de desarrollo

## Requisitos (ya instalados en esta PC)
- Windows 11
- **.NET 10 SDK** (`winget install Microsoft.DotNet.SDK.10`)
- Opcional: Visual Studio 2022/2026 con la carga "Desarrollo de aplicaciones WinUI", o VS Code con C# Dev Kit.
- Herramienta de migraciones: se restaura sola con `dotnet tool restore` (está en `dotnet-tools.json`).

> Si `dotnet` no se reconoce en una terminal abierta antes de instalarlo, cerrala y abrí otra.

## Compilar y probar
```powershell
dotnet build Cuentitas.slnx
dotnet test tests/Cuentitas.Core.Tests
```

## Ejecutar sin tocar tus datos reales
```powershell
# Con datos de ejemplo (3 meses de movimientos, cuentas en pesos y dólares)
powershell -ExecutionPolicy Bypass -File tools\dev-run.ps1 -Demo

# Base vacía, como la vería alguien nuevo
powershell -ExecutionPolicy Bypass -File tools\dev-run.ps1 -Fresh

# Abrir directo en una pantalla / con un diálogo / con un tema
powershell -ExecutionPolicy Bypass -File tools\dev-run.ps1 -Demo -AppArgs "--page=transactions --theme=light"
```
`dev-run.ps1` define `CUENTITAS_DATA_DIR=%TEMP%\cuentitas-dev`, así la app usa esa carpeta en lugar de
`%LOCALAPPDATA%\Cuentitas`.

### Opciones de desarrollo (sólo funcionan con `CUENTITAS_DATA_DIR` definida)
| Opción | Efecto |
|---|---|
| `--demo` | Carga datos de ejemplo (`DemoData.cs`) si la base no tiene movimientos |
| `--theme=light\|dark` | Fuerza un tema |
| `--page=home\|transactions\|bills\|goals\|accounts\|categories\|settings` | Abre en esa pantalla |
| `--open=new\|edit\|buy\|account\|category\|recurring\|pay\|goal` | Abre ese diálogo (`pay` = pagar el primer vencimiento pendiente) |
| `--cut=50` | En Metas, pone el simulador de recortes en ese porcentaje |
| `--scroll=bottom` | Baja hasta el final de la página (para capturar lo que no entra) |
| `--onboarding[=N]` | Muestra la bienvenida (y avanza N pasos) |
| `--onboarding-fill` | Completa la bienvenida con datos de prueba y la termina (prueba de punta a punta) |
| `--set-pin=2580` / `--lock` | Configura un PIN de prueba / bloquea |

Para ver la bienvenida como alguien nuevo: `tools\dev-run.ps1 -Fresh` (con `-Demo` no aparece porque hay movimientos).

> La app de desarrollo también descarga la cotización del dólar (es sólo un GET público). Los tests no usan
> internet: `QuoteService` recibe un `HttpClient` con un handler falso.

Estas sí funcionan siempre (son de la app, no de desarrollo):
| Argumento | Efecto |
|---|---|
| `--check-due` | Modo sin ventana: muestra el aviso de vencimientos de hoy (uno por día) y sale |
| `--check-due --force` | Igual, aunque ya se haya avisado hoy |
| `cuentitas:bills` | Abre (o trae al frente) en Vencimientos; es lo que usa el clic en la notificación |

Probar avisos con los datos de ejemplo:
```powershell
$env:CUENTITAS_DATA_DIR = "$env:TEMP\cuentitas-dev"
& "src\Cuentitas.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Cuentitas.exe" --check-due --force
```

### Capturas de pantalla de la app
```powershell
powershell -ExecutionPolicy Bypass -File tools\screenshot.ps1 -Out captura.png
```
Usa `PrintWindow`: captura **sólo la ventana de Cuentitas**, nunca el resto de la pantalla.

## Cambiar el esquema de la base
1. Modificar entidades en `src/Cuentitas.Core/Domain/Entities.cs` y/o el mapeo en `CuentitasDbContext.cs`.
2. Crear la migración:
   ```powershell
   dotnet tool restore
   dotnet ef migrations add NombreDelCambio --project src/Cuentitas.Core --output-dir Data/Migrations
   ```
3. Borrar el `design-time.db` que pueda quedar en la raíz.
4. Revisar el archivo generado, correr los tests, actualizar `docs/MODELO_DE_DATOS.md` (tabla de migraciones).
5. Las bases existentes se actualizan solas al abrir la app.

> **Nunca** editar ni borrar migraciones ya distribuidas a la familia: siempre agregar una nueva.

## Instalador
`tools\publish.ps1` también compila `installer\Cuentitas.iss` si está Inno Setup 6
(`winget install JRSoftware.InnoSetup`). La versión sale de `<Version>` del `.csproj`.
- Resultado: `dist\Cuentitas-Setup-X.Y.Z.exe` (~80 MB).
- Instala por usuario en `%LOCALAPPDATA%\Programs\Cuentitas`, acceso directo en el menú Inicio (y escritorio opcional).
- **Actualizar:** ejecutar el instalador nuevo con la app cerrada (si está abierta, el instalador ofrece cerrarla).
- **Desinstalar:** Configuración de Windows > Aplicaciones > Cuentitas. Pregunta si borrar los datos.
- Los scripts `.ps1` y el `.iss` se guardan en **UTF-8 con BOM** (PowerShell 5 e Inno Setup leen como ANSI si no).

## Publicar una versión nueva en GitHub
1. Subir `<Version>` en `src/Cuentitas.App/Cuentitas.App.csproj` y anotar los cambios en `docs/ROADMAP.md`.
2. `powershell -ExecutionPolicy Bypass -File tools\publish.ps1` (genera instalador y zip en `dist\`).
3. Commit y push a `main`.
4. Crear la release con los dos archivos (reemplazar X.Y.Z):
   ```powershell
   gh release create vX.Y.Z dist\Cuentitas-Setup-X.Y.Z.exe dist\Cuentitas-win-x64.zip --repo sperattidev/cuentitas --target main --title "Cuentitas X.Y.Z" --notes "Novedades…" --latest
   ```
El README enlaza a `releases/latest`, así que siempre apunta a la última versión.

## Si alguien olvida su PIN
Con Windows Hello activo se desbloquea con la huella/cara/PIN de Windows. Si no: cerrar la app, abrir
`%LOCALAPPDATA%\Cuentitas\cuentitas.db` con cualquier visor de SQLite y vaciar el valor de `lock.pinHash` en la
tabla `Settings` (ADR-017).

## Publicar (para usar o compartir)
```powershell
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
```
Genera:
- `dist\Cuentitas\Cuentitas.exe` → la app lista para usar (carpeta autocontenida).
- `dist\Cuentitas-win-x64.zip` → para pasar a otra PC (descomprimir y ejecutar `Cuentitas.exe`).

No hace falta instalar .NET ni nada más en la PC destino (Windows 10 1809+ / Windows 11, x64).
Sugerencia: copiar la carpeta a `C:\Programas\Cuentitas` y crear un acceso directo en el escritorio.

Al publicar una versión nueva: subir `<Version>` en `src/Cuentitas.App/Cuentitas.App.csproj` y
anotar los cambios en la bitácora de `docs/ROADMAP.md`.

## Ícono de la app
`tools\make-icon.ps1` regenera `Assets\cuentitas.ico` y `Assets\AppIcon.png`.

## Dónde están los datos
| Qué | Dónde |
|---|---|
| Base de datos | `%LOCALAPPDATA%\Cuentitas\cuentitas.db` |
| Registro de errores | `%LOCALAPPDATA%\Cuentitas\errores.log` |
| Copias de seguridad | `Documentos\Cuentitas\Copias de seguridad\` |
| Tarea de avisos (si se activa) | Programador de tareas → `Cuentitas\Recordatorios` |
| Registro de notificaciones | `HKCU\Software\Classes\AppUserModelId\Cuentitas.App` y `HKCU\Software\Classes\cuentitas` |

Para desinstalar del todo: desactivar los avisos en Ajustes, borrar la carpeta de la app y, si se quiere,
`%LOCALAPPDATA%\Cuentitas` y las dos claves de registro de arriba.

Restaurar una copia: cerrar la app y reemplazar `cuentitas.db` por el archivo de la copia (renombrado).

## Convenciones de código
- C# moderno: constructores primarios, `required`/`init` donde aplique, nullable habilitado.
- Lógica de negocio **sólo** en `Cuentitas.Core` y con tests. La App no habla con EF directamente
  (salvo `DevOptions` para datos de ejemplo).
- Comentarios y textos en español. Nombres de código en inglés.
- Nada de colores fijos en XAML de páginas: usar recursos de `Theme.xaml`.
