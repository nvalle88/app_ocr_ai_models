namespace app_tramites.Models.ModelAi;

public partial class Process
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<ProcessCase> ProcessCase { get; set; } = new List<ProcessCase>();

    public virtual ICollection<AgentProcess> AgentProcesses { get; set; } = new List<AgentProcess>();

    public virtual ICollection<ProcessStep> ProcessStep { get; set; } = new List<ProcessStep>();

    // REQ-019 T1: columnas para versionado y clonado de procesos
    /// <summary>FK auto-referencial nullable: código del proceso origen si éste es un clon.</summary>
    public string? ClonedFromCode { get; set; }

    /// <summary>Número de versión del proceso. DEFAULT 1.</summary>
    public int VersionNumber { get; set; }

    /// <summary>Proceso activo o archivado. DEFAULT true.</summary>
    public bool IsActive { get; set; }

    public virtual Process? ClonedFrom { get; set; }

    public virtual ICollection<Process> ClonedProcesses { get; set; } = new List<Process>();
}
