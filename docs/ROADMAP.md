# Roadmap

Estado: ✅ hecho · 🚧 en curso · ⬜ pendiente

## v0.1 — Base para el uso diario ✅
- ✅ Solución .NET 10: `Cuentitas.Core` (lógica + datos), `Cuentitas.App` (WinUI 3), tests
- ✅ Base SQLite con EF Core y migraciones; datos por usuario de Windows
- ✅ Categorías argentinas precargadas (expensas, ABL, SUBE, prepaga, monotributo, aguinaldo…)
- ✅ Cuentas en pesos y dólares: efectivo, banco, billetera virtual, tarjeta, inversión; archivar
- ✅ Movimientos: gasto, ingreso, transferencia; **compra de dólares** con tipo de dólar y tipo de cambio
- ✅ Etiqueta necesario / prescindible (con sugerencia por categoría)
- ✅ Carga rápida: Ctrl+N, grilla de categorías, "Guardar y cargar otro"
- ✅ Inicio: patrimonio ARS/USD, resumen del mes, en qué se fue la plata, prescindibles, cuentas
- ✅ Movimientos: por mes, búsqueda, filtros, agrupado por día, editar/duplicar/eliminar
- ✅ Tema claro/oscuro/sistema, Mica, íconos Fluent (sin emojis)
- ✅ Copia de seguridad
- ✅ Publicación autocontenida (`tools\publish.ps1`)
- ✅ 31 tests del Core

## v0.2 — Servicios, vencimientos y sueldo ✅
- ✅ Pagos y cobros recurrentes: mensual, bimestral, trimestral, semestral, anual; monto fijo o variable; fecha de fin
- ✅ Plantillas rápidas argentinas (luz/gas/agua bimestrales, expensas, prepaga, ABL, monotributo, sueldo, aguinaldo…)
- ✅ Pantalla "Vencimientos": vencidos, próximos 7 días, resto del mes, pagados, omitidos; navegación por mes
- ✅ Pagar/Cobrar con un clic: abre el formulario precargado y vincula el movimiento al vencimiento
- ✅ Omitir un vencimiento; borrar el pago lo vuelve a dejar pendiente; no se puede pagar dos veces
- ✅ Tarjeta "Próximos vencimientos" en el Inicio y contador en el menú
- ✅ Notificaciones de Windows con la app cerrada (tarea programada opcional desde Ajustes, 10:00 + inicio de sesión)
- ✅ Sueldo y aguinaldo como cobros recurrentes (aguinaldo sugiere medio sueldo)
- ✅ Compras en cuotas (2 a 24): N gastos mensuales, "Cuota 2/6", borrar una o todas
- ✅ Saldos "a hoy" + "Cuotas y pagos futuros" por cuenta; movimientos futuros marcados "Programado"
- ✅ Instancia única (abrir dos veces trae al frente la ventana existente)
- ✅ 44 tests del Core
- ⬜ Probar en uso real: activar la tarea programada y el clic en la notificación (en modo desarrollo están desactivados)

## v0.3 — Metas de ahorro ✅
- ✅ Metas en pesos o en dólares, con monto objetivo y fecha deseada opcional
- ✅ Progreso con aportes/retiros (asignaciones) o con el saldo de una cuenta vinculada
- ✅ Capacidad de ahorro: promedios de los últimos 3 meses completos
- ✅ Proyección: "llegás en N meses (mes año)", o "a este ritmo no llegás"; aporte necesario para la fecha
- ✅ Aporte mensual planificado opcional por meta
- ✅ Metas en pesos ajustadas por inflación mensual estimada (editable); dólar de referencia automático o manual
- ✅ Simulador de recorte de prescindibles (0–100 %) + ranking por categoría
- ✅ Ideas rápidas (fondo de emergencia = 3 meses de gastos, vacaciones, juntar dólares, auto…)
- ✅ Tarjeta "Tus metas" en el Inicio
- ✅ 58 tests del Core

## v0.4 — Análisis y dólar ✅
- ✅ Cotización del dólar online (oficial, MEP, blue, tarjeta, cripto) con dolarapi.com, guardada para uso sin conexión
- ✅ Patrimonio "todo junto" en el Inicio con el dólar elegido; metas en dólares usan la cotización
- ✅ Reportes: ingresos vs gastos, necesario vs prescindible, patrimonio mes a mes, dona por categoría
- ✅ Comparación con el mes anterior ("a la misma altura del mes") con variación real descontando inflación
- ✅ Presupuestos mensuales por categoría: niveles, proyección al cierre, sugerencia por promedio, alertas
- ✅ Presupuestos en riesgo en el Inicio y en el aviso diario de Windows
- ✅ Exportar a Excel (3 hojas) y CSV desde Movimientos, Reportes y Ajustes
- ✅ 65 tests del Core

## v1.0 — Para compartir con la familia ✅
- ✅ Instalador por usuario (Inno Setup): menú Inicio, escritorio opcional, protocolo, desinstalador que limpia
- ✅ Actualizar = instalar encima, conservando los datos
- ✅ Bienvenida de 5 pasos: cuentas con saldos, sueldo + aguinaldo, servicios con vencimientos, avisos
- ✅ Bloqueo con PIN (hash PBKDF2) y Windows Hello opcional; Ctrl+L
- ✅ Restaurar copia de seguridad desde Ajustes (con copia automática de lo actual)
- ✅ 77 tests del Core
- ⬜ Probar en uso real: instalar con el Setup, activar avisos, PIN + Windows Hello, restaurar una copia

## Ideas para después
- ⬜ Mostrar en "Acerca de" si hay una versión nueva (requiere publicar las versiones en algún lado, ej. GitHub)
- ⬜ Movimientos recurrentes sin vencimiento (ej. ahorro automático mensual)
- ⬜ Adjuntar foto del comprobante a un movimiento
- ⬜ Importar movimientos desde el resumen del banco / Mercado Pago (CSV)

## v1.x — Inversiones ⬜
- ⬜ Plazo fijo (con interés y vencimiento), FCI, acciones/CEDEARs, cripto
- ⬜ Rendimiento en pesos y en dólares

---

## Bitácora
- **2026-10-14** — Inicio. Primera propuesta Flutter (Windows + APK) descartada: el usuario sólo necesita Windows.
  Se elige C# + WinUI 3. Se construye v0.1 completa. A pedido del usuario se reemplazan los emojis por
  íconos Fluent y se adapta todo al contexto argentino (ADR-007, ADR-009).
- **2026-10-14** — v0.2: vencimientos, cobros, cuotas, notificaciones (ADR-010 a 013). AppNotificationManager
  no funciona en apps autocontenidas: se usa la API clásica de toasts + protocolo `cuentitas:`.
- **2026-10-14** — v0.3: metas con proyección real, inflación y simulador de recortes (ADR-014).
- **2026-10-14** — v0.4: cotización del dólar online (ADR-015), reportes con gráficos propios y Excel (ADR-016), presupuestos.
- **2026-10-14** — v1.0: bienvenida, PIN/Windows Hello (ADR-017), restaurar copias, instalador Inno Setup (ADR-018).
