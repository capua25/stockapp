namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza cuando una entidad solicitada por Id (u otra clave) no existe en el sistema.
/// Reemplaza KeyNotFoundException del BCL en los servicios de Application (Fase 3a, D4):
/// permite que DomainExceptionHandler distinga errores de dominio esperables (404) de
/// errores genéricos/no anticipados (500, fail-closed).
/// </summary>
public class EntidadNoEncontradaException : Exception
{
    public EntidadNoEncontradaException(string mensaje) : base(mensaje)
    {
    }
}
