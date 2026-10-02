using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Publica al ViewModel dueño de la vista si algún campo tiene un error de entrada que vive SOLO
/// en la View (bug de integridad 2026-10-01). Un <see cref="TextBox"/> con texto que el converter
/// rechaza (ej. "5.4", "Usá coma para los decimales") muestra el error, pero el binding nunca
/// escribe en el ViewModel, que conserva el valor ANTERIOR válido: sin este behavior, Guardar
/// seguía habilitado y guardaba en silencio el valor viejo.
///
/// Cómo funciona: cada control con <c>Reportar=True</c> (TODO <see cref="TextBox"/>, vía el style
/// global de Themes/Controls.axaml, igual que <c>ErrorValidacionConverter</c>) avisa cuando cambia
/// su <see cref="DataValidationErrors.HasErrorsProperty"/> o entra/sale del árbol visual. El dueño
/// es el primer ancestro (incluido el propio control) cuyo DataContext implementa
/// <see cref="IConErroresDeEntrada"/>; las filas de grilla (ObservableObject/ObservableValidator)
/// no lo implementan, así que una celda reporta al ViewModel de la pantalla. Se lleva el conjunto
/// de controles en error POR dueño y se publica <c>HayErroresDeEntrada = conjunto no vacío</c>.
/// El ViewModel lo consulta en el CanExecute de su comando de guardar.
///
/// Una vista nueva lo hereda sin tocar XAML; el ViewModel crea su comando de guardar con
/// <see cref="ViewModelBase"/>.<c>ComandoDeGuardado(...)</c>, que compone el bloqueo solo, y el
/// botón recibe el tooltip del bloqueo vía <see cref="ExplicarBloqueoProperty"/> (global en el tema).
/// </summary>
public static class ErroresDeEntradaBehavior
{
    public static readonly AttachedProperty<bool> ReportarProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("Reportar");

    /// <summary>
    /// Sobre un <see cref="DataGrid"/> (global vía Themes/DataGrid.axaml): si la celda en edición
    /// tiene un campo en rojo, el commit se CANCELA y la celda sigue en edición. Sin esto, terminar
    /// la edición (Enter, Tab, click en otra fila) descartaba el editor: la fila quedaba con el
    /// valor ANTERIOR, el error desaparecía de pantalla y el comando de confirmar se rehabilitaba
    /// (verificado con NuevaImportacionView: commit=True, Monto viejo, PuedeConfirmar=True).
    /// Escape (cancelar la edición) sigue funcionando: vuelve al valor anterior a la vista.
    /// </summary>
    public static readonly AttachedProperty<bool> RetenerEdicionInvalidaProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, DataGrid, bool>("RetenerEdicionInvalida");

    /// <summary>
    /// Sobre un <see cref="Button"/> (global vía Themes/Controls.axaml): si su Command es un comando
    /// de guardado del ViewModel dueño (<see cref="ViewModelBase.EsComandoDeGuardado"/>), el botón
    /// muestra <see cref="ViewModelBase.MotivoBloqueoPorErrores"/> como tooltip, también
    /// deshabilitado. Prioridad de estilo: un <c>ToolTip.Tip</c> puesto en el XAML del botón gana.
    /// </summary>
    public static readonly AttachedProperty<bool> ExplicarBloqueoProperty =
        AvaloniaProperty.RegisterAttached<Button, Button, bool>("ExplicarBloqueo");

    private static readonly ConditionalWeakTable<Button, IDisposable> TooltipsEnganchados = new();

    /// <summary>Dueño al que el control reportó su error (para poder retirarlo aunque cambie el árbol).</summary>
    private static readonly AttachedProperty<IConErroresDeEntrada?> DuenioProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, IConErroresDeEntrada?>("DuenioErroresDeEntrada");

    private static readonly ConditionalWeakTable<IConErroresDeEntrada, HashSet<Control>> CamposConError = new();

