# Registro de decisiones (ADR)

Cada decisión importante con su contexto, para no volver a discutirla sin motivo.

---

## ADR-001 · C# + WinUI 3 (sólo Windows)
**Fecha:** 2026-10-14 · **Estado:** aceptada (reemplaza la propuesta inicial de Flutter)

**Contexto:** el usuario sólo necesita el programa para **Windows 11** (descartó el APK de Android).
Quiere un resultado profesional.

**Decisión:** C# / .NET 10 (LTS) + WinUI 3 (Windows App SDK), app de escritorio nativa.

**Alternativas evaluadas:** Tauri + React (más libre en diseño, instalador chico, requiere Rust);
Electron (pesado, ~150 MB). Se eligió WinUI 3 por look nativo de Windows 11 (Fluent, Mica),
rendimiento y ecosistema .NET maduro.

**Consecuencia:** si algún día se quiere móvil, el proyecto `Cuentitas.Core` (sin dependencias de
Windows) se puede reutilizar desde .NET MAUI o una API.

---

## ADR-002 · Local-first con SQLite (EF Core), sin servidor
**Fecha:** 2026-10-14 · **Estado:** aceptada

**Contexto:** "cada uno con su cuenta". Datos financieros = privados.

**Decisión:** SQLite local en `%LOCALAPPDATA%\Cuentitas` → cada usuario de Windows tiene su propia base
automáticamente. Sin login. EF Core 10 con migraciones. Respaldo con `VACUUM INTO`.

**Consecuencia:** no hay sincronización entre computadoras. Para usar la misma cuenta en dos PCs habría
que agregar exportar/importar o sincronización (evaluar en el futuro).

---

## ADR-003 · Montos en centavos enteros (`long`)
**Fecha:** 2026-10-14 · **Estado:** aceptada

Evita errores de redondeo y permite sumar en SQL (SQLite no tiene `decimal` nativo). La conversión a
texto se hace sólo en `Money.Format/Parse`.

---

## ADR-004 · Multimoneda por cuenta; compra de dólares = transferencia
**Fecha:** 2026-10-14 · **Estado:** aceptada

Cada cuenta tiene moneda (ARS o USD). Comprar dólares es una transferencia ARS → USD con dos montos;
el tipo de cambio pagado queda registrado para siempre. No se convierten monedas automáticamente
(evita mezclar cotizaciones). Patrimonio total equivalente: con cotización online (ver roadmap).

---

## ADR-005 · MVVM con CommunityToolkit.Mvvm + DI de Microsoft
**Fecha:** 2026-10-14 · **Estado:** aceptada

Propiedades `[ObservableProperty]` parciales (compatibles con AOT), `[RelayCommand]`,
`Microsoft.Extensions.DependencyInjection`. Vistas con `x:Bind` (compilado).

---

## ADR-006 · App sin empaquetar (no MSIX), autocontenida
**Fecha:** 2026-10-14 · **Estado:** aceptada

`WindowsPackageType=None` + `WindowsAppSDKSelfContained=true` + `--self-contained`: se copia una carpeta
y se ejecuta `Cuentitas.exe`, sin instalar .NET ni certificados. Ideal para pasarla a la familia.
Requiere `EnableMsixTooling=true` para que `publish` incluya `Cuentitas.pri`. Costo: ~290 MB en disco
(~110 MB zip). Un instalador (MSIX o Inno Setup) queda para v1.0.

---

## ADR-007 · Íconos por clave, glifos de Segoe Fluent Icons, sin emojis
**Fecha:** 2026-10-14 · **Estado:** aceptada (revisada: primero se usaron emojis)

El usuario pidió **no usar emojis** porque restan profesionalismo. Se guarda una clave (`cart`) y la app
la traduce a un glifo de Segoe Fluent Icons (incluida en Windows 11), monocromático y coherente con el
sistema. Nunca se guardan códigos de glifo en la base (permite cambiar el diseño sin migrar datos).

---

