using System.Text.Json.Serialization;
using app_tramites.Models.ModelAi;

namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-046 — Dictamen del agente AGENTE_AUDITOR_CASOS (6 secciones).
// Mapea el JSON de salida definido en su SystemPrompt.
// ============================================================

public sealed class AuditoriaCasoDto
{
    [JsonPropertyName("datosCaso")]          public DatosCasoDto? DatosCaso { get; set; }
    [JsonPropertyName("analisisClinico")]    public AnalisisClinicoDto? AnalisisClinico { get; set; }
    [JsonPropertyName("analisisContractual")] public AnalisisContractualDto? AnalisisContractual { get; set; }
    [JsonPropertyName("alertasFraude")]      public List<AlertaFraudeDto> AlertasFraude { get; set; } = new();
    [JsonPropertyName("recomendacion")]      public RecomendacionDto? Recomendacion { get; set; }
    [JsonPropertyName("bloqueRedactor")]     public BloqueRedactorDto? BloqueRedactor { get; set; }
}

public sealed class DatosCasoDto
{
    public string? ContratoPlan { get; set; }
    public string? FechaInicioCobertura { get; set; }
    public string? FechaAtencion { get; set; }
    public string? Diagnostico { get; set; }
    public string? Procedimiento { get; set; }
    public string? MontoSolicitado { get; set; }
    public List<string> DocumentosRevisados { get; set; } = new();
    public List<string> InformacionFaltante { get; set; } = new();
}

public sealed class AnalisisClinicoDto
{
    public string? Pertinencia { get; set; }
    public string? Justificacion { get; set; }
    public FuenteClinicaDto? Fuente { get; set; }
}
public sealed class FuenteClinicaDto
{
    public string? Guia { get; set; }
    public string? Organizacion { get; set; }
    public string? Anio { get; set; }
    public string? Resumen { get; set; }
}

public sealed class AnalisisContractualDto
{
    public CoberturaDto? Cobertura { get; set; }
    public PreexistenciasDto? Preexistencias { get; set; }
    public CarenciasDto? Carencias { get; set; }
    public ExclusionesDto? Exclusiones { get; set; }
}
public sealed class CoberturaDto
{
    public bool? Incluido { get; set; }
    public string? Porcentaje { get; set; }
    public string? Tope { get; set; }
    public string? Deducible { get; set; }
    public string? Copago { get; set; }
    public string? Notas { get; set; }
}
public sealed class PreexistenciasDto
{
    public string? Hallazgo { get; set; }
    public string? TipoNexo { get; set; }
    public string? Evidencia { get; set; }
}
public sealed class CarenciasDto
{
    public bool? Aplica { get; set; }
    public string? Detalle { get; set; }
}
public sealed class ExclusionesDto
{
    public bool? Aplica { get; set; }
    public string? TextoCitado { get; set; }
    public string? Clausula { get; set; }
}

public sealed class AlertaFraudeDto
{
    public string? Hallazgo { get; set; }
    public string? Documento { get; set; }
    public string? PorQue { get; set; }
}

public sealed class RecomendacionDto
{
    public string? Resolucion { get; set; }
    public string? MotivoPrincipal { get; set; }
    public string? NivelConfianza { get; set; }
    public RiesgoLegalDto? RiesgoLegal { get; set; }
}
public sealed class RiesgoLegalDto
{
    public string? Nivel { get; set; }
    public string? Razon { get; set; }
}

public sealed class BloqueRedactorDto
{
    public string? TipoResolucion { get; set; }
    public string? MotivoPrincipal { get; set; }
    public List<string> ElementosClave { get; set; } = new();
    public List<string> Alertas { get; set; } = new();
}

// ── ViewModel de la pantalla de resultado ──────────────────────────────────
public sealed class AuditoriaCasoViewModel
{
    public Guid CaseCode { get; set; }
    public string? NumeroSobre { get; set; }
    public string? NombreTitular { get; set; }
    public string? Producto { get; set; }
    public string? NumeroContrato { get; set; }

    public List<DataFile> Documentos { get; set; } = new();

    public bool Generada { get; set; }
    public AuditoriaCasoDto? Dictamen { get; set; }
    public string? RawText { get; set; }
    public DateTime? GeneradoEn { get; set; }
    public string? GeneradoPor { get; set; }
    public string? Error { get; set; }
}
