using System;
using System.Collections.Generic;
using System.Linq;

namespace app_tramites.Services.Ai.Tools;

// =============================================================================
// REQ-020d — Las identidades legítimas de un caso, por CLASE de identificador.
//
// El guardián anti-IDOR comparaba TODO campo de identidad contra una sola
// cadena: la cédula del caso. Eso es un error de categoría, porque los campos
// que revisa no son la misma clase de cosa:
//
//     cedula / identificacion / numeroDocumento   → una cédula
//     numeroContrato (+ region + producto)        → un contrato
//     numeroPersona / numeroPersonaBeneficiario   → un número de persona
//
// Comparar un contrato (4160731) contra una cédula (0912514197) no coincide
// jamás. Medido en la auditoría: 3 llamadas de consultar_deducible_contrato
// denegadas exactamente por eso. No se notaba más porque el guardián se abre
// entero cuando la identidad del caso no está resuelta, que era lo habitual; el
// portal del afiliado la fija desde el primer paso, con lo que el falso rechazo
// habría pasado de raro a sistemático.
//
// EL NÚMERO DE CONTRATO NO ES ÚNICO. La llave es el triple
// región + producto + número: medido sobre Cl05Beneficiarios, 322.560 de
// 1.864.098 números (17%) se repiten entre combinaciones de región y producto.
// Autorizar por el número suelto sería un agujero, no una validación: dejaría
// pasar el contrato 4160731 de Sierra/COR cuando el caso es de Costa/IND.
//
// El número de persona, en cambio, SÍ es único globalmente (Cl03Personas:
// 2.803.785 filas, 2.803.785 valores distintos), así que por sí solo identifica
// sin ambigüedad y es el ancla más fuerte que hay.
//
// Y un caso tiene VARIAS identidades legítimas: un contrato cubre al titular y
// a sus dependientes, y consultar las preexistencias de un hijo por su cédula
// es correcto, no un IDOR.
// =============================================================================

/// <summary>
/// Los identificadores que un caso tiene derecho a consultar, agrupados por la
/// clase de identificador a la que pertenecen.
/// </summary>
public sealed class IdentidadCaso
{
    /// <summary>Cédulas del titular y de los beneficiarios del contrato.</summary>
    public HashSet<string> Cedulas { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Contratos del caso, como el triple <c>REGION|PRODUCTO|NUMERO</c>. El
    /// número suelto no vale: se repite entre regiones y productos.
    /// </summary>
    public HashSet<string> Contratos { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Números de persona del titular y de los beneficiarios.</summary>
    public HashSet<string> Personas { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cierto cuando no se conoce ningún identificador: nada que validar.</summary>
    public bool Vacia => Cedulas.Count == 0 && Contratos.Count == 0 && Personas.Count == 0;

    public IdentidadCaso ConCedula(string? valor)  => Anadir(Cedulas, valor);
    public IdentidadCaso ConPersona(string? valor) => Anadir(Personas, valor);

    /// <summary>
    /// Añade un contrato por su llave completa. Si falta la región o el
    /// producto NO se guarda: un contrato a medio identificar no autoriza nada,
    /// y guardarlo daría una falsa sensación de validación.
    /// </summary>
    public IdentidadCaso ConContrato(string? region, string? producto, string? numero)
    {
        var llave = Llave(region, producto, numero);
        if (llave != null) Contratos.Add(llave);
        return this;
    }

    /// <summary>
    /// La llave del contrato, o null si falta alguna de las tres partes.
    /// El producto y la región se comparan en mayúsculas porque la API los
    /// devuelve como "Costa"/"IND" y las bases como "COSTA"/"IND".
    /// </summary>
    public static string? Llave(string? region, string? producto, string? numero)
    {
        var r = (region   ?? string.Empty).Trim().ToUpperInvariant();
        var p = (producto ?? string.Empty).Trim().ToUpperInvariant();
        var n = Normalizar(numero);
        if (r.Length == 0 || p.Length == 0 || n.Length == 0) return null;
        return $"{r}|{p}|{n}";
    }

    private IdentidadCaso Anadir(HashSet<string> destino, string? valor)
    {
        var n = Normalizar(valor);
        if (!string.IsNullOrEmpty(n)) destino.Add(n);
        return this;
    }

    /// <summary>
    /// Normaliza un identificador para compararlo: quita guiones, espacios y
    /// los ceros de la izquierda. Hace falta porque la misma cédula viaja con
    /// cero inicial en las APIs y sin él en las bases de Saludsa.
    /// </summary>
    public static string Normalizar(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? string.Empty
            : id.Replace("-", "").Replace(" ", "").Trim().TrimStart('0');

    /// <summary>Una identidad con una sola cédula, como la que había antes.</summary>
    public static IdentidadCaso DeCedula(string? cedula) =>
        new IdentidadCaso().ConCedula(cedula);

    /// <summary>Para el log: qué se consideró legítimo, sin exponer los valores.</summary>
    public override string ToString() =>
        $"cedulas={Cedulas.Count} contratos={Contratos.Count} personas={Personas.Count}";
}
