using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StockApp.Domain.Entities;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Red de seguridad automatizada de la verificacion inversa Vista-ViewModel pedida por el encargo
/// (post-mortem: "el contrato Vista-ViewModel es el unico que nadie verifica en direccion
/// 'esta todo expuesto?'"). Es un chequeo de TEXTO sobre el .axaml fuente real (no sobre el
/// arbol visual cargado): confirma, via reflection, que cada propiedad publica y cada
/// [RelayCommand] declarados DIRECTAMENTE en TareaListViewModel/TareaFormViewModel/
/// NuevaImportacionViewModel aparecen como palabra completa en alguna parte del .axaml de su
/// vista (deteccion "VM expone algo que la vista no muestra").
///
/// LIMITACION reconocida (no se sobre-vende): esto es presencia textual, no validacion del grafo
/// de binding real -- no distingue si el texto aparece dentro de un Binding valido o de un
/// comentario, y no alcanza para el sentido inverso completo (un Binding en la vista que apunta a
/// una propiedad INEXISTENTE) en vistas con multiples DataContext por DataTemplate como
/// NuevaImportacionView (3 tipos de fila comparten nombres de propiedad como "Estado" sin un
/// x:DataType unico -- ver el x:CompileBindings="False" en su axaml). Para TareaListView y
/// TareaFormView, que solo tienen DOS DataContext posibles cada una (el VM raiz + un unico tipo
/// de item en sus DataTemplates), SI se hace ademas el chequeo inverso completo mas abajo.
/// </summary>
public class ReflexionVistaViewModelTests
{
    private static string LeerAxaml(string rutaRelativaDesdeSrc, [CallerFilePath] string archivoDeEsteTest = "")
    {
        var dirDeTests = Path.GetDirectoryName(archivoDeEsteTest)!;
        var ruta = Path.GetFullPath(Path.Combine(dirDeTests, "..", "..", "src", rutaRelativaDesdeSrc));
        Assert.True(File.Exists(ruta), $"No se encontro el .axaml esperado en: {ruta}");
        return File.ReadAllText(ruta);
    }

    private static IReadOnlyList<string> PropiedadesPublicasDeclaradas(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .ToList();

    private static void AssertTodasExpuestas(string axaml, IEnumerable<string> nombres, ISet<string> exclusiones)
    {
        var faltantes = nombres
            .Where(n => !exclusiones.Contains(n))
            .Where(n => !Regex.IsMatch(axaml, $@"\b{Regex.Escape(n)}\b"))
            .ToList();

        Assert.True(faltantes.Count == 0,
            "Propiedad(es)/Command(s) del ViewModel SIN ningun control que las exponga en la vista: "
            + string.Join(", ", faltantes));
    }

    [Fact]
    public void TareaListViewModel_TodaPropiedadYComando_TieneUnControlEnLaVista()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Tareas/TareaListView.axaml");
        var miembros = PropiedadesPublicasDeclaradas(typeof(TareaListViewModel));

        // Sin exclusiones: verificado a mano (lectura completa del axaml) que las 11 propiedades/
        // comandos declarados en TareaListViewModel tienen control propio.
        AssertTodasExpuestas(axaml, miembros, new HashSet<string>());
    }

    [Fact]
    public void TareaFormViewModel_TodaPropiedadYComando_TieneUnControlEnLaVista()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Tareas/TareaFormView.axaml");
        var miembros = PropiedadesPublicasDeclaradas(typeof(TareaFormViewModel));

