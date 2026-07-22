# Facturas duplicadas en la planilla de Gastos 2026 (a resolver antes de migrar)

**Contexto:** al preparar el test de aceptación end-to-end de F5c (Task 9 — el que corre
`/analizar` → `/confirmar` con las planillas reales y verifica contra la base), el sistema
rechazó la importación con un error de validación: dos proveedores tienen, cada uno, el mismo
número de factura repetido en dos renglones distintos del libro de caja. Este documento junta
los cuatro renglones involucrados para que se puedan comparar contra los comprobantes de papel
y decidir qué hacer.

## 1. Por qué esto frena la migración (explicación sin tecnicismos)

El sistema guarda un gasto identificado por **proveedor + número de factura**: no puede haber
dos gastos activos del mismo proveedor con el mismo número de factura. Esa regla no es nueva de
esta importación — existe desde antes en la base, y se aplica **también si alguien carga los
gastos a mano desde la aplicación**, no solo al importar la planilla. Es decir: aunque no
migráramos nada y alguien intentara cargar estos dos pares de renglones manualmente, uno por
uno, la aplicación los rechazaría igual en el segundo intento.

La planilla de Gastos 2026 tiene 4 renglones (2 pares) donde el mismo proveedor repite el mismo
número de factura en dos fechas/renglones distintos, con importes y números de orden diferentes.
El importador no puede decidir por su cuenta si eso es un error de tipeo en la planilla o si son
dos pagos reales de una misma factura — necesita que una persona lo confirme mirando los papeles.
Hasta que eso se resuelva, esos 4 renglones no se pueden migrar.

## 2. Resumen

| | |
|---|---:|
| Pares (proveedor, factura) en conflicto | **2** |
| Renglones de la planilla involucrados | **4** |
| De esos, renglones entre enero y junio (afectan el saldo de caja a junio) | **4** |
| Suma de los montos en conflicto | **$ 7.648** |

Los dos pares de facturas repetidas son del **mismo proveedor** (GARAY POZO HERNÁN) y los 4
renglones caen en **ENERO** — o sea, los 4 afectan el cálculo del saldo de caja a junio 2026.
No se encontró ningún otro proveedor ni ningún otro mes con el mismo problema: este es el
panorama completo de la planilla de Gastos, no una muestra parcial.

*(Método: se parseó `PlanillaGastos2026.ods` con el mismo parser que usa el importador —
`PlanillaOdsParser.ParsearGastos`, StockApp.Infrastructure — sobre las 12 hojas mensuales, y se
agruparon los 341 renglones de egreso por proveedor + número de factura normalizados
(recorte de espacios + mayúsculas, la misma normalización que aplica
`ConfirmacionImportacionService.Normalizar` al validar). Se buscaron TODOS los grupos con más de
un renglón, no solo los de GARAY POZO HERNÁN.)*

## 3. Los renglones, uno por aparición

### Conflicto 1 — GARAY POZO HERNÁN, factura **82446**

| Hoja (mes) | Fila en la planilla | Fecha | N° de orden | Monto | Detalle | Destino |
|---|---:|---|---|---:|---|---|
| ENERO | 44 | 2026-01-23 | 865813 | $ 263 | PAPEL HIGIÉNICO | MANT OPERATIVO |
| ENERO | 63 | 2026-01-23 | 865901 | $ 6.407 | BOLSAS PARA RESIDUOS | MANT OPERATIVO |

### Conflicto 2 — GARAY POZO HERNÁN, factura **82447**

| Hoja (mes) | Fila en la planilla | Fecha | N° de orden | Monto | Detalle | Destino |
|---|---:|---|---|---:|---|---|
| ENERO | 62 | 2026-01-23 | 865900 | $ 526 | PAPEL HIGIÉNICO | MANT OPERATIVO |
| ENERO | 152 | 2026-01-29 | 867363 | $ 452 | CLORO Y DESINFECTANTE | MANT OPERATIVO |

*(La "fila en la planilla" es el número de renglón tal como se ve en LibreOffice/Excel dentro
de la hoja del mes correspondiente.)*

## 4. Lo que se ve en los datos (sin decidir por nadie)

- En **los dos conflictos**, el número de orden es distinto entre los dos renglones (865813 vs
  865901; 865900 vs 867363) — nunca se repite el mismo número de orden bajo la misma factura.
  Eso apunta más a "una factura, varias imputaciones" que a un simple tipeo, porque un error de
  tipeo típicamente reutiliza también los demás datos de una fila copiada.