## ADR-008 · Clasificación "necesario / prescindible"
**Fecha:** 2026-10-14 · **Estado:** aceptada

Cada gasto se marca necesario o prescindible (con valor sugerido por categoría). Es la base de las metas:
"si recortás lo prescindible, llegás en X meses". Se guarda desde v0.1 para tener historial.

---

## ADR-009 · Contexto argentino
**Fecha:** 2026-10-14 · **Estado:** aceptada

- Categorías iniciales locales: expensas, ABL, prepaga/obra social, SUBE, monotributo/ganancias,
  comisiones bancarias, cuotas; ingreso **Aguinaldo**.
- **Tipo de dólar** en cada compra/venta (oficial, MEP, blue, tarjeta, cripto): en Argentina conviven
  varias cotizaciones y el usuario quiere saber a cuánto compró realmente.
- Tarjeta de crédito modelada como cuenta con saldo negativo; pagar el resumen es una transferencia.
- Formato `$ 1.234,56`, fechas en es-AR, semana desde el lunes.
- Pendiente (roadmap): cotización online, ajuste de metas por inflación.

---

## ADR-010 · Notificaciones con la API clásica de Windows + tarea programada
**Fecha:** 2026-10-14 · **Estado:** aceptada

**Contexto:** se quiere avisar vencimientos aunque la app esté cerrada. `AppNotificationManager` (Windows App SDK)
falla en apps autocontenidas: depende del paquete "Singleton" del runtime (error al cargar
`Microsoft.WindowsAppRuntime.Insights.Resource.dll`).

**Decisión:**
- Notificaciones con `Windows.UI.Notifications` (incluida en Windows) + un AppUserModelID en HKCU.
- Clic → protocolo `cuentitas:bills` registrado en HKCU → abre/trae la app en Vencimientos.
- Avisos con la app cerrada: tarea programada del usuario (`schtasks`, sin admin) que corre
  `Cuentitas.exe --check-due` a las 10:00 y al iniciar sesión, con "ejecutar si se perdió". Opcional, desde Ajustes.
- Máximo un aviso por día (`reminders.lastNotified`).

**Alternativas descartadas:** dejar la app residente en la bandeja (consume memoria siempre; más complejo);
exigir instalar el Windows App Runtime (rompe "copiar la carpeta y listo").

---

## ADR-011 · Vencimientos calculados, pagos vinculados
**Fecha:** 2026-10-14 · **Estado:** aceptada

Los vencimientos no se materializan en la base: se calculan desde `StartDate` + `Frequency`. Pagar crea un
movimiento normal con `RecurringItemId` + `DueDate`. Ventajas: cambiar la frecuencia o el día no deja
"vencimientos huérfanos", los reportes de gastos no cambian, y borrar el pago reabre el vencimiento.
Sólo se guarda lo excepcional: omisiones (`RecurringSkips`).

---

## ADR-012 · Cuotas como N movimientos con fecha futura; saldo "a hoy"
**Fecha:** 2026-10-14 · **Estado:** aceptada

En Argentina se compra mucho en cuotas: cada cuota impacta en un mes distinto. Se crean N gastos mensuales
agrupados. Para que el saldo y los totales del mes no cuenten plata que todavía no salió, **los saldos se
calculan a hoy** y lo futuro se muestra aparte ("Cuotas y pagos futuros"). Los movimientos futuros se marcan
"Programado" en la lista.

---

## ADR-014 · Metas: aportes virtuales, proyección con ahorro real e inflación
**Fecha:** 2026-10-14 · **Estado:** aceptada

- **Aportes = asignaciones**, no movimientos: en Argentina la plata para una meta suele estar mezclada en la
  caja de ahorro o en Mercado Pago. Alternativa para quien separa: vincular la meta a una cuenta (ej. dólares)
  y usar su saldo.
- **Ritmo por defecto = ahorro promedio real** (últimos 3 meses completos), no un número inventado. Cada meta
  muestra "si destinás tu ahorro promedio": no se reparte entre metas (sería arbitrario); para eso está el
  aporte planificado por meta.
