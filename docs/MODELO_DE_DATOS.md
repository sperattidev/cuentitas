# Modelo de datos

Base SQLite en `%LOCALAPPDATA%\Cuentitas\cuentitas.db` (una por usuario de Windows).
Definido en `src/Cuentitas.Core/Domain/Entities.cs` y `Data/CuentitasDbContext.cs`.

## Tablas

### Accounts (cuentas)
| Columna | Tipo | Notas |
|---|---|---|
| Id | int PK | |
| Name | text(60) | |
| Type | text | `Cash` · `Bank` · `Wallet` · `CreditCard` · `Investment` |
| Currency | text(3) | `ARS` · `USD` — **no se puede cambiar si hay movimientos** |
| InitialBalance | int (centavos) | Saldo al crear la cuenta. Tarjeta: negativo = deuda. |
| Color | int | ARGB |
| Icon | text | Clave de `AppIcons` (ej. `bank`) |
| IncludeInTotal | bool | Si suma al patrimonio |
| Archived | bool | Oculta en carga y patrimonio; conserva historial |
| SortOrder, CreatedAt | | |

### Categories (categorías)
| Columna | Tipo | Notas |
|---|---|---|
| Id, Name(40), Icon, Color, SortOrder, Archived | | |
| Kind | text | `Expense` · `Income` |
| DefaultNecessity | text? | `Necessary` · `Optional` — sugerencia al cargar un gasto. Null en ingresos. |

### Transactions (movimientos)
| Columna | Tipo | Notas |
|---|---|---|
| Id | int PK | |
| Type | text | `Expense` · `Income` · `Transfer` |
| Amount | int (centavos) | **Siempre > 0** (CHECK). En la moneda de `AccountId`. |
| AccountId | FK | Gasto: de dónde sale · Ingreso: dónde entra · Transferencia: origen |
| ToAccountId | FK? | Sólo transferencias: destino |
| ToAmount | int? | Sólo transferencias: lo que entra al destino (en su moneda) |
| DollarType | text? | Sólo compra/venta de dólares: `Oficial` · `Mep` · `Blue` · `Tarjeta` · `Cripto` · `Otro` |
| CategoryId | FK? | Gastos e ingresos (obligatoria). Null en transferencias. |
| Date | datetime | Sólo se usa la fecha |
| Note | text(500) | |
| Necessity | text? | Sólo gastos |
| RecurringItemId | FK? | Si paga/cobra un vencimiento (v0.2). `SET NULL` si se borra el pago recurrente. |
| DueDate | datetime? | Qué vencimiento salda. Único por (RecurringItemId, DueDate). |
| InstallmentGroup | text(36)? | Compra en cuotas: GUID común a todas las cuotas (v0.2) |
| InstallmentNumber / InstallmentCount | int? | "Cuota 2 de 6" |
| CreatedAt, UpdatedAt | | |

Índices: `Date`, `AccountId`, `ToAccountId`, `(RecurringItemId, DueDate)`, `InstallmentGroup`.
Borrado de cuentas/categorías con movimientos: **RESTRICT** (se archivan en lugar de borrarse).

### RecurringItems (pagos y cobros recurrentes, v0.2)
| Columna | Tipo | Notas |
|---|---|---|
| Id, Name(60), Note(500), Archived, CreatedAt | | |
| Type | text | `Expense` (pago) · `Income` (cobro). Nunca `Transfer`. |
| CategoryId, AccountId | FK | Categoría del tipo correcto; cuenta habitual |
| EstimatedAmount | int (centavos) | ≥ 0. Puede ser 0 sólo si `VariableAmount`. |
| VariableAmount | bool | El monto cambia (luz, gas): se confirma al pagar |
| Frequency | text | `Monthly` · `Bimonthly` · `Quarterly` · `Semiannual` · `Yearly` |
| StartDate | datetime | Primer vencimiento: define mes de arranque y día |
| EndDate | datetime? | Último vencimiento posible (ej. préstamo) |
| RemindDaysBefore | int | 0–30 días de anticipación del aviso |

### Goals (metas de ahorro, v0.3)
| Columna | Tipo | Notas |
|---|---|---|
| Id, Name(60), Icon, Color, Note(500), SortOrder, Archived, CreatedAt | | |
| Currency | text(3) | `ARS` · `USD` |
| TargetAmount | int (centavos) | > 0, a valores de hoy |
| TargetDate | datetime? | Fecha deseada (opcional) |
| Tracking | text | `Contributions` (suma de aportes) · `LinkedAccount` (saldo de una cuenta) |
| AccountId | FK? | Sólo `LinkedAccount`; misma moneda que la meta. `SET NULL` si se borra la cuenta |
| AdjustForInflation | bool | Sólo en pesos (en dólares siempre false) |
| PlannedMonthly | int? | Aporte mensual planificado; null = usar el ahorro promedio |
| CompletedAt | datetime? | Marcada como cumplida (se archiva) |

