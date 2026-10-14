# Sistema de diseño

Objetivo: una app **sobria y profesional**, nativa de Windows 11 (Fluent Design), cómoda para
uso diario y fácil para cualquier integrante de la familia.

## Principios
1. **Los números son lo importante**: montos grandes, en negrita y con tipografía de cifras tabulares.
2. **Cargar un gasto en menos de 5 segundos**: Ctrl+N → monto → clic en categoría → Enter.
3. **Calma visual**: los gastos se muestran en color de texto normal con signo "−"; sólo los
   ingresos van en verde. El rojo se reserva para alertas, no para cada gasto.
4. **Sin emojis.** Íconos monocromáticos de Segoe Fluent Icons (los del propio Windows).
5. **Vocabulario argentino** y voseo: "¿Cuánto gastaste?", "Pagaste con", expensas, SUBE, aguinaldo, MEP.

## Color (definidos en `Styles/Theme.xaml`)
| Token | Claro | Oscuro | Uso |
|---|---|---|---|
| Acento (`SystemAccentColor`) | `#0F9D8A` | `#2BB8A4` | Marca: botones principales, selección |
| `IncomeBrush` | `#138A4B` | `#4CD088` | Ingresos, balance positivo |
| `ExpenseBrush` | `#C8372D` | `#FF7A6E` | Balance negativo, errores |
| `OptionalBrush` | `#B26A00` | `#F2B54A` | Gastos prescindibles |
| `HeroBrush` | degradé teal → azul petróleo | más oscuro | Tarjeta de patrimonio |
| `*SoftBrush` | mismo color al ~10–15% | ~15% | Fondos de etiquetas y tarjetas destacadas |

Paleta de cuentas/categorías (`AppIcons.Colors`): 12 tonos medios que funcionan en ambos temas.
Respeta el modo **alto contraste** de Windows (diccionario `HighContrast`).

## Tipografía
- Segoe UI Variable (la de Windows 11). Montos grandes: *Segoe UI Variable Display* SemiBold.
- Estilos: `PageTitleStyle` (28), `CardTitleStyle` (15 semibold), `LabelStyle` (12, gris),
  `AmountHeroStyle` (34), `AmountLargeStyle` (22), `AmountStyle` (alineado a la derecha).

## Formatos (Argentina)
- Dinero: `$ 1.234,56` · `US$ 1.234,56` · signo: `-$ 500,00`, `+$ 85.000,00`.
- Fechas: "Hoy", "Ayer", "Martes 14 de octubre"; cortas "14 oct". Semana empieza el lunes.
- Al tipear montos se acepta `1500`, `1.500`, `1500,50` y también `1500.50`.

## Íconos
- Catálogo en `Helpers/AppIcons.cs`: la base guarda la **clave** (`cart`, `bolt`, `bank`…).
- `Controls/IconBadge`: glifo del color de la categoría sobre un círculo del mismo color suavizado.
- Efectivo y dólares usan texto (`$`, `US$`) en lugar de glifo.
- Códigos verificados contra `C:\Windows\Fonts\SegoeIcons.ttf`. Si agregás uno, verificá que exista.

## Layout
- Ventana: barra de título propia, fondo **Mica**, menú lateral (`NavigationView`) que se compacta en ventanas angostas.
- Contenido con ancho máximo 1240 px (900 en Categorías y Ajustes), márgenes 32 px.
- Tarjetas: radio 12, borde sutil, padding 20. Espaciado en múltiplos de 4.
- Inicio en dos columnas desde 1180 px; en menos, una columna.

