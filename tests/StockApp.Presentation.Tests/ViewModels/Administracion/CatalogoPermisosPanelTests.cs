using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class CatalogoPermisosPanelTests
{
    // Guardián central del refactor (Task 3/4/6): reemplaza la garantía que antes daba
    // GuardarAsync_TildarTodosLosCheckboxes_EnviaExactamenteLosPermisosConfigurables por
    // reflection en PanelPermisosViewModelTests, movida un nivel antes -- al catálogo mismo.
    // Escenario histórico exacto (bug real 2026-08-14): se agregó Permisos.GestionarDocumentos
    // a AuthorizationService.PermisosConfigurables y nadie agregó su entrada acá -- el panel se
    // lo borraba en silencio a cualquier Operador que se editara. Este test tiene que reventar
    // nombrando EXACTAMENTE el permiso faltante, no solo "algo falló".
    [Fact]
    public void Entradas_CoincidenExactamenteConPermisosConfigurables_SinFaltantesNiSobrantes()
    {
        var clavesCatalogo = CatalogoPermisosPanel.Entradas.Select(e => e.Permiso).ToList();
        var configurables = AuthorizationService.PermisosConfigurables;

        var faltantesEnCatalogo = configurables.Except(clavesCatalogo).ToList();
        Assert.True(faltantesEnCatalogo.Count == 0,
            "AuthorizationService.PermisosConfigurables tiene permiso(s) sin entrada en el catálogo: "
            + string.Join(", ", faltantesEnCatalogo));

        var sobrantesEnCatalogo = clavesCatalogo.Except(configurables).ToList();
        Assert.True(sobrantesEnCatalogo.Count == 0,
            "El catálogo tiene entrada(s) para permiso(s) que no son configurables (¿colado un "
            + "permiso estructural Admin-only?): " + string.Join(", ", sobrantesEnCatalogo));
    }

    [Fact]
    public void Entradas_NoTieneClavesDuplicadas()
    {
        var duplicadas = CatalogoPermisosPanel.Entradas
            .GroupBy(e => e.Permiso)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicadas.Count == 0,
            "El catálogo tiene permiso(s) repetidos: " + string.Join(", ", duplicadas));
    }
}
