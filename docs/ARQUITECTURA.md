# Arquitectura

## Vista general

```
┌──────────────────────────── Cuentitas.App (WinUI 3) ─────────────────────────────┐
│  Views (XAML)  ──x:Bind──▶  ViewModels (MVVM Toolkit)  ──▶  Services (UI)        │
│  HomePage, TransactionsPage,  HomeViewModel, ...            DialogService,       │
│  AccountsPage, CategoriesPage, TransactionEditorViewModel   ThemeService,        │
│  SettingsPage, Dialogs/*                                    BuyDollars, DevOptions│
└───────────────────────────────────────┬──────────────────────────────────────────┘
                                        │ inyección de dependencias
┌───────────────────────────── Cuentitas.Core (.NET 10) ───────────────────────────┐
│  Services: AccountService, CategoryService, TransactionService, SummaryService,  │
│            SettingsService, BackupService, DataChanges (aviso de cambios)        │
│  Domain:   Account, Category, Transaction (+ enums)      Money: centavos ↔ texto │
│  Data:     CuentitasDbContext (EF Core), Migrations, Seed, DemoData, Database    │
└───────────────────────────────────────┬──────────────────────────────────────────┘
                                        ▼
                   SQLite: %LOCALAPPDATA%\Cuentitas\cuentitas.db
```

- **Cuentitas.Core** no depende de Windows ni de la UI: toda la lógica de negocio vive acá y se
  prueba con `tests/Cuentitas.Core.Tests` (SQLite en memoria, migraciones reales).
- **Cuentitas.App** sólo presenta: pide datos a los servicios y los muestra.

## Carpetas

```
Cuentitas.slnx
src/
  Cuentitas.Core/
    Money/Money.cs              Currency (ARS/USD), formato "$ 1.234,56", parseo, tipo de cambio
    Domain/Entities.cs          Account, Category, Transaction, enums (TxType, Necessity, DollarType…)
    Data/
      CuentitasDbContext.cs     Mapeo EF Core (enums como texto, restricciones)
      Migrations/               Migraciones EF (NO editar a mano salvo necesidad)
      Seed.cs                   Cuenta "Efectivo" + categorías argentinas (una sola vez)
      DemoData.cs               Datos de ejemplo (sólo desarrollo, --demo)
      Database.cs               Ruta de datos, arranque (migrar + seed), factory de diseño
    Services/                   Casos de uso (ver abajo)
  Cuentitas.App/
    Program.cs                  Main propio: instancia única y modo --check-due
    App.xaml(.cs)               Arranque, contenedor DI, activaciones, registro de errores
    MainWindow.xaml(.cs)        Barra de título propia, Mica, NavigationView, Ctrl+N
    Styles/Theme.xaml           Colores (claro/oscuro), estilos de tarjetas y textos
    Styles/Templates.xaml       Plantillas compartidas (fila de movimiento, de cuenta…)
    Controls/                   IconBadge (ícono en círculo), IconColorPicker, Charts (BarChart, LineChart, DonutChart)
    Dialogs/                    TransactionDialog, AccountDialog, CategoryDialog, RecurringDialog
    Views/                      Páginas
    ViewModels/                 Lógica de presentación de cada página + ítems de lista
    Services/                   DialogService, ThemeService, BuyDollars, DevOptions, Reminders, Notifications
    Helpers/                    AppIcons (catálogo de íconos), Ui (fechas/colores), DollarTypes, DueText
    Assets/                     cuentitas.ico, AppIcon.png (generados por tools/make-icon.ps1)
tests/Cuentitas.Core.Tests/     Tests de dinero y servicios
tools/                          Scripts: dev-run, screenshot, publish, make-icon
docs/                           Esta documentación
```

## Servicios del Core

| Servicio | Responsabilidad |
|---|---|
| `AccountService` | CRUD de cuentas, **saldos calculados**, archivar, impedir borrar cuentas con movimientos. |
| `CategoryService` | CRUD de categorías, archivar. |
| `TransactionService` | Alta/edición/baja de movimientos, filtros y búsqueda, **validación y normalización** (`NormalizeAndValidateAsync`). |
| `SummaryService` | Patrimonio por moneda, totales del período (ingresos, gastos, prescindibles), gastos por categoría. |
| `SettingsService` | Preferencias clave/valor (tema). |
| `BackupService` | Copia de seguridad consistente con `VACUUM INTO`. |
| `GoalService` / `GoalMath` | Metas: progreso, capacidad de ahorro, proyección con inflación, simulador. |
| `BudgetService` | Presupuestos por categoría: estado, proyección del mes, alertas, sugerencias. |
| `ReportService` | Serie de 12 meses (ingresos, gastos, prescindibles, patrimonio) y comparación con el mes anterior. |
| `QuoteService` | Cotizaciones del dólar (dolarapi.com), guardadas para uso sin conexión. **Único acceso a internet.** |
| `ExportService` | Excel (ClosedXML: movimientos, resumen mensual, cuentas) y CSV para Excel en español. |
| `SetupService` | Bienvenida: aplica cuentas, sueldo/aguinaldo y servicios en una transacción; marca `onboarding.done`. |
| `PinService` | PIN de la app (hash PBKDF2 con sal), Windows Hello on/off. |
| `RecurringService` | Pagos/cobros recurrentes: CRUD, agenda de vencimientos con estado, recordatorios, omitir. Fechas en `Schedule` (puro). |
| `DataChanges` | Evento global "cambiaron los datos": los servicios lo disparan, las páginas recargan. |