    static ErroresDeEntradaBehavior()
    {
        ReportarProperty.Changed.AddClassHandler<Control>(OnReportarChanged);
        RetenerEdicionInvalidaProperty.Changed.AddClassHandler<DataGrid>((grilla, e) =>
        {
            grilla.CellEditEnding -= AlTerminarEdicionDeCelda;
            if (e.GetNewValue<bool>())
                grilla.CellEditEnding += AlTerminarEdicionDeCelda;
        });
        ExplicarBloqueoProperty.Changed.AddClassHandler<Button>((boton, e) =>
        {
            boton.AttachedToVisualTree -= AlEntrarBoton;
            boton.DetachedFromVisualTree -= AlSalirBoton;
            boton.PropertyChanged -= AlCambiarBoton;
            Desenganchar(boton);
            if (!e.GetNewValue<bool>())
                return;
            boton.AttachedToVisualTree += AlEntrarBoton;
            boton.DetachedFromVisualTree += AlSalirBoton;
            boton.PropertyChanged += AlCambiarBoton;
            EngancharTooltip(boton);
        });
        DataValidationErrors.HasErrorsProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (GetReportar(control))
                Reevaluar(control);
        });
    }

    public static bool GetReportar(Control control) => control.GetValue(ReportarProperty);

    public static void SetReportar(Control control, bool value) => control.SetValue(ReportarProperty, value);

    public static bool GetExplicarBloqueo(Button boton) => boton.GetValue(ExplicarBloqueoProperty);

    public static void SetExplicarBloqueo(Button boton, bool value) => boton.SetValue(ExplicarBloqueoProperty, value);

    public static bool GetRetenerEdicionInvalida(DataGrid grilla) => grilla.GetValue(RetenerEdicionInvalidaProperty);

    public static void SetRetenerEdicionInvalida(DataGrid grilla, bool value) => grilla.SetValue(RetenerEdicionInvalidaProperty, value);

    private static void AlTerminarEdicionDeCelda(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit
            && e.EditingElement is Control editor
            && editor.GetSelfAndVisualDescendants().OfType<Control>().Any(DataValidationErrors.GetHasErrors))
            e.Cancel = true;
    }

    private static void AlEntrarBoton(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Button boton)
            EngancharTooltip(boton);
    }

    private static void AlSalirBoton(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Button boton)
            Desenganchar(boton);
    }

    private static void AlCambiarBoton(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is Button boton
            && (e.Property == Button.CommandProperty || e.Property == StyledElement.DataContextProperty))
            EngancharTooltip(boton);
    }

    private static void EngancharTooltip(Button boton)
    {
        Desenganchar(boton);
        if (TopLevel.GetTopLevel(boton) is null
            || BuscarDuenio(boton) is not ViewModelBase duenio
            || !duenio.EsComandoDeGuardado(boton.Command))
            return;

        var motivo = new MotivoBloqueoObservable(duenio);
        var tip = boton.Bind(ToolTip.TipProperty, motivo, BindingPriority.Style);
        var mostrar = boton.Bind(ToolTip.ShowOnDisabledProperty, new Constante<bool>(true), BindingPriority.Style);
        TooltipsEnganchados.AddOrUpdate(boton, new Desenganche(tip, mostrar, motivo));
    }

    private static void Desenganchar(Button boton)
    {
        if (!TooltipsEnganchados.TryGetValue(boton, out var enganche))
            return;
        TooltipsEnganchados.Remove(boton);
        enganche.Dispose();
    }

    private static void OnReportarChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.AttachedToVisualTree -= AlEntrarAlArbol;
        control.DetachedFromVisualTree -= AlSalirDelArbol;

        if (e.GetNewValue<bool>())
        {
            control.AttachedToVisualTree += AlEntrarAlArbol;
            control.DetachedFromVisualTree += AlSalirDelArbol;
            Reevaluar(control);
        }
        else
        {
            Retirar(control);
        }
    }

    private static void AlEntrarAlArbol(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Reevaluar(control);
    }

    /// <summary>Un campo que sale del árbol (celda que termina de editarse, fila quitada, vista
    /// que se cierra) deja de bloquear: su error ya no está en pantalla.</summary>
    private static void AlSalirDelArbol(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Retirar(control);
    }

    private static void Reevaluar(Control control)
    {
        var duenio = DataValidationErrors.GetHasErrors(control) && TopLevel.GetTopLevel(control) is not null
            ? BuscarDuenio(control)
            : null;

        var anterior = control.GetValue(DuenioProperty);
        if (ReferenceEquals(anterior, duenio))
            return;

        Retirar(control);
        if (duenio is null)
            return;

        control.SetValue(DuenioProperty, duenio);
        var campos = CamposConError.GetOrCreateValue(duenio);
        campos.Add(control);
        duenio.HayErroresDeEntrada = true;
    }

    private static void Retirar(Control control)
    {
        if (control.GetValue(DuenioProperty) is not { } duenio)
            return;

        control.ClearValue(DuenioProperty);
        if (CamposConError.TryGetValue(duenio, out var campos))
        {
            campos.Remove(control);
            duenio.HayErroresDeEntrada = campos.Count > 0;
        }
    }

    private static IConErroresDeEntrada? BuscarDuenio(Control control)
        => control.GetSelfAndVisualAncestors()
            .OfType<StyledElement>()
            .Select(e => e.DataContext)
            .OfType<IConErroresDeEntrada>()
            .FirstOrDefault();

    /// <summary><see cref="ViewModelBase.MotivoBloqueoPorErrores"/> como observable (para enlazarlo
    /// con prioridad de estilo, que deja ganar a un tooltip local del XAML).</summary>
    private sealed class MotivoBloqueoObservable : IObservable<object?>, IDisposable
    {
        private readonly ViewModelBase _duenio;
        private readonly List<IObserver<object?>> _observadores = new();

        public MotivoBloqueoObservable(ViewModelBase duenio)
        {
            _duenio = duenio;
            _duenio.PropertyChanged += AlCambiar;
        }

        public IDisposable Subscribe(IObserver<object?> observador)
        {
            _observadores.Add(observador);
            observador.OnNext(_duenio.MotivoBloqueoPorErrores);
            return new Desenganche(new Accion(() => _observadores.Remove(observador)));
        }

        private void AlCambiar(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewModelBase.MotivoBloqueoPorErrores))
                return;
            foreach (var observador in _observadores.ToList())
                observador.OnNext(_duenio.MotivoBloqueoPorErrores);
        }

        public void Dispose()
        {
            _duenio.PropertyChanged -= AlCambiar;
            _observadores.Clear();
        }
    }

    private sealed class Constante<T>(T valor) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observador)
        {
            observador.OnNext(valor);
            return new Desenganche();
        }
    }

    private sealed class Accion(Action accion) : IDisposable
    {
        public void Dispose() => accion();
    }

    private sealed class Desenganche(params IDisposable?[] partes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var parte in partes)
                parte?.Dispose();
        }
    }
}
