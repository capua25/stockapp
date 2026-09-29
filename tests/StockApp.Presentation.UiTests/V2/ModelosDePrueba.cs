namespace StockApp.Presentation.UiTests.V2;

/// <summary>Modelos mínimos para bindings reales (ReflectionBinding) en tests de componentes v2.</summary>
public sealed class ModeloCampoDecimal
{
    public decimal Cantidad { get; set; }
}

public sealed class ModeloBusqueda
{
    public string? Busqueda { get; set; }
}
