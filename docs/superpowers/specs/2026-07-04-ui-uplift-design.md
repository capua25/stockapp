# StockApp UI Kit — Capa de diseño sobre Fluent

**Fecha:** 2026-07-04
**Objetivo:** Elevar la UX/UI de StockApp de "funcional pero crudo (Fluent default)" a un aspecto profesional tipo dashboard/SaaS, para una demo con cliente. Estrategia: capa de diseño ADITIVA sobre FluentTheme (no lo reemplaza), aplicada globalmente para levantar las ~20 vistas de una sola vez, con bajo riesgo y reversibilidad.

## Contexto actual (diagnóstico)
- Avalonia 12.0.4, .NET 10, MVVM con CommunityToolkit.Mvvm. Presentation csproj en Version 0.1.5.
- No existe sistema de diseño: `App.axaml` solo tiene `FluentTheme` + DataGrid Fluent theme.
- La fuente Inter (`Avalonia.Fonts.Inter`) está referenciada en el csproj pero NO se aplica en ninguna vista.
- Colores hardcodeados inline y duplicados copy-paste en cada vista (ej. `#F5F5F5` ×13, `Foreground="Red"` para errores).
- Cero iconos. Sin componentes reutilizables. Sidebar sin estado activo/seleccionado.
- Vistas: MainWindow (raíz), LoginView, PrimerArranqueView, ShellMainView (sidebar 200px + contenido), InicioView (dashboard de accesos rápidos), Catálogo (Producto/Categoria/Proveedor/UnidadMedida × List+Form), Movimientos (Registro/Historial), Reportes (Valorizacion/StockCategoria/HistorialPorProducto/MasMovidos/AuditoriaLog con DataGrid), Dialogs/ConfirmacionDialog, Actualizaciones (Banner/Modal/Bloqueo).

## Decisión de arquitectura (aprobada por el usuario)
Enfoque elegido: **capa aditiva sobre Fluent** (descartados: theme custom completo por riesgo pre-demo; maquillaje selectivo por dejar el resto crudo en un recorrido completo).

Carpeta nueva `src/StockApp.Presentation/Themes/` con 3 ResourceDictionaries incluidos en `App.axaml` DESPUÉS de FluentTheme para overridear:
- `Tokens.axaml` — paleta completa como recursos nombrados (único lugar de verdad).
- `Typography.axaml` — Inter global + escala tipográfica.
- `Controls.axaml` — estilos globales de Button, TextBox, ComboBox, Card, DataGrid, sidebar.

## Paleta (tema claro profesional)
- Primary/brand: `#2563EB` (unifica el `#2196F3` disperso).
- Neutros: fondo `#F8FAFC`, superficie `#FFFFFF`, borde `#E2E8F0`, texto primario `#0F172A`, texto secundario `#64748B`.
- Semánticos: success `#16A34A`, warning `#D97706`, danger `#DC2626`, info `#0EA5E9`.

## Tipografía
Inter global + escala: título de vista 20/semibold, sección 16/medium, body 14, caption 12.

## Estilos globales de componentes
- Botones: `primary` (relleno brand), `secondary` (outline), `ghost` (sidebar), `danger` (destructivo), con estados hover/pressed/disabled.
- Inputs (TextBox/ComboBox): borde consistente, foco brand, estado error.
- Cards: Border con corner radius + sombra sutil + padding (dashboard de inicio y formularios).
- Sidebar: estado ACTIVO en el ítem de navegación seleccionado (fondo brand suave + texto brand).

## Iconos (aprobado)
Agregar dependencia `Projektanker.Icons.Avalonia` (con Material/FontAwesome) para iconografía consistente en sidebar y botones clave.

## Quick wins de interacción
- Enter submitea el formulario de login (pendiente UX previo conocido).
- Estado activo del sidebar.
- Empty states básicos en listas vacías (si el tiempo lo permite).

## Riesgo y validación
- Capa aditiva → riesgo estructural bajo, reversible quitando el include del ResourceDictionary.
- OBLIGATORIO antes de la demo: correr la app y recorrer visualmente las ~20 vistas (un theme global se ve distinto en cada pantalla).
- La suite de tests debe quedar verde al final (no se toca lógica).

## Orden de ejecución
1. Tokens + Inter + tipografía (base, impacto inmediato en todo).
2. Estilos de botones + inputs + cards.
3. Sidebar activo + iconos.
4. Quick wins (Enter en login).
5. Correr app, recorrer vistas, ajustar.
