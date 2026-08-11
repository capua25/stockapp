using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verifica que el ViewLocator resuelve AdjuntosPanelViewModel a AdjuntosPanelView real,
/// no al placeholder "Not Found: ..." (ver ViewLocator.Build). Este panel se creo sin su
/// View correspondiente en la Fase 3 de Adjuntos; este test evita la regresion.
///
/// Los 6 servicios del constructor de AdjuntosPanelViewModel se pasan como null! porque
/// Build() solo instancia la View via reflexion (Activator.CreateInstance del tipo resuelto
/// por convencion de nombre) — no invoca ningun metodo del ViewModel.
/// </summary>
public class ViewLocatorTests
{
    [AvaloniaFact]
    public void Build_ConAdjuntosPanelViewModel_ResuelveAdjuntosPanelView()
    {
        var vm = new AdjuntosPanelViewModel(null!, null!, null!, null!, null!);

        var control = new ViewLocator().Build(vm);

        Assert.IsType<AdjuntosPanelView>(control);
    }

    [AvaloniaFact]
    public void Build_ConAdjuntosPanelViewModel_NoDevuelveElPlaceholder()
    {
        var vm = new AdjuntosPanelViewModel(null!, null!, null!, null!, null!);

        var control = new ViewLocator().Build(vm);

        Assert.False(control is TextBlock tb && tb.Text!.StartsWith("Not Found:"));
    }

    /// <summary>
    /// Fix (MINOR, tercer review final E1): mismo perfil de riesgo que AdjuntosPanelViewModel
    /// de arriba -- AccesoLimitadoView es justamente la pantalla que el admin necesita cuando la
    /// licencia venció (modo acotado, FIX 1 re-review final E1), y no tenía ningún AvaloniaFact
    /// que la montara ni entrada acá que evite la regresión al placeholder "Not Found: ...".
    /// </summary>
    [AvaloniaFact]
    public void Build_ConAccesoLimitadoViewModel_ResuelveAccesoLimitadoView()
    {
        var vm = new AccesoLimitadoViewModel(null!);

        var control = new ViewLocator().Build(vm);

        Assert.IsType<AccesoLimitadoView>(control);
    }

    [AvaloniaFact]
    public void Build_ConAccesoLimitadoViewModel_NoDevuelveElPlaceholder()
    {
        var vm = new AccesoLimitadoViewModel(null!);

        var control = new ViewLocator().Build(vm);

        Assert.False(control is TextBlock tb && tb.Text!.StartsWith("Not Found:"));
    }
}