- **Inflación mensual compuesta** editable (2 % por defecto) para metas en pesos: sin esto, una meta en pesos a
  un año queda vieja. Metas en dólares no se ajustan.
- **Simulador de recortes** sobre lo marcado como prescindible (ADR-008), con ranking por categoría.
- Montos de proyección redondeados a la unidad (son estimaciones).

---

## ADR-015 · Cotización del dólar online (único uso de internet)
**Fecha:** 2026-10-14 · **Estado:** aceptada

**Contexto:** para ver el patrimonio "todo junto" y proyectar metas en dólares hace falta la cotización del día.
En Argentina conviven varios dólares.

**Decisión:** GET a dolarapi.com (pública, gratuita, sin registro). Se guarda la última respuesta en la base;
sin conexión se usa esa. Se puede desactivar en Ajustes. **Nunca se envía información del usuario**: no hay
parámetros ni cuentas, sólo se descarga la cotización. Los dólares se valúan al precio **comprador** (lo que te
darían si los vendés); las metas usan el **vendedor** (lo que te cuesta comprarlos).

---

## ADR-016 · Gráficos propios y ClosedXML para Excel
**Fecha:** 2026-10-14 · **Estado:** aceptada

- **Gráficos:** controles propios sobre `Canvas` (barras agrupadas/apiladas, línea, dona) en lugar de una librería
  (LiveCharts2 suma SkiaSharp y depende de versiones de WinUI). Son ~300 líneas, usan los colores del tema y
  tienen tooltips. Si en el futuro hacen falta zoom o muchas series, reevaluar.
- **Excel:** ClosedXML (MIT) genera .xlsx reales con formato numérico (se pueden sumar). El CSV usa `;` y coma
  decimal porque Excel en español lo abre así directamente.

---

## ADR-017 · Bloqueo con PIN de la app + Windows Hello
**Fecha:** 2026-10-14 · **Estado:** aceptada

**Contexto:** la app se comparte con la familia; la protección principal es que cada persona use su propio
usuario de Windows. A veces la sesión queda abierta.

**Decisión:** PIN opcional de 4–8 números propio de la app, guardado como hash PBKDF2-SHA256 (200.000 iteraciones,
sal aleatoria), comparación en tiempo constante, espera de 30 s tras 5 intentos. Windows Hello opcional como segunda
forma de desbloqueo. **No se cifra la base** (lo decimos en la UI): cifrar implicaría manejar claves y recuperar
datos si se olvida, con más riesgo de perder información que beneficio para un uso familiar.

**Si se olvida el PIN y no hay Windows Hello:** borrar el valor `lock.pinHash` de la tabla Settings (ver
DESARROLLO.md). Es intencionalmente posible: el PIN protege la pantalla, no es una bóveda.

---

## ADR-018 · Instalador con Inno Setup, por usuario
**Fecha:** 2026-10-14 · **Estado:** aceptada

MSIX exige firmar con un certificado de confianza (costo o instalar certificados en cada PC). Inno Setup genera un
`.exe` clásico: instalación **por usuario** (sin permisos de administrador) en `%LOCALAPPDATA%\Programs\Cuentitas`,
acceso directo en el menú Inicio con el AppUserModelID de las notificaciones, protocolo `cuentitas:`, desinstalador
que quita la tarea de avisos y pregunta si borrar los datos. **Actualizar = instalar la versión nueva encima**
(los datos están aparte y se migran solos). No hay actualización automática: no hay servidor (ADR-002).

---

## ADR-013 · Instancia única
**Fecha:** 2026-10-14 · **Estado:** aceptada

Dos ventanas sobre la misma base confunden (cambios que no se ven en la otra). `AppInstance` con una clave por
carpeta de datos: una segunda apertura trae al frente la existente. La carpeta de desarrollo usa otra clave,
así la versión de pruebas puede convivir con la real.
