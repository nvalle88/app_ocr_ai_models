using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace app_ocr_ai_models.Services;

// =============================================================================
// REQ-024b — Decirlo cuando adjunta la factura, no cuando ya subió todo
//
// El bloqueo por factura repetida vivía al final, cuando el afiliado ya había
// elegido beneficiario, subido cada documento y esperado el cálculo. Enterarse
// entonces de que esa factura ya estaba registrada es hacerle recorrer el
// camino entero para darle un no que se sabía desde el primer papel.
//
// La clave de acceso se conoce en cuanto el documento se identifica: queda en
// DocumentoClasificacion.ClaveAcceso. Desde ahí ya se puede preguntar.
//
// -- Por qué NO se reusa aquí la herramienta del agente ----------------------
// Porque dependería de que el agente la hubiera llamado, y en ese momento el
// agente todavía no ha corrido. La regla no puede esperar a que alguien decida
// consultarla: se pregunta directamente.
//
// -- Por qué se consulta en cada render y no se guarda ------------------------
// Medido: 150-280 ms. A cambio, la respuesta es siempre la de ahora. Un
// veredicto guardado envejece —entre que se guarda y que el afiliado vuelve,
// la misma factura puede haber entrado por otra vía— y un duplicado que se
// perdió por leer un dato viejo se paga dos veces.
//
// La consulta es la misma que la de la herramienta, y por el mismo motivo:
// busca en TODA la base, sin limitar por contrato ni exigir la misma persona.
// La factura es única.
// =============================================================================

public interface IBuscadorFacturaRepetida
{
    /// <summary>
    /// ¿Alguna de estas facturas ya está registrada? Devuelve el JSON con la
    /// misma forma que la herramienta, para que lo juzgue la misma regla.
    /// </summary>
    Task<string?> BuscarAsync(IEnumerable<(string? NumeroFactura, string? ClaveAcceso)> facturas,
                              CancellationToken ct = default);
}

public sealed class BuscadorFacturaRepetida : IBuscadorFacturaRepetida
{
    private const string ConnName = "SaludReclamos";

    // La misma consulta de factura_ya_pagada_bd. Si se toca una, se toca la otra.
    //
    // Los parámetros van como VARCHAR a propósito: NroFacturaPrestador es
    // varchar(30) y NumeroAutorizacion varchar(60). Pasarlos como NVARCHAR obliga
    // a SQL Server a convertir la COLUMNA, el índice IdxNroFactNumConvenio deja
    // de servir y la consulta pasa de 0 ms a 47 segundos. Medido.
    private const string Consulta = @"
SELECT TOP 20
       d.NumeroReclamo, d.NumeroAlcance, d.ContratoNumero, d.PersonaNumero,
       MAX(r.EstadoReclamo)             AS EstadoReclamo,
       MAX(r.FechaPresentacionReclamo)  AS FechaPresentacion,
       MAX(r.FechaPagoReclamo)          AS FechaPago,
       MAX(r.MontoPagado)               AS MontoPagadoDelReclamo,
       MAX(CASE WHEN r.EstadoReclamo = 10 AND r.FechaAnulacion IS NULL THEN 1 ELSE 0 END) AS YaPagado,
       MAX(CASE WHEN r.FechaAnulacion IS NOT NULL THEN 1 ELSE 0 END)                      AS Anulada
  FROM Salud.dbo.Lr04DetalleReclamo d WITH (NOLOCK)
  LEFT JOIN Salud.dbo.Lr02Reclamos r WITH (NOLOCK)
         ON r.NumeroReclamo = d.NumeroReclamo AND r.NumeroAlcance = d.NumeroAlcance
 WHERE d.NroFacturaPrestador = @numeroFactura
   AND (d.ClaveAcceso = @claveAcceso OR d.NumeroAutorizacion = @claveAcceso)
 GROUP BY d.NumeroReclamo, d.NumeroAlcance, d.ContratoNumero, d.PersonaNumero";

    private readonly IConfiguration _config;
    private readonly ILogger<BuscadorFacturaRepetida> _log;

    public BuscadorFacturaRepetida(IConfiguration config, ILogger<BuscadorFacturaRepetida> log)
    {
        _config = config;
        _log = log;
    }

    public async Task<string?> BuscarAsync(
        IEnumerable<(string? NumeroFactura, string? ClaveAcceso)> facturas,
        CancellationToken ct = default)
    {
        // Sin las dos cosas no se puede buscar: el número solo no identifica nada
        // -medido: 001-100-000000916 sale en 37 líneas con 17 claves distintas- y
        // la clave sola no puede aprovechar el índice.
        var buscables = facturas
            .Where(f => !string.IsNullOrWhiteSpace(f.NumeroFactura)
                     && !string.IsNullOrWhiteSpace(f.ClaveAcceso))
            .Select(f => (Numero: f.NumeroFactura!.Trim(), Clave: f.ClaveAcceso!.Trim()))
            .Distinct()
            .ToList();

        if (buscables.Count == 0) return null;

        var cs = _config.GetConnectionString(ConnName);
        if (string.IsNullOrWhiteSpace(cs))
        {
            _log.LogWarning("[FacturaRepetida] Falta ConnectionStrings:{Conn}. No se comprueba.", ConnName);
            return null;
        }

        var filas = new List<Dictionary<string, object?>>();

        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync(ct);

            foreach (var (numero, clave) in buscables)
            {
                await using var cmd = new SqlCommand(Consulta, cn) { CommandTimeout = 15 };
                cmd.Parameters.Add(new SqlParameter("@numeroFactura", SqlDbType.VarChar, 30) { Value = numero });
                cmd.Parameters.Add(new SqlParameter("@claveAcceso",   SqlDbType.VarChar, 120) { Value = clave });

                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    var fila = new Dictionary<string, object?>();
                    for (var i = 0; i < rd.FieldCount; i++)
                        fila[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
                    filas.Add(fila);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;      // el afiliado cerró la pestaña: no es un fallo
        }
        catch (Exception ex)
        {
            // Que no se pueda comprobar NO puede bloquear a nadie: sin respuesta,
            // la regla deja pasar. Pero queda en el log, porque si esto falla a
            // menudo el control deja de existir sin que se note.
            _log.LogWarning(ex, "[FacturaRepetida] No se pudo comprobar si la factura ya estaba.");
            return null;
        }

        return JsonSerializer.Serialize(new { rowCount = filas.Count, rows = filas });
    }
}
