using System.Threading;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Turnos de las consultas que reemplazan el contenido de un listado. Dos consultas pueden superponerse (la carga
/// inicial de un historial grande y una búsqueda filtrada; el debounce de la búsqueda y la recarga tras eliminar)
/// y el servidor puede responder en otro orden: sin esto, la más vieja pisa a la más nueva y la grilla muestra
/// algo que no es lo que el usuario pidió. Cada consulta pide su turno ANTES de llamar al servicio y, al volver,
/// aplica su resultado solo si ninguna otra empezó después. Los servicios no reciben CancellationToken: lo que se
/// descarta es el resultado, no el pedido HTTP.
/// </summary>
public sealed class ConsultaVigente
{
    private int _ultima;

    /// <summary>Empieza una consulta: desde ahora las anteriores dejan de ser la última.</summary>
    public Turno Empezar() => new(this, Interlocked.Increment(ref _ultima));

    /// <summary>El turno de una consulta.</summary>
    public readonly struct Turno
    {
        private readonly ConsultaVigente _consultas;
        private readonly int _numero;

        internal Turno(ConsultaVigente consultas, int numero)
        {
            _consultas = consultas;
            _numero = numero;
        }

        /// <summary>True si ninguna consulta empezó después de esta: su resultado es el que hay que mostrar.</summary>
        public bool EsLaUltima => Volatile.Read(ref _consultas._ultima) == _numero;
    }
}
