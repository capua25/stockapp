using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation;
using StockApp.Presentation.ViewModels.Finanzas;
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
        var vm = new AdjuntosPanelViewModel(null!, null!, null!, null!, null!, null!);

        var control = new ViewLocator().Build(vm);

        Assert.IsType<AdjuntosPanelView>(control);
    }

    [AvaloniaFact]
    public void Build_ConAdjuntosPanelViewModel_NoDevuelveElPlaceholder()
    {
        var vm = new AdjuntosPanelViewModel(null!, null!, null!, null!, null!, null!);

        var control = new ViewLocator().Build(vm);

        Assert.False(control is TextBlock tb && tb.Text!.StartsWith("Not Found:"));
    }
}