## Pantallas (v0.1)
| Pantalla | Contenido |
|---|---|
| **Inicio** | Patrimonio en pesos y dólares + "Comprar dólares"; ingresos/gastos/balance del mes; últimos movimientos; "¿En qué se fue la plata?"; gastos prescindibles y cuánto ahorrarías recortando la mitad; cuentas. |
| **Movimientos** | Navegación por mes, búsqueda (en todos los meses), filtros por tipo/cuenta/necesidad, resumen, lista agrupada por día con total diario. Clic = editar; clic derecho = editar/duplicar/eliminar. |
| **Vencimientos** (v0.2) | Resumen: vencidos, próximos 7 días, falta pagar en el mes. Agenda del mes agrupada (Vencidos · Próximos 7 días · Más adelante · Pagados · Omitidos) con botón **Pagar/Cobrar**. A la derecha, los pagos y cobros configurados. Clic derecho: omitir, ver el pago, editar. El menú lateral muestra un contador (rojo si hay vencidos). |
| **Metas** (v0.3) | "Tu capacidad de ahorro" (ingresos, gastos, ahorro y prescindibles promedio; inflación mensual y dólar de referencia editables). Simulador "¿Y si recortás tus gastos prescindibles?" con control deslizante y ranking por categoría. Tarjetas de metas: progreso, cuándo llegás (color: verde cumplida, ámbar si no llegás a la fecha, rojo si no llegás nunca), aporte necesario para la fecha, efecto de la inflación y del recorte. Botones Aportar / Aportes / más opciones. El Inicio muestra hasta 3 metas. |
| **Presupuestos** (v0.4) | Total del mes, una fila por categoría con barra (verde/ámbar/rojo), "te quedan / te pasaste", proyección al cierre del mes. Abajo: categorías con gasto y sin tope, con "Ponerle tope" (sugiere tu promedio). Navegación por mes. |
| **Reportes** (v0.4) | 6/12 meses. Indicadores (ahorro promedio, % prescindible, mejor mes, variación del patrimonio). Gráficos: ingresos vs gastos, necesario vs prescindible (apilado), patrimonio a fin de mes. Detalle del mes: dona por categoría con leyenda y comparación con el mes anterior (nominal y real). Botón Exportar a Excel. |
| **Cuentas** | Tarjetas con saldo y moneda, totales por moneda, "Cuotas y pagos futuros", archivadas aparte. |
| **Categorías** | Gastos / Ingresos, necesidad por defecto, archivadas al pie. |
| **Inicio** (agregados v0.4) | "Todo junto: $ X" con el dólar elegido y la hora de la cotización; tarjeta "Presupuestos en riesgo". |
| **Bienvenida** (v1.0) | Pantalla completa sin menú, 5 pasos con barra de progreso: presentación (privacidad, dólar, avisos) · ¿dónde tenés tu plata? (efectivo + cuentas sugeridas: caja de ahorro, Mercado Pago, Ualá, tarjeta como deuda, dólares) · tu sueldo (neto, día, cuenta, aguinaldo) · servicios (luz/gas/agua bimestrales, internet, celular, alquiler, expensas, prepaga, streaming; día, monto o "varía") · ¡todo listo! (tips + activar avisos). "Saltear" siempre disponible. |
| **Bloqueo** (v1.0) | Pantalla que tapa todo: ícono, "Cuentitas está bloqueada", PIN, Desbloquear, "Usar Windows Hello". Ctrl+L. |
| **Exportar** | Desde Movimientos, Reportes o Ajustes: formato (Excel/CSV) y período → elegir archivo → "Abrir archivo". |
| **Ajustes** | Tema, seguridad (PIN, Windows Hello, cambiar PIN, bloquear ahora), cotización del dólar (dólar de valuación, cotizaciones, actualizar, apagar internet), avisos de vencimientos (tarea programada + probar notificación), ubicación de datos, copia de seguridad, versión. |
| **Nuevo pago o cobro recurrente** | Pago/Cobro; plantillas rápidas (Luz y Gas bimestrales, Expensas, Prepaga, ABL, Monotributo, Sueldo, Aguinaldo…); categoría, cuenta, monto estimado o "varía"; frecuencia, próximo vencimiento, días de aviso, fecha de fin opcional. |
| **Pagar vencimiento** | El mismo formulario de movimiento, precargado (monto estimado, cuenta, categoría) y con aviso del vencimiento que se paga. |
| **Nuevo movimiento** | Gasto / Ingreso / Transferencia; monto grande; fecha; cuenta; grilla de categorías; necesario/prescindible; nota. En transferencias entre monedas: monto recibido, **tipo de dólar** y tipo de cambio calculado. "Guardar y cargar otro" para cargas en serie. En gastos nuevos: **Cuotas** (1, 2, 3, 6, 9, 12, 18, 24) con el valor de cada cuota. |
