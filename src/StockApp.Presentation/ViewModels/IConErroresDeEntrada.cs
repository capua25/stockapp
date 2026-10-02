namespace StockApp.Presentation.ViewModels;

/// <summary>
/// ViewModel que quiere enterarse de los errores de entrada que viven SOLO en la View: un
/// <c>TextBox</c> con texto que el converter rechaza (ej. "5.4") nunca llega a la propiedad del
/// ViewModel, que conserva el valor anterior válido. Lo publica
/// <see cref="StockApp.Presentation.Behaviors.ErroresDeEntradaBehavior"/>; lo implementa
/// <see cref="ViewModelBase"/>, así que todo ViewModel de pantalla ya lo tiene.
/// </summary>
public interface IConErroresDeEntrada
{
    bool HayErroresDeEntrada { get; set; }
}