### GoalContributions (aportes a metas)
`GoalId` (cascade), `Amount` (centavos; negativo = retiro), `Date`, `Note`. Son **asignaciones**: no mueven
plata entre cuentas, sólo marcan cuánto de tu plata es para la meta. No se puede retirar más de lo aportado.

### Budgets (presupuestos, v0.4)
`CategoryId` (único; sólo categorías de gasto; cascade), `MonthlyLimit` (centavos de pesos, > 0), `CreatedAt`.

## Presupuestos — `BudgetService`
- Gastado = gastos en **pesos** de la categoría del 1 al último día del mes (en el mes en curso, hasta hoy).
- Niveles: < 80 % bien · 80–100 % justo · > 100 % pasado.
- Proyección del mes en curso (desde el día 3): `gastado ÷ días transcurridos × días del mes`.
- Sugerencia: promedio de los últimos 3 meses completos con gasto en esa categoría, redondeado a $ 1.000 hacia arriba.
- Los presupuestos en riesgo se suman al aviso diario de Windows.

## Reportes — `ReportService`
- Serie mensual (sólo pesos): ingresos, gastos, prescindibles y patrimonio a fin de cada mes (mes actual: a hoy).
- Comparación con el mes anterior; en el mes en curso compara **del 1 a hoy contra el mismo tramo** del mes anterior.
- Variación real = `actual / (anterior × (1 + inflación mensual)) − 1`.

## Cotizaciones — `QuoteService` (v0.4)
- GET a `https://dolarapi.com/v1/dolares` (público, sin cuenta, sin datos del usuario). Mapea oficial, blue,
  bolsa (= MEP), cripto, tarjeta. Última descarga guardada en Settings (`quotes.snapshot`, JSON) → funciona sin internet.
- Se actualiza sola al abrir la app si la guardada tiene más de 3 horas. Desactivable (`quotes.enabled = false`).
- Patrimonio "todo junto" = pesos + dólares × **compra** del dólar elegido (`quotes.valuation`, por defecto MEP).
- Dólar de referencia de metas: el manual > la cotización guardada (venta) > el promedio de tus compras.

### RecurringSkips (vencimientos omitidos)
`(RecurringItemId, DueDate)` único. "Este mes no corresponde": el vencimiento deja de figurar como pendiente.

### Settings (preferencias)
Clave/valor. Claves usadas:
| Clave | Valor |
|---|---|
| `seeded.v1` | Fecha en que se cargaron los datos iniciales |
| `ui.theme` | `System` · `Light` · `Dark` |
| `onboarding.done` | Fecha en que se completó (o salteó) la bienvenida |
| `lock.pinHash` | `v1:{iteraciones}:{sal}:{hash}` PBKDF2-SHA256 del PIN (vacío = sin PIN). Nunca el PIN. |
| `lock.hello` | `true` = también desbloquea con Windows Hello |
| `quotes.snapshot` / `quotes.valuation` / `quotes.enabled` | Cotizaciones guardadas, dólar de valuación, consultar internet |
| `goals.monthlyInflation` / `goals.usdRate` | Inflación mensual estimada, dólar de referencia manual |
| `reminders.lastNotified` | Última fecha (yyyy-MM-dd) en que el modo `--check-due` mostró un aviso (máx. uno por día) |

## Reglas de negocio (implementadas en `TransactionService.NormalizeAndValidateAsync`)
- Monto > 0. Nota ≤ 500 caracteres. La fecha se guarda sin hora.
- **Gasto/ingreso**: requiere categoría del mismo tipo. Se limpian `ToAccountId`, `ToAmount`, `DollarType`.
  Un gasto sin necesidad explícita toma la `DefaultNecessity` de su categoría. Los ingresos no tienen necesidad.
- **Transferencia**: requiere destino distinto del origen. Sin categoría ni necesidad.
  - Misma moneda → `ToAmount = Amount`, `DollarType = null`.
  - Distinta moneda (**compra/venta de dólares**) → `ToAmount` obligatorio. Tipo de cambio pagado = `Amount / ToAmount` (ARS→USD).

