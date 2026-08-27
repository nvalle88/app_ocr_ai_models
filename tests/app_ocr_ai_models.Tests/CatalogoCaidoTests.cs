using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using app_ocr_ai_models.Services.Ai;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// Qué pasa cuando SQLMIGRACION no contesta.
//
// HomologarAsync se llama una vez POR PROCEDIMIENTO dentro de un bucle
// (ClasificacionController.PersistirAsync). El catálogo Lr05 vive en
// SQLMIGRACION, al otro lado de la VPN, con Connect Timeout=30 en la cadena.
//
// Mientras el fallo no se recordaba, cada ítem de cada factura pagaba la espera
// entera: seis líneas eran tres minutos de silencio absoluto —sin error, sin
// log por ítem, sin nada— que el afiliado lee como "se quedó congelado".
// =============================================================================
public class CatalogoCaidoTests
{
    /// <summary>Un host que no existe, con la espera recortada para que la prueba corra.</summary>
    private static HomologadorProcedimientos ConCatalogoInalcanzable(IMemoryCache cache)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SaludProcedimientos"] =
                    "Server=no-existe-esta-maquina.invalid;Database=salud;User ID=x;Password=y;"
                    + "TrustServerCertificate=True;Encrypt=False;Connect Timeout=2"
            })
            .Build();

        return new HomologadorProcedimientos(
            config, cache, NullLogger<HomologadorProcedimientos>.Instance);
    }

    [Fact]
    public async Task Tras_fallar_no_se_vuelve_a_cargar_el_catalogo()
    {
        // El invariante que de verdad importa, sin depender de relojes: si la
        // segunda llamada recargara, dejaría en la caché una lista NUEVA. Que
        // siga siendo exactamente la misma instancia demuestra que nadie volvió
        // a salir a la red.
        //
        // Se mide así y no por tiempo porque un timeout real de 30 s no cabe en
        // una prueba, y un host falso falla por DNS al instante: cronometrarlo
        // compara cero con cero y la prueba no podría fallar nunca.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var h = ConCatalogoInalcanzable(cache);

        await h.HomologarAsync("COLONOSCOPIA (VCC)");
        cache.TryGetValue("lr05:catalogo", out object? primera);

        // Los cinco ítems siguientes de la misma factura.
        for (var i = 0; i < 5; i++) await h.HomologarAsync("BIOPSIA");
        cache.TryGetValue("lr05:catalogo", out object? ultima);

        Assert.NotNull(primera);
        Assert.Same(primera, ultima);
    }

    [Fact]
    public async Task El_fallo_queda_recordado_en_la_cache()
    {
        // El invariante exacto, sin depender de relojes: tras fallar, la clave
        // del catálogo TIENE que estar puesta. Antes el `return` del catch salía
        // sin pasar por el _cache.Set.
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var h = ConCatalogoInalcanzable(cache);

        await h.HomologarAsync("COLONOSCOPIA (VCC)");

        Assert.True(cache.TryGetValue("lr05:catalogo", out _),
            "El catálogo caído no se recordó: se reintentará la conexión por cada ítem.");
    }
}
