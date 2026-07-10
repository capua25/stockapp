namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza cuando una operación viola una regla de negocio (duplicado, entidad ya
/// inactiva, último Admin, auto-baja, etc). Reemplaza InvalidOperationException del BCL
/// en los servicios de Application (Fase 3a, D4). StockInsuficienteException es un caso
/// particular (falta de stock al registrar una salida) y hereda de esta clase.
/// </summary>
public class ReglaDeNegocioException : Exception
{
    public ReglaDeNegocioException(string mensaje) : base(mensaje)
    {
    }
}