## Flujo típico: cargar un gasto

1. Usuario aprieta **Nuevo movimiento** o **Ctrl+N** → `DialogService.NewTransactionAsync()`.
2. Se crea `TransactionEditorViewModel`, que carga cuentas y categorías, y se muestra `TransactionDialog`.
3. **Guardar** → `TransactionEditorViewModel.SaveAsync()` → `TransactionService.AddAsync()`:
   valida, limpia campos que no aplican, guarda y llama `DataChanges.Notify()`.
4. Las páginas visibles (heredan de `PageViewModel`) reciben el aviso y recargan.

## Arranque: `Program.cs` (Main propio)
`DISABLE_XAML_GENERATED_MAIN` reemplaza el Main de XAML para:
1. **`--check-due`** → modo sin ventana (`Reminders.RunHeadlessAsync`): abre la base, calcula los
   recordatorios de hoy, muestra **una** notificación por día y sale. Lo usa la tarea programada.
2. **Instancia única** por carpeta de datos (`AppInstance.FindOrRegisterForKey`): si ya hay una ventana,
   la segunda instancia le redirige su activación (con sus argumentos) y termina.
   La principal recibe `Activated` → `MainWindow.BringToFront(página)`.

## Recordatorios y notificaciones
```
Tarea programada de Windows "Cuentitas\Recordatorios" (10:00 diario + al iniciar sesión)
        └─▶ Cuentitas.exe --check-due ─▶ RecurringService.GetRemindersAsync(hoy)
                                       └─▶ Notifications.ShowReminders  (toast de Windows)
Clic en el toast ─▶ protocolo "cuentitas:bills" ─▶ Cuentitas.exe "cuentitas:bills"
        └─▶ (si ya está abierta) redirección ─▶ ventana al frente en Vencimientos
```
- `Services/Reminders.cs`: alta/baja de la tarea con `schtasks /XML` (sin permisos de administrador;
  si Windows no permite el disparador de inicio de sesión, queda sólo el diario).
- `Services/Notifications.cs`: API clásica `Windows.UI.Notifications` + AUMID y protocolo en HKCU (ADR-010).
- En **modo desarrollo** (`CUENTITAS_DATA_DIR`) no se crea la tarea ni se registra el protocolo.
- Con la app abierta: el ítem **Vencimientos** del menú muestra un contador (rojo si hay vencidos)
  y el Inicio muestra la tarjeta "Próximos vencimientos".

## Arranque de la ventana (v1.0)
1. `MainWindow.InitializeAsync` (tamaño, tema, menú, contador de vencimientos).
2. `LockAsync`: si hay PIN, la capa de bloqueo tapa todo **antes** de mostrar la ventana.
3. Si `SetupService.ShouldShowAsync()` (nunca completada y sin movimientos) → `ShowOnboarding()`:
   la bienvenida ocupa la ventana (`FullFrame`) sin el menú; al terminar `EndOnboarding()` vuelve al Inicio.
4. Con Windows Hello activo se ofrece apenas la ventana es visible.
- Ctrl+L bloquea. Mientras está bloqueada no responde Ctrl+N.
- `Services/WindowsHello.cs` usa `UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd)` (apps sin empaquetar).

## Restaurar una copia
`BackupService.RestoreAsync`: valida el archivo (tablas de Cuentitas) → guarda una copia "antes-de-restaurar" →
suelta las conexiones (`SqliteConnection.ClearAllPools`) → reemplaza la base vía archivo temporal → aplica
migraciones. Como cada operación abre su propio `DbContext`, no hace falta reiniciar: se avisa `DataChanges` y se
vuelve al Inicio.

## Decisiones de diseño de código
- **`IDbContextFactory`**: un `DbContext` corto por operación (recomendado para apps de escritorio).
- **Sin `{Binding}`**: sólo `x:Bind` (compilado, más rápido y con errores en compilación).
  Los `SelectedItem` de ComboBox/GridView se sincronizan en code-behind (x:Bind TwoWay con `object` no compila).
- **Colores calculados en código** (`Ui.ToneBrush`) se resuelven con el tema de la ventana; al cambiar
  de tema, `MainWindow` recarga la página actual.
- **Errores no controlados** se registran en `%LOCALAPPDATA%\Cuentitas\errores.log`.