        var exclusiones = new HashSet<string>
        {
            // Helper interno consumido por MuestraCambioPrioridad (que SI esta bindeada, IsVisible
            // del panel de cambio de prioridad) -- no necesita un binding propio en el axaml.
            nameof(TareaFormViewModel.EsAdmin),
        };
        AssertTodasExpuestas(axaml, miembros, exclusiones);
    }

    [Fact]
    public void NuevaImportacionViewModel_TodaPropiedadYComando_TieneUnControlEnLaVista()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml");
        var miembros = PropiedadesPublicasDeclaradas(typeof(NuevaImportacionViewModel));

        var exclusiones = new HashSet<string>
        {
            // Colecciones RAW envueltas en su *View (DataGridCollectionView) para que el DataGrid
            // pueda ordenar (ver AllowSort/IDataGridCollectionView, DataGridSortClickTests.cs) --
            // el axaml bindea FilasGastoView/FilasIngresoView/FilasLineaPoaView, nunca la coleccion
            // cruda.
            nameof(NuevaImportacionViewModel.FilasGasto),
            "FilasIngreso",
            "FilasLineaPoa",
            // Gating via [RelayCommand(CanExecute = nameof(PuedeConfirmar))] sobre ConfirmarCommand:
            // Button.IsEnabled se deriva automaticamente del Command, no necesita un binding propio
            // -- el mensaje visible para el usuario es MensajeConfirmarBloqueado (esa SI esta bindeada).
            nameof(NuevaImportacionViewModel.PuedeConfirmar),
        };
        AssertTodasExpuestas(axaml, miembros, exclusiones);
    }

    /// <summary>
    /// Encargo puntual: auditar la vista mas critica que quedo sin cubrir en la ronda anterior
    /// (post-mortem literal: "en ingreso por factura se llego a 2023 tests verdes con una
    /// pantalla inutilizable"). A diferencia de NuevaImportacionView, IngresoPorFacturaView SI
    /// tiene x:DataType explicito en cada DataTemplate (FilaRenglonFacturaVm en la grilla de
    /// renglones, ItemConfirmacionPrecioVm en el overlay de confirmacion de precios) y ningun
    /// nombre de propiedad se repite ambiguamente entre esos dos tipos -- por eso el chequeo
    /// inverso completo de mas abajo SI es viable aca, a diferencia de NuevaImportacionView.
    /// </summary>
    [Fact]
    public void IngresoPorFacturaViewModel_TodaPropiedadYComando_TieneUnControlEnLaVista()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml");
        var miembros = PropiedadesPublicasDeclaradas(typeof(IngresoPorFacturaViewModel));

        var exclusiones = new HashSet<string>
        {
            // Seteada en GuardarInternoAsync (GastoIdCreado = resultado.GastoId) solo para uso
            // INTERNO: nunca se muestra en el axaml (el panel post-guardado dice "Factura
            // guardada..." sin mostrar el numero/id). No es de los 2 bugs historicos (elegir
            // producto / marcar credito) -- se documenta como observacion en el informe, no como
            // bloqueante, porque no promete nada distinto al usuario.
            nameof(IngresoPorFacturaViewModel.GastoIdCreado),
        };
        AssertTodasExpuestas(axaml, miembros, exclusiones);
    }

    // ── Chequeo inverso (vista -> viewmodel): solo para las vistas donde es viable sin ambiguedad
    // (ver limitacion documentada arriba). ──

    private static readonly Regex PatronBindingSimple =
        new(@"\{Binding\s+!?([A-Za-z_][A-Za-zA-Z0-9_]*)", RegexOptions.Compiled);

    private static IReadOnlyList<string> ExtraerPrimerSegmentoDeBindings(string axaml) =>
        PatronBindingSimple.Matches(axaml).Select(m => m.Groups[1].Value).Distinct().ToList();

    [Fact]
    public void TareaListView_NingunBindingApuntaAUnaPropiedadInexistente()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Tareas/TareaListView.axaml");
        var candidatos = ExtraerPrimerSegmentoDeBindings(axaml);

        var validos = PropiedadesPublicasDeclaradas(typeof(TareaListViewModel))
            .Concat(PropiedadesPublicasDeclaradas(typeof(TareaFila)))
            .ToHashSet();

        var invalidos = candidatos.Where(c => !validos.Contains(c)).ToList();
        Assert.True(invalidos.Count == 0,
            "Binding(s) en TareaListView.axaml sin propiedad correspondiente ni en TareaListViewModel "
            + "ni en TareaFila (posible typo silencioso de Avalonia): " + string.Join(", ", invalidos));
    }

    [Fact]
    public void TareaFormView_NingunBindingApuntaAUnaPropiedadInexistente()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Tareas/TareaFormView.axaml");
        var candidatos = ExtraerPrimerSegmentoDeBindings(axaml);

        var validos = PropiedadesPublicasDeclaradas(typeof(TareaFormViewModel))
            .Concat(PropiedadesPublicasDeclaradas(typeof(NotaTarea)))
            .ToHashSet();

        var invalidos = candidatos.Where(c => !validos.Contains(c)).ToList();
        Assert.True(invalidos.Count == 0,
            "Binding(s) en TareaFormView.axaml sin propiedad correspondiente ni en TareaFormViewModel "
            + "ni en NotaTarea (posible typo silencioso de Avalonia): " + string.Join(", ", invalidos));
    }

    /// <summary>
    /// IngresoPorFacturaView tiene MAS de dos DataContext posibles (el VM raiz + FilaRenglonFacturaVm
    /// en la grilla + ItemConfirmacionPrecioVm en el overlay de precios + 6 tipos de entidad de
    /// catalogo -- Proveedor/FuenteFinanciamiento/RubroGasto/LineaPoa/Categoria/UnidadMedida --
    /// usados solo como ItemsSource de un ComboBox con "Nombre" en su ItemTemplate). A diferencia
    /// de NuevaImportacionView, ninguno de estos tipos comparte nombres de propiedad de forma
    /// ambigua entre si (confirmado a mano: Producto/Cantidad/PrecioUnitario/Subtotal/
    /// ActualizarPrecioCosto/NombreMostrado/EsProductoNuevo son unicos de FilaRenglonFacturaVm;
    /// Confirmado/ProductoNombre/PrecioActual/PrecioNuevo son unicos de ItemConfirmacionPrecioVm;
    /// "Nombre" es la unica propiedad compartida, y la comparten los 6 tipos de catalogo entre si,
    /// nunca con los VMs), asi que la union simple SI detecta un typo real sin falsos negativos.
    /// </summary>
    [Fact]
    public void IngresoPorFacturaView_NingunBindingApuntaAUnaPropiedadInexistente()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml");
        var candidatos = ExtraerPrimerSegmentoDeBindings(axaml);

        var validos = PropiedadesPublicasDeclaradas(typeof(IngresoPorFacturaViewModel))
            .Concat(PropiedadesPublicasDeclaradas(typeof(FilaRenglonFacturaVm)))
            .Concat(PropiedadesPublicasDeclaradas(typeof(ItemConfirmacionPrecioVm)))
            .Concat(PropiedadesPublicasDeclaradas(typeof(Proveedor)))
            .Concat(PropiedadesPublicasDeclaradas(typeof(FuenteFinanciamiento)))
            .Concat(PropiedadesPublicasDeclaradas(typeof(RubroGasto)))
            .Concat(PropiedadesPublicasDeclaradas(typeof(LineaPoa)))
            .Concat(PropiedadesPublicasDeclaradas(typeof(Categoria)))
            .Concat(PropiedadesPublicasDeclaradas(typeof(UnidadMedida)))
            .ToHashSet();

        var invalidos = candidatos.Where(c => !validos.Contains(c)).ToList();
        Assert.True(invalidos.Count == 0,
            "Binding(s) en IngresoPorFacturaView.axaml sin propiedad correspondiente en ninguno de "
            + "los tipos de DataContext posibles (posible typo silencioso de Avalonia): "
            + string.Join(", ", invalidos));
    }
}