## Vencimientos (v0.2) — `RecurringService` y `Schedule`
- Los vencimientos **no se guardan**: se calculan con `Schedule.DueDates` a partir de `StartDate`
  y `Frequency` (cada 1, 2, 3, 6 o 12 meses). Si el mes no tiene el día (31), se usa el último día.
- Estado de cada vencimiento:
  - **Pagado/Cobrado**: existe un movimiento con ese `RecurringItemId` + `DueDate`.
  - **Omitido**: existe un `RecurringSkip`.
  - **Vencido**: fecha < hoy (se buscan hasta 12 meses hacia atrás).
  - **Por vencer** (`DueSoon`): dentro de `RemindDaysBefore` días.
  - **Más adelante** (`Upcoming`).
- Pagar = crear un gasto/ingreso normal vinculado. Borrar ese movimiento vuelve a dejar el vencimiento pendiente.
- Reglas: el movimiento debe ser del mismo tipo que el pago recurrente; no se puede pagar dos veces el mismo vencimiento.

## Cuotas (v0.2) — `TransactionService.AddInstallmentsAsync`
- Una compra en N cuotas crea **N gastos**, uno por mes desde la fecha de compra, con el mismo `InstallmentGroup`.
- El total se divide en partes iguales; los centavos que sobran van en la primera cuota.
- Se puede borrar una cuota o todas. Editar afecta sólo a la cuota editada.

## Metas (v0.3) — `GoalService` y `GoalMath`
- **Llevás juntado**: suma de aportes (hasta hoy) o saldo a hoy de la cuenta vinculada.
- **Capacidad de ahorro**: promedios mensuales de los últimos 3 meses **completos** con movimientos
  (el mes en curso no cuenta; si no hay ninguno completo, se usa lo que va del mes). Ahorro = ingresos − gastos
  (las transferencias y compras de dólares no cuentan).
- **Ritmo de la meta**: el aporte planificado si existe; si no, el ahorro promedio. Para metas en dólares:
  ahorro en dólares + ahorro en pesos ÷ dólar de referencia.
- **Dólar de referencia**: el que elige el usuario o, si no, el promedio de sus compras de dólares de los últimos 3 meses.
- **Inflación** (metas en pesos con ajuste): el objetivo del mes n es `objetivo × (1 + i)^n`. Se busca el menor n con
  `llevás + ritmo × n ≥ objetivo(n)` (hasta 600 meses; si no, "a este ritmo no llegás").
- **Fecha deseada**: aporte necesario = `(objetivo(n) − llevás) / n`, redondeado hacia arriba.
- **Simulador**: suma al ritmo el X % del gasto prescindible promedio (en pesos; convertido en metas en dólares).

Preferencias en Settings: `goals.monthlyInflation` (fracción, por defecto 0.02) y `goals.usdRate` (pesos por dólar; vacío = automático).

## Saldos
```
saldo(cuenta, a hoy) = InitialBalance
              + Σ ingresos de la cuenta
              − Σ gastos de la cuenta
              − Σ transferencias que salen (Amount)
              + Σ transferencias que entran (ToAmount)
              (sólo movimientos con fecha ≤ hoy)
```
- Lo cargado con **fecha futura** (cuotas que faltan, pagos programados) se informa aparte en
  `AccountBalance.Future` ("Cuotas y pagos futuros" en la tarjeta de la cuenta). No afecta el saldo de hoy.
- **Patrimonio** por moneda = suma de saldos de cuentas activas con `IncludeInTotal`.
- **Totales del mes** (ingresos/gastos/prescindibles) **excluyen transferencias**: mover plata entre tus cuentas no es gastar.
- **Tarjeta de crédito**: los consumos son gastos de la cuenta tarjeta (su saldo queda negativo = deuda);
  pagar el resumen es una **transferencia** banco → tarjeta (no un gasto, si no se contaría dos veces).

## Migraciones
| Versión | Migración | Cambios |
|---|---|---|
| v0.1 | `InitialCreate` | Esquema inicial completo (incluye `DollarType`). |
| v0.4 | `Budgets` | Tabla `Budgets`. |
| v0.3 | `Goals` | Tablas `Goals` y `GoalContributions`. |
| v0.2 | `RecurringAndInstallments` | Tablas `RecurringItems`, `RecurringSkips`; en `Transactions`: `RecurringItemId`, `DueDate`, `InstallmentGroup/Number/Count`. |

Cómo crear una nueva: ver [DESARROLLO.md § Cambiar el esquema](DESARROLLO.md#cambiar-el-esquema-de-la-base).
La app aplica migraciones pendientes sola al iniciar (`Database.InitializeAsync`).