- El detalle de cada renglón es distinto dentro de cada par (PAPEL HIGIÉNICO / BOLSAS PARA
  RESIDUOS en el primer conflicto; PAPEL HIGIÉNICO / CLORO Y DESINFECTANTE en el segundo) y el
  destino es el mismo en los 4 renglones (MANT OPERATIVO) — compatible con una factura que
  agrupa varios artículos o varias órdenes de compra del mismo rubro.
- En el conflicto de la factura 82447, las dos fechas difieren en 6 días (23 y 29 de enero) — un
  desfasaje así es menos típico de "una sola factura con varias líneas" y más fácil de explicar
  si son dos facturas reales distintas que alguien tipeó igual por error.
- No hay ningún caso, en ninguno de los dos conflictos, donde monto y orden coincidan entre las
  dos filas — descarta que sea la MISMA fila cargada dos veces por error en la planilla (eso se
  vería como un duplicado idéntico, no es el caso acá).

## 5. Preguntas para responder mirando los comprobantes de papel

Para cada uno de los dos conflictos:

1. **¿Son dos facturas distintas y una de las dos está mal tipeada en la planilla?**
   Si es así, se corrige el número de factura en el `.ods` (o en los datos de origen) antes de
   migrar — no hace falta tocar el sistema.
2. **¿Es una sola factura real, pagada o imputada en varias partes, cada una con su propio
   número de orden?**
   Si es así, la regla que usa el sistema para identificar un gasto (proveedor + factura,
   sin el número de orden) está mal pensada para este caso, y hay que ampliarla para que
   contemple también el número de orden — eso sí requiere un cambio de esquema (una migración),
   no solo corregir un dato.

## 6. Cierre

Hasta que se responda esto (conflicto por conflicto, mirando los papeles), la migración real de
estos 4 renglones no se puede correr — ni por el importador ni cargándolos a mano. El resto de
la fase F5c (análisis, confirmación, dedupe, reversa, auditoría — Tasks 1 a 8) está terminado y
probado; el test de aceptación final (Task 9) queda a la espera de esta decisión antes de poder
cerrar limpio contra la planilla completa.

## 7. Decisión tomada (sin acceso a los comprobantes)

Se decidió **ampliar el índice único de la base** para que la clave de unicidad de un gasto pase
de (Proveedor, Factura) a (Proveedor, Factura, N° de Orden) — migración
`AmpliaIndiceFacturaConNumeroOrden`, índice `IX_Gastos_ProveedorId_NumeroFactura_NumeroOrden`. Con
esto, los 4 renglones de los dos conflictos se migran sin tocar ningún dato de la planilla: en
ambos pares el número de orden ya es distinto, así que quedan como dos gastos separados, cada uno
con su propio número de orden — exactamente como están tipeados hoy en el archivo.

**Por qué se pudo decidir esto sin mirar los comprobantes de papel** (pregunta 2 de la sección 5,
no la 1): el dato entra correctamente bajo las DOS lecturas posibles que planteaba la sección 5.

- Si es una sola factura real pagada/imputada en varias partes (lectura 2), el sistema ahora la
  representa tal cual: dos gastos, mismo número de factura, cada uno con su propio número de
  orden — que es justamente el caso para el que se amplió el índice.
- Si en cambio alguno de los dos números de factura fuera un tipeo de la planilla (lectura 1), el
  sistema igual los acepta sin error — no hay ninguna validación que dependa de que la factura sea
  "correcta", y el número de factura **no participa de ningún total ni de ningún cálculo**: ni el
  saldo de caja, ni el control POA, ni la valorización de stock usan NumeroFactura para sumar o
  filtrar nada (es un dato identificatorio, no operativo). Migrar con el número tal como está en
  la planilla, sea correcto o no, no distorsiona ningún saldo.

En ambos casos el dato migrado es correcto o, en el peor caso, inocuo — por eso no hacía falta
esperar a los papeles para decidir el cambio de esquema.

**Pendiente**: si alguna vez aparece el comprobante de la factura **82447** (el segundo conflicto,
GARAY POZO HERNÁN), vale la pena revisarlo — sus dos renglones están separados por 6 días (23 y 29
de enero), un desfasaje menos típico de "una sola factura con varias líneas" que el del conflicto
82446 (mismo día para las dos filas). Si resulta ser un tipeo, se corrige editando el gasto
correspondiente desde el desktop (Finanzas → Gastos → Editar): es un cambio de dato puntual, sin
impacto en ningún saldo ya calculado, porque NumeroFactura no interviene en ningún total.
