using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

// ============================================================
// REQ-046 — Biblioteca de ANEXOS estructurada (Auditoría de Casos).
//
// Es la base PROPIA de Nexus donde el contrato base y su anexo por plan quedan
// estructurados (coberturas, topes, deducibles, carencias, exclusiones,
// cláusulas). Se llena por OCR→Claude desde la pantalla de administración; las
// tools del agente auditor LEEN de aquí, no de M-Files ni de las BD de Saludsa.
// ============================================================

/// <summary>Contrato base por tipo (Individual, Tradicional, Optimus Plus, Oncológico…).
/// Tiene las cláusulas generales; el detalle parametrizado vive en el <see cref="Anexo"/>.</summary>
public partial class AnexoContrato
{
    public int Id { get; set; }

    /// <summary>Individual | Tradicional | OptimusPlus | Oncologico | Corporativo.</summary>
    public string Tipo { get; set; } = null!;

    /// <summary>Registro ACESS, p. ej. 025-CI-012.</summary>
    public string? CodigoAcess { get; set; }

    public string Nombre { get; set; } = null!;
    public string? Version { get; set; }
    public string? Vigencia { get; set; }

    /// <summary>PDF fuente en Blob.</summary>
    public string? ArchivoUri { get; set; }

    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public virtual ICollection<Anexo> Anexos { get; set; } = new List<Anexo>();
    public virtual ICollection<AnexoClausula> Clausulas { get; set; } = new List<AnexoClausula>();
}

/// <summary>Anexo por plan/producto: donde viven las condiciones parametrizadas.
/// Se enlaza con el plan del cliente por <see cref="CodigoPlan"/>.</summary>
public partial class Anexo
{
    public int Id { get; set; }

    /// <summary>Contrato base al que pertenece (opcional).</summary>
    public int? ContratoId { get; set; }

    /// <summary>Enlace con el plan del cliente (Cl04Contratos.CodigoPlan).</summary>
    public string CodigoPlan { get; set; } = null!;

    public string? NombrePlan { get; set; }
    public string? CodigoProducto { get; set; }
    public string? Version { get; set; }
    public string? ArchivoUri { get; set; }

    /// <summary>Resumen legible de las condiciones (para mostrar y para el agente).</summary>
    public string? ResumenCondiciones { get; set; }

    /// <summary>BORRADOR | ACTIVO.</summary>
    public string Estado { get; set; } = "ACTIVO";

    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    public virtual AnexoContrato? Contrato { get; set; }
    public virtual ICollection<AnexoCobertura> Coberturas { get; set; } = new List<AnexoCobertura>();
    public virtual ICollection<AnexoCarencia> Carencias { get; set; } = new List<AnexoCarencia>();
    public virtual ICollection<AnexoExclusion> Exclusiones { get; set; } = new List<AnexoExclusion>();
}

/// <summary>Cobertura de un beneficio dentro del anexo: %, tope, deducible, copago.</summary>
public partial class AnexoCobertura
{
    public int Id { get; set; }
    public int AnexoId { get; set; }

    /// <summary>Hospitalario, Ambulatorio, Medicina, Maternidad, Emergencia…</summary>
    public string Beneficio { get; set; } = null!;
    public string? CodigoBeneficio { get; set; }
    public decimal? Porcentaje { get; set; }
    public decimal? Tope { get; set; }
    public string? MonedaTope { get; set; }
    public decimal? Deducible { get; set; }
    public string? Copago { get; set; }
    public string? Periodo { get; set; }
    public string? Ambito { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedDate { get; set; }

    public virtual Anexo? Anexo { get; set; }
}

/// <summary>Período de carencia de un beneficio del anexo.</summary>
public partial class AnexoCarencia
{
    public int Id { get; set; }
    public int AnexoId { get; set; }
    public string Beneficio { get; set; } = null!;
    public int? DiasCarencia { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedDate { get; set; }

    public virtual Anexo? Anexo { get; set; }
}

/// <summary>Exclusión (del anexo o del contrato base). Se cita textual, no por analogía.</summary>
public partial class AnexoExclusion
{
    public int Id { get; set; }
    public int? AnexoId { get; set; }
    public int? ContratoId { get; set; }
    public string Texto { get; set; } = null!;
    public string? ClausulaRef { get; set; }
    public DateTime CreatedDate { get; set; }

    public virtual Anexo? Anexo { get; set; }
    public virtual AnexoContrato? Contrato { get; set; }
}

/// <summary>Cláusula del contrato base, para citar cláusula/numeral/literal exacto.</summary>
public partial class AnexoClausula
{
    public int Id { get; set; }
    public int ContratoId { get; set; }
    public string? Ordinal { get; set; }
    public string? Numeral { get; set; }
    public string? Literal { get; set; }
    public string? Titulo { get; set; }
    public string Texto { get; set; } = null!;
    public DateTime CreatedDate { get; set; }

    public virtual AnexoContrato? Contrato { get; set; }
}

/// <summary>Traza de cada carga: subir → OCR → estructurar (Claude) → guardar.</summary>
public partial class AnexoIngesta
{
    public int Id { get; set; }
    public int? ContratoId { get; set; }
    public int? AnexoId { get; set; }
    public string Archivo { get; set; } = null!;
    public string? ArchivoUri { get; set; }

    /// <summary>RECIBIDO | OCR_OK | ESTRUCTURADO | GUARDADO | ERROR.</summary>
    public string Estado { get; set; } = "RECIBIDO";
    public string? Mensaje { get; set; }
    public string? TextoOcr { get; set; }
    public string? JsonExtraido { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
}
