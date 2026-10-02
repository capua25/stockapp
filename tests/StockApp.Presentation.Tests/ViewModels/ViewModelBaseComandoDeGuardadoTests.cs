using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// Cubre <c>ViewModelBase.ComandoDeGuardado</c> (bug de integridad 2026-10-01): un comando de
/// guardar/confirmar se bloquea solo mientras <see cref="ViewModelBase.HayErroresDeEntrada"/>, y
/// avisa el cambio sin que el ViewModel concreto lo cablee. Antes cada formulario sumaba a mano
/// <c>&amp;&amp; !HayErroresDeEntrada</c> y notificaba el comando: uno que se olvidaba guardaba en
/// silencio el valor viejo.
/// </summary>
public class ViewModelBaseComandoDeGuardadoTests
{
    private sealed class VmDePrueba : ViewModelBase
    {
        public bool Puede { get; set; } = true;
        public int Ejecuciones { get; private set; }

        public IAsyncRelayCommand GuardarCommand => field ??= ComandoDeGuardado(GuardarAsync, () => Puede);
        public IRelayCommand AgregarCommand => field ??= ComandoDeGuardado(() => Ejecuciones++);
        public IRelayCommand CancelarCommand => field ??= new RelayCommand(() => { });

        private Task GuardarAsync()
        {
            Ejecuciones++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void SinErrores_RespetaElCanExecuteDelViewModel()
    {
        var vm = new VmDePrueba();
        Assert.True(vm.GuardarCommand.CanExecute(null));

        vm.Puede = false;

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void ConErroresDeEntrada_BloqueaElComando_AsyncYSincronico()
    {
        var vm = new VmDePrueba();

        vm.HayErroresDeEntrada = true;

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.False(vm.AgregarCommand.CanExecute(null));

        vm.HayErroresDeEntrada = false;

        Assert.True(vm.GuardarCommand.CanExecute(null));
        Assert.True(vm.AgregarCommand.CanExecute(null));
    }

    [Fact]
    public void AlCambiarLosErrores_NotificaElCambioDeCanExecute_SinCableadoEnElViewModel()
    {
        var vm = new VmDePrueba();
        var avisosGuardar = 0;
        var avisosAgregar = 0;
        vm.GuardarCommand.CanExecuteChanged += (_, _) => avisosGuardar++;
        vm.AgregarCommand.CanExecuteChanged += (_, _) => avisosAgregar++;

        vm.HayErroresDeEntrada = true;
        vm.HayErroresDeEntrada = false;

        Assert.Equal(2, avisosGuardar);
        Assert.Equal(2, avisosAgregar);
    }

    [Fact]
    public void EsComandoDeGuardado_SoloParaLosCreadosConElHelper()
    {
        var vm = new VmDePrueba();

        Assert.True(vm.EsComandoDeGuardado(vm.GuardarCommand));
        Assert.True(vm.EsComandoDeGuardado(vm.AgregarCommand));
        Assert.False(vm.EsComandoDeGuardado(vm.CancelarCommand));
        Assert.False(vm.EsComandoDeGuardado(null));
        Assert.False(new VmDePrueba().EsComandoDeGuardado(vm.GuardarCommand));
    }

    [Fact]
    public void ElComandoSigueEjecutando_ElCuerpoDelViewModel()
    {
        var vm = new VmDePrueba();

        vm.GuardarCommand.Execute(null);
        vm.AgregarCommand.Execute(null);

        Assert.Equal(2, vm.Ejecuciones);
    }
}
