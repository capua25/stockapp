# Discrepancias de la planilla POA 2026 (a reconciliar antes de migrar)

**Contexto:** F5b cerró el criterio de aceptación §11 leyendo `SaldosPoa` (hoja "SALDO TOTALES" del
.ods, cacheada por LibreOffice) en vez de sumar `SaldoPlanilla` por Literal a través de
`LineasPoa`. Esta nota documenta POR QUÉ: las dos fuentes NO coinciden en la planilla real
(`PlanillaPoa2026.ods`), y hay que reconciliar la planilla ANTES de correr la migración one-shot
(F5c), porque F5c va a escribir en la base lo que digan las hojas de LÍNEA (no el resumen).

## 1. El parser ahora lee COMPOSTERAS completa (financiamiento mixto)

Antes de F5b, el parser (F5a) leía la hoja "COMPOSTERAS Y COMPACTADORAS" tomando el bloque
PRESUPUESTO/SALDO **agregado** (col8/col10 de la hoja, colspan=2) — presupuesto=1.500.000,
saldo=1.500.000 — y el primer LITERAL que encontraba en esa fila (LITERAL C), perdiendo por
completo la asignación LITERAL B.

F5b corrige esto: la hoja tiene en realidad DOS asignaciones apiladas bajo un segundo par de
columnas PRESUPUESTO/SALDO compartido (col14/col15, colspan=1), una por cada literal:

| Literal | Presupuesto | Saldo |
|---|---:|---:|
| C | 1.407.252 | 1.407.252 |
| B | 92.748 | 92.748 |
| **Suma** | **1.500.000** | **1.500.000** |

La suma cuadra EXACTO contra el bloque agregado (1.407.252 + 92.748 = 1.500.000), confirmando que
el agregado es la suma de las dos asignaciones reales, no una tercera asignación independiente.

## 2. La hoja "SALDO TOTALES" está DESINCRONIZADA de sus hojas de línea

Sumando `SaldoPlanilla` por Literal a través de las 15 hojas de línea (con COMPOSTERAS ya
corregida) y comparando contra los valores cacheados de la hoja "SALDO TOTALES":

| Literal | Suma de las hojas de línea | Cacheado en "SALDO TOTALES" | Diferencia |
|---|---:|---:|---:|
| B | 6.341.849 | 6.643.349 | **−301.500** |
| C | 4.174.206 | 4.654.206 | **−480.000** |

### Literal B: diferencia explicada

La hoja "MEJORAS EN PLUVIALES" (Literal B) tiene presupuesto=1.500.000 y saldo=1.198.500
(diferencia interna de 301.500 sin movimientos que la expliquen — el parser no inventa datos, lee
lo que está cacheado). La propia hoja "SALDO TOTALES" trae una anotación manual, en la misma zona
donde se cachea el total, con el texto exacto:

> "diferencia caños en mejora pluviales"

Esa anotación coincide en magnitud (301.500) y en la hoja señalada (MEJORAS EN PLUVIALES) con la
diferencia observada: el gasto de 301.500 (caños) existe y está reflejado en el saldo de la
hoja de línea, pero el total cacheado de "SALDO TOTALES" nunca se actualizó para reflejarlo.

### Literal C: diferencia SIN explicación

La diferencia de 480.000 en Literal C no coincide con ningún gasto individual de las hojas de
línea de esa fuente (LUCES DE NAVIDAD, ROPA PARA FUNCIONARIOS, EVENTOS, PRENSA, COMPRA
CONTENEDORES, COMPOSTERAS-C), ni hay ninguna anotación manual equivalente en "SALDO TOTALES" que
la explique. Queda abierta.

## 3. La hoja EVENTOS está descuadrada contra sus propios movimientos

Este caso es distinto a los dos anteriores: no es el resumen ("SALDO TOTALES") contra las hojas,
sino una hoja de línea descuadrada contra sus propios movimientos.

La hoja "EVENTOS" (Literal C) declara presupuesto=2.505.700 y saldo=2.205.700, es decir que según
la hoja se consumieron **300.000**. Pero la hoja tiene un solo movimiento cargado: fila 14, orden
"SUMINISTRO 068555", importe **150.000**, sin proveedor y sin número de factura (es un
compromiso, no un gasto facturado).

Faltan 150.000 en movimientos: el saldo de la hoja dice que se gastó el doble de lo que está
detallado en sus propias filas.

Esto no es un dato mal tipeado que se pueda corregir a ojo: el movimiento faltante **no existe en
la planilla**. No hay fila, no hay importe, no hay fecha a la que atribuirlo. Sin el comprobante o
el registro original de ese gasto, nadie puede reconstruir de dónde salen esos 150.000.

**Consecuencia para la migración:** F5c migra los movimientos reales de cada hoja, no el saldo
cacheado. Como el único movimiento migrable de EVENTOS es el de 150.000, la línea POA de EVENTOS
va a quedar en la base con 150.000 más de disponible que lo que dice la planilla. Dos escenarios:

- Si ese gasto de 150.000 existió de verdad, hay que agregar el movimiento faltante al .ods
  ANTES de migrar.
- Si no existió y el saldo de la hoja estaba mal calculado, no hay nada que corregir: el número
  que va a quedar en la base es el correcto.

## 4. Recomendación

Reconciliar la planilla ANTES de correr la migración one-shot real (F5c):

1. Actualizar el total cacheado de Literal B en "SALDO TOTALES" para reflejar el gasto de
   caños de MEJORAS EN PLUVIALES (−301.500), o corregir el saldo de la hoja de línea si el ajuste
   correcto va del otro lado.
2. Investigar el origen de los 480.000 de diferencia en Literal C — no atribuible a ningún gasto
   individual visible en las hojas de línea actuales.
3. Resolver el descuadre interno de la hoja EVENTOS (150.000 sin movimiento que los explique):
   confirmar si el gasto existió y cargar el movimiento faltante al .ods, o dejar constancia de
   que el saldo de la hoja estaba mal calculado.

Mientras esto no se resuelva, el criterio de aceptación §11 (`SaldosPoa`) sigue siendo la fuente
de verdad para F5b porque es un valor cacheado único y estable de la planilla, pero **F5c va a
escribir en la base las `AsignacionPresupuestal` derivadas de las hojas de LÍNEA** (que son las
que tienen detalle transaccional) — no del resumen. Si la planilla no se reconcilia antes, la
base de datos migrada quedará con los mismos 301.500 / 480.000 de diferencia respecto de "SALDO
TOTALES".
