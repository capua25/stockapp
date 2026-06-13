using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        // Fallback: algunas Views viven en un subnamespace "Views" dentro del namespace del VM.
        // Ej: VM en "...Actualizaciones.ActualizacionBannerViewModel" →
        //     View en "...Actualizaciones.Views.ActualizacionBannerView".
        if (type is null)
        {
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
            {
                var fallbackName = name[..lastDot] + ".Views" + name[lastDot..];
                type = Type.GetType(fallbackName);
            }
        }

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
