using System;
using System.Collections.Generic;
using app_tramites.Models.ModelAi;
using Microsoft.EntityFrameworkCore;
namespace app_ocr_ai_models.Data;

public partial class OCRDbContext : DbContext
{
    public OCRDbContext()
    {
    }

    public OCRDbContext(DbContextOptions<OCRDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Agent> Agent { get; set; }

    public virtual DbSet<AzureBlobConf> AzureBlobConf { get; set; }

    public virtual DbSet<DataFile> DataFile { get; set; }

    public virtual DbSet<OCRPlatform> OCRPlatform { get; set; }

    public virtual DbSet<OCRSetting> OCRSetting { get; set; }

    public virtual DbSet<OPAIConfiguration> OPAIConfiguration { get; set; }

    public virtual DbSet<OPAIModelPrompt> OPAIModelPrompt { get; set; }

    public virtual DbSet<OPAIPrompt> OPAIPrompt { get; set; }

    public virtual DbSet<Process> Process { get; set; }

    public virtual DbSet<ProcessCase> ProcessCase { get; set; }
    public virtual DbSet<CaseReview> CaseReview { get; set; }

    public virtual DbSet<Note> Note { get; set; }

    public virtual DbSet<ProcessStep> ProcessStep { get; set; }

    public virtual DbSet<StepExecution> StepExecution { get; set; }

    public virtual DbSet<Usage> Usage { get; set; }

    public virtual DbSet<FinalResponseConfig> FinalResponseConfig { get; set; }

    public virtual DbSet<FinalResponseResult> FinalResponseResult { get; set; }

    // REQ-019 T1: entidades nuevas del motor Claude
    public virtual DbSet<OPAITool> OPAITool { get; set; }

    public virtual DbSet<OPAIModelTool> OPAIModelTool { get; set; }

    public virtual DbSet<OPAISkill> OPAISkill { get; set; }

    public virtual DbSet<OPAIModelSkill> OPAIModelSkill { get; set; }

    public virtual DbSet<ToolInvocation> ToolInvocation { get; set; }

    public virtual DbSet<AgentProcess> AgentProcesses { get; set; }
    public virtual DbSet<Policys> Policies { get; set; }
    public virtual DbSet<AccessAgentPolicy> AccessAgentPolicies { get; set; }
    public virtual DbSet<PolicyUser> PolicyUsers { get; set; }
    public virtual DbSet<RolProcess> RolProcesses { get; set; }
    public DbSet<Catalog> Catalog { get; set; }

    // REQ-019 T3: configuración multi-cuenta Zendesk
    public virtual DbSet<ZendeskConf> ZendeskConf { get; set; }

    // REQ-019 / clasificación de documentos de reembolso
    public virtual DbSet<DataFilePage> DataFilePage { get; set; }

    public virtual DbSet<DocumentoClasificacion> DocumentoClasificacion { get; set; }

    // REQ-019m — tipificación profunda
    public virtual DbSet<DocumentoProcedimiento> DocumentoProcedimiento { get; set; }
    public virtual DbSet<ClasificacionSobre> ClasificacionSobre { get; set; }

    /// <summary>REQ-020: la sesión del afiliado en el portal del cliente.</summary>
    public virtual DbSet<SolicitudCliente> SolicitudCliente { get; set; }
    public virtual DbSet<CatalogoCodigoLiquidacion> CatalogoCodigoLiquidacion { get; set; }
    public virtual DbSet<CatalogoBeneficioCorrelacion> CatalogoBeneficioCorrelacion { get; set; }

    public virtual DbSet<DocumentoItem> DocumentoItem { get; set; }

    public virtual DbSet<DocumentoTag> DocumentoTag { get; set; }

    public virtual DbSet<DocumentoDiagnostico> DocumentoDiagnostico { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK__OPAIMode__A25C5AA6EC87418D");

            entity.HasIndex(e => new { e.ConfigCode, e.IsActive }, "IX_OPAIModel_ConfigCode_IsActive");

            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ConfigCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ModifiedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.Name)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.VersionNumber).HasDefaultValue(1);

            entity.HasOne(d => d.AgentConfig).WithMany(p => p.Agent)
                .HasForeignKey(d => d.ConfigCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OPAIModel_Configuration");

            // REQ-019 T1: columnas Claude AI
            entity.Property(e => e.ModelId).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.SystemPrompt).IsUnicode(true);
            entity.Property(e => e.ThinkingMode).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Effort).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.Temperature).HasColumnType("decimal(4,2)");
            entity.Property(e => e.ToolChoice)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("auto");

            entity.HasMany(e => e.OPAIModelTool).WithOne(e => e.ModelCodeNavigation)
                .HasForeignKey(e => e.ModelCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OPAIModelTool_Agent");

            entity.HasMany(e => e.OPAIModelSkill).WithOne(e => e.ModelCodeNavigation)
                .HasForeignKey(e => e.ModelCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OPAIModelSkill_Agent");

            entity.HasMany(e => e.ProcessStep).WithOne(e => e.ModelCodeNavigation)
                .HasForeignKey(e => e.ModelCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProcessStep_Agent");

            entity.HasMany(e => e.StepExecution).WithOne(e => e.ModelCodeNavigation)
                .HasForeignKey(e => e.ModelCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StepExecution_Agent");
        });

        modelBuilder.Entity<AzureBlobConf>(entity =>
        {
            entity.HasKey(e => e.Codigo).HasName("PK__AzureBlo__06370DAD99872AA4");

            entity.Property(e => e.Codigo)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ConnectionString)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.ContainerName)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Catalog>(entity =>
        {
            entity.ToTable("Catalog");
            entity.HasKey(e => e.Id).HasName("PK_Catalog");
            entity.HasIndex(e => e.Code, "UK_Catalog").IsUnique();
            entity.Property(e => e.Code)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.CodeType)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Description)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<DataFile>(entity =>
        {
            entity.HasIndex(e => e.CaseCode, "IX_DataFile_CaseCode");

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getutcdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.FileUri)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.Text).IsUnicode(false);

            entity.HasOne(d => d.CaseCodeNavigation).WithMany(p => p.DataFile)
                .HasForeignKey(d => d.CaseCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DataFile_ProcessCase");

            // REQ-019 T1/T19: Files API de Claude
            entity.Property(e => e.ClaudeFileId)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<OCRPlatform>(entity =>
        {
            entity.HasKey(e => e.PlatformCode).HasName("PK_OCRP_Code");

            entity.HasIndex(e => e.Name, "UQ_OCRP_Name").IsUnique();

            entity.Property(e => e.PlatformCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LanguageSupport)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.Name)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.PricePerPage).HasColumnType("decimal(10, 4)");
        });

        modelBuilder.Entity<OCRSetting>(entity =>
        {
            entity.HasKey(e => new { e.SettingCode, e.PlatformCode }).HasName("PK_OCRS_Composite");

            entity.Property(e => e.SettingCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.PlatformCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.ApiKey)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.Endpoint)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ModelId)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.Name)
                .HasMaxLength(250)
                .IsUnicode(false);

            entity.HasOne(d => d.PlatformCodeNavigation).WithMany(p => p.OCRSetting)
                .HasForeignKey(d => d.PlatformCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OCRS_PlatformCode");
        });

        modelBuilder.Entity<OPAIConfiguration>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK__OPAIConf__A25C5AA6FD9EF12B");

            entity.HasIndex(e => e.IsActive, "IX_OPAIConfiguration_IsActive");

            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ApiKey)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.ConfigType)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.EndpointUrl)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ModifiedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.Name)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.Notes)
                .HasMaxLength(1500)
                .IsUnicode(false);

            // REQ-019 T1: proveedor y referencia a secreto
            entity.Property(e => e.Provider)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("AzureOpenAI");
            entity.Property(e => e.SecretRef)
                .HasMaxLength(250)
                .IsUnicode(false);
        });

        modelBuilder.Entity<OPAIModelPrompt>(entity =>
        {
            entity.HasKey(e => new { e.ModelCode, e.PromptCode });

            entity.HasIndex(e => new { e.ModelCode, e.Order }, "IX_OPAIModelPrompt_ModelCode_Order");

            entity.Property(e => e.ModelCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.PromptCode)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.ModelCodeNavigation).WithMany(p => p.OPAIModelPrompt)
                .HasForeignKey(d => d.ModelCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OPAIModelPrompt_Model");

            entity.HasOne(d => d.PromptCodeNavigation).WithMany(p => p.OPAIModelPrompt)
                .HasForeignKey(d => d.PromptCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OPAIModelPrompt_Prompt");
        });

        modelBuilder.Entity<OPAIPrompt>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK__OPAIProm__A25C5AA669CD99E6");

            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Content).IsUnicode(false);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ModifiedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.VersionNumber).HasDefaultValue(1);
        });

        modelBuilder.Entity<Process>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK__ProcessD__E868B50EAA7CACE8");

            entity.Property(e => e.Code)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Description).IsUnicode(false);
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .IsUnicode(false);

            // REQ-019 T1: versionado y clonado
            entity.Property(e => e.ClonedFromCode).HasMaxLength(30).IsUnicode(false);
            entity.Property(e => e.VersionNumber).HasDefaultValue(1);
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(e => e.ClonedFrom).WithMany(e => e.ClonedProcesses)
                .HasForeignKey(e => e.ClonedFromCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Process_ClonedFrom");

            entity.HasMany(e => e.ProcessStep).WithOne(e => e.ProcessCodeNavigation)
                .HasForeignKey(e => e.ProcessCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProcessStep_Process");
        });



        modelBuilder.Entity<ProcessCase>(entity =>
        {
            entity.HasKey(e => e.CaseCode).HasName("PK__ProcessC__F536950C493032E1");

            entity.Property(e => e.CaseCode).HasDefaultValueSql("(newid())");
            entity.Property(e => e.DefinitionCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.StartDate).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Started");

            entity.HasOne(d => d.DefinitionCodeNavigation).WithMany(p => p.ProcessCase)
                .HasForeignKey(d => d.DefinitionCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProcessCase_Definition");

            
        });

        modelBuilder.Entity<CaseReview>(entity =>
        {
            entity.HasKey(e => e.ReviewId);
            entity.Property(e => e.ReviewId).HasDefaultValueSql("NEWID()");
            entity.Property(e => e.CreatedAt)
                  .HasDefaultValueSql("DATEADD(HOUR, -5, GETUTCDATE())"); // Hora Ecuador
            entity.Property(e => e.ReviewText).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);

            // Relación 1:N
            entity.HasOne(e => e.Case)
                  .WithMany(p => p.CaseReviews) // Aquí se enlaza la colección de ProcessCase
                  .HasForeignKey(e => e.CaseCode)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.ToTable("Notes");
            entity.HasKey(e => e.NoteId);

            entity.Property(e => e.CaseCode)
                  .HasColumnType("uniqueidentifier")
                  .IsRequired();

            entity.Property(e => e.CreatedAt)
                  .HasColumnType("datetime2(0)")
                  .HasDefaultValueSql("SYSUTCDATETIME()");

            // Si quieres relación formal con ProcessCase:
            entity.HasOne(n => n.ProcessCase)
                    .WithMany(pc => pc.Notes)
                    .HasForeignKey(n => n.CaseCode)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_Notes_ProcessCase");
        });

        // REQ-019 T1: reactivado — el motor nuevo necesita ProcessStep
        modelBuilder.Entity<ProcessStep>(entity =>
        {
            entity.HasKey(e => new { e.ProcessCode, e.StepOrder }).HasName("PK__ProcessS__6E33D9169CD4811E");

            entity.Property(e => e.ProcessCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.ModelCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.StepName)
                .HasMaxLength(200)
                .IsUnicode(false);
            // SourceType se almacena como int (enum)
        });

        // REQ-019 T1: reactivado — StepExecution registra la ejecución de cada paso
        modelBuilder.Entity<StepExecution>(entity =>
        {
            entity.HasKey(e => e.ExecutionId).HasName("PK__StepExec__473088C52A0DECD5");

            entity.Property(e => e.ApiKey)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.EndpointUrl)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.ModelCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.StartDate).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Pending");
        });

        modelBuilder.Entity<Usage>(entity =>
        {
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            // REQ-019 T1: FK al motor nuevo (nullable — régimen doble D1/D5)
            entity.HasOne(d => d.Execution).WithMany(p => p.Usage)
                .HasForeignKey(d => d.ExecutionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Usage_StepExecution");

            // FK legacy a FinalResponseResult se mantiene vía DataAnnotations en Usage.cs
        });

        // REQ-019 T1: reactivado — FinalResponseConfig controla el paso de síntesis final
        modelBuilder.Entity<FinalResponseConfig>(entity =>
        {
            entity.ToTable("FinalResponseConfig");
            entity.HasKey(e => e.ConfigCode);
            entity.Property(e => e.ConfigCode)
                  .HasMaxLength(50)
                  .IsRequired();
            entity.Property(e => e.ProcessCode)
                  .HasMaxLength(30)
                  .IsRequired();
            entity.Property(e => e.AgentCode)
                  .HasMaxLength(50)
                  .IsRequired();
            entity.Property(e => e.PromptTemplate)
                  .IsRequired();
            entity.Property(e => e.IncludedStepOrders)
                  .HasMaxLength(100)
                  .HasDefaultValue("*")
                  .IsRequired();
            entity.Property(e => e.UseOriginalText)
                  .HasDefaultValue(true);
            entity.Property(e => e.IncludeStepNames)
                  .HasDefaultValue(false);
            entity.Property(e => e.IncludeFileCount)
                  .HasDefaultValue(true);
            entity.Property(e => e.IsEnabled)
                  .HasDefaultValue(true);
            entity.Property(e => e.MetadataJson)
                  .HasColumnType("NVARCHAR(MAX)")
                  .IsRequired(false);
            entity.HasIndex(e => e.ProcessCode);
            entity.HasIndex(e => e.AgentCode);
        });

        // FinalResponseResult
        modelBuilder.Entity<FinalResponseResult>(entity =>
        {
            entity.ToTable("FinalResponseResult");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ResponseText)
                  .IsRequired();
            entity.Property(e => e.CreatedDate)
                  .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(e => e.CaseCode);
            
        });

        modelBuilder.Entity<Policys>(entity =>
        {
            // Nombre de la tabla
            entity.ToTable("Policys");

            // Clave Primaria (varchar)
            entity.HasKey(p => p.Code);

            // Propiedades
            entity.Property(p => p.Code)
                .HasColumnType("varchar(30)")
                .IsRequired(); // Ya implícito por HasKey

            entity.Property(p => p.PolicyName)
                .HasColumnType("NVARCHAR(100)")
                .IsUnicode(false)
                .IsRequired();

            // Constraint Único
            entity.HasIndex(p => p.PolicyName)
                .IsUnique(); // SQL: UNIQUE (PolicyName)
        });

        modelBuilder.Entity<AgentProcess>(entity =>
        {
            entity.ToTable("AgentProcess");

            // Clave Primaria (int IDENTITY)
            entity.HasKey(ap => ap.Id);
            entity.Property(ap => ap.Id).ValueGeneratedOnAdd(); // SQL: IDENTITY

            // Propiedades
            entity.Property(ap => ap.DefinitionCode).HasColumnType("varchar(30)").IsRequired();
            entity.Property(ap => ap.AgentCode).HasColumnType("varchar(50)").IsRequired();

            // Constraint Único Compuesto
            entity.HasIndex(ap => new { ap.DefinitionCode, ap.AgentCode })
                .IsUnique(); // SQL: UK_ProcesoAgente

            // Relación FK a Process (basada en una clave NO primaria)
            entity.HasOne(ap => ap.Process)
                .WithMany(p => p.AgentProcesses)
                .HasForeignKey(ap => ap.DefinitionCode) // FK en esta tabla
                .HasPrincipalKey(p => p.Code); // PK (o clave principal) en la tabla 'Process'

            // Relación FK a Agent (basada en una clave NO primaria)
            entity.HasOne(ap => ap.Agent)
                .WithMany(a => a.AgentProcesses)
                .HasForeignKey(ap => ap.AgentCode) // FK en esta tabla
                .HasPrincipalKey(a => a.Code); // PK (o clave principal) en la tabla 'Agent'
        });

        modelBuilder.Entity<AccessAgentPolicy>(entity =>
        {
            entity.ToTable("AccessAgentPolicy");

            // Clave Primaria (int IDENTITY)
            entity.HasKey(aap => aap.Id);
            entity.Property(aap => aap.Id).ValueGeneratedOnAdd(); // SQL: IDENTITY

            // Propiedades
            entity.Property(aap => aap.PolicyCode).HasColumnType("varchar(30)").IsRequired();
            entity.Property(aap => aap.AgentProcessId).IsRequired();

            entity.Property(aap => aap.Status)
                .IsRequired()
                .HasDefaultValue(true); // SQL: DEFAULT 1

            // Constraint Único Compuesto
            entity.HasIndex(aap => new { aap.PolicyCode, aap.AgentProcessId })
                .IsUnique(); // SQL: UK_PoliticaAgenteAcceso

            // Índice simple
            entity.HasIndex(aap => aap.AgentProcessId); // SQL: IX_AccessAgentPolicy_AgentProcessID

            // Relación FK a Policys (basada en una clave NO primaria)
            entity.HasOne(aap => aap.Policy)
                .WithMany(p => p.AccessAgentPolicies)
                .HasForeignKey(aap => aap.PolicyCode) // FK en esta tabla
                .HasPrincipalKey(p => p.Code); // PK (o clave principal) en la tabla 'Policys'

            // Relación FK a AgentProcess (basada en la PK estándar)
            entity.HasOne(aap => aap.AgentProcess)
                .WithMany(ap => ap.AccessAgentPolicies)
                .HasForeignKey(aap => aap.AgentProcessId); // FK en esta tabla (EF lo infiere, pero es bueno ser explícito)
        });

        // --- Mapeo de PolicyUser ---
        modelBuilder.Entity<PolicyUser>(entity =>
        {
            entity.ToTable("PolicyUser");
            // 1. Clave Primaria (PK_PolicyUser)
            // Usamos la columna 'Id' como PK simple (Identity)
            entity.HasKey(e => e.Id)
                  .HasName("PK_PolicyUser");

            // 2. Restricción ÚNICA (UK_PolicyUser)
            // Asegura que no haya duplicados de la combinación PolicyCode y UserId
            entity.HasIndex(e => new { e.PolicyCode, e.UserId })
                  .IsUnique()
                  .HasDatabaseName("UK_PolicyUser"); // Opcional: Nombre de restricción

            // 3. Clave Foránea a AspNetUsers (FK_PolicyUser_User)
            entity.HasOne(e => e.User)
                  .WithMany() // Muchos PolicyUser pueden estar relacionados con un User
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade) // Comportamiento al borrar
                  .HasConstraintName("FK_PolicyUser_User");

            // 4. Clave Foránea a Policys (FK_PolicyUsedr_Policy)
            entity.HasOne(e => e.Policys)
                  .WithMany() // Muchos PolicyUser pueden estar relacionados con una Policy
                  .HasForeignKey(e => e.PolicyCode)
                  .OnDelete(DeleteBehavior.Restrict) // Comportamiento al borrar
                  .HasConstraintName("FK_PolicyUsedr_Policy");

            // 5. Mapeo de Columnas (Opcional, si los nombres de C# no coinciden con la DB)
            entity.Property(e => e.PolicyCode).HasColumnType("VARCHAR(30)");
            // Las propiedades de IdentityUser.Id ya están configuradas a nvarchar(450) por defecto
        });

        modelBuilder.Entity<RolProcess>(entity =>
        {
            entity.ToTable("RolProcess");
            // 1. Clave Primaria (PK_RolProcess)
            // Usamos la columna 'Id' como PK simple (Identity)
            entity.HasKey(e => e.Id)
                  .HasName("PK_RolProcess");

            // 2. Restricción ÚNICA (UK_RolProcess)
            entity.HasIndex(e => new { e.ProcessCode, e.RolId })
                  .IsUnique()
                  .HasDatabaseName("UK_RolProcess");

            // 3. Clave Foránea a AspNetRoles (FK_RolProcess_Roles)
            entity.HasOne(e => e.Rol)
                  .WithMany()
                  .HasForeignKey(e => e.RolId)
                  .OnDelete(DeleteBehavior.Cascade)
                  .HasConstraintName("FK_RolProcess_Roles");

            // 4. Clave Foránea a Process (FK_RolProcess_Process)
            entity.HasOne(e => e.Process)
                  .WithMany()
                  .HasForeignKey(e => e.ProcessCode)
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_RolProcess_Process");

            // 5. Mapeo de Columnas
            entity.Property(e => e.ProcessCode).HasColumnType("VARCHAR(30)");
        });

        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.ToTable("AspNetRoles"); // Mapea a la tabla AspNetRoles
        });

        // REQ-019 T1: nuevas entidades del motor Claude ----------------------------------------

        modelBuilder.Entity<OPAITool>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK_OPAITool");
            entity.Property(e => e.Code).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(250).IsUnicode(false);
            entity.Property(e => e.Description).HasMaxLength(500); // nvarchar(500)
            entity.Property(e => e.BindingType).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Strict).HasDefaultValue(true);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.VersionNumber).HasDefaultValue(1);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.HasIndex(e => e.IsActive, "IX_OPAITool_IsActive");
        });

        modelBuilder.Entity<OPAIModelTool>(entity =>
        {
            entity.HasKey(e => new { e.ModelCode, e.ToolCode });
            entity.Property(e => e.ModelCode).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.ToolCode).HasMaxLength(50).IsUnicode(false);
            // SortOrder en C# → columna [Order] en SQL (palabra reservada en T-SQL)
            entity.Property(e => e.SortOrder).HasColumnName("Order").HasDefaultValue(0);
            entity.Property(e => e.IsEnabled).HasDefaultValue(true);

            entity.HasOne(e => e.ToolCodeNavigation).WithMany(e => e.OPAIModelTool)
                .HasForeignKey(e => e.ToolCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OPAIModelTool_Tool");

            entity.HasIndex(e => e.ToolCode, "IX_OPAIModelTool_ToolCode");
        });

        modelBuilder.Entity<OPAISkill>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK_OPAISkill");
            entity.Property(e => e.Code).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.SkillType).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.SkillId).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.SkillVersion).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(250).IsUnicode(false);
            entity.Property(e => e.Description).HasMaxLength(500); // nvarchar(500)
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.VersionNumber).HasDefaultValue(1);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<OPAIModelSkill>(entity =>
        {
            entity.HasKey(e => new { e.ModelCode, e.SkillCode });
            entity.Property(e => e.ModelCode).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.SkillCode).HasMaxLength(50).IsUnicode(false);
            // SortOrder en C# → columna [Order] en SQL (palabra reservada en T-SQL)
            entity.Property(e => e.SortOrder).HasColumnName("Order").HasDefaultValue(0);
            entity.Property(e => e.IsEnabled).HasDefaultValue(true);

            entity.HasOne(e => e.SkillCodeNavigation).WithMany(e => e.OPAIModelSkill)
                .HasForeignKey(e => e.SkillCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OPAIModelSkill_Skill");

            entity.HasIndex(e => e.SkillCode, "IX_OPAIModelSkill_SkillCode");
        });

        modelBuilder.Entity<ToolInvocation>(entity =>
        {
            entity.HasKey(e => e.InvocationId).HasName("PK_ToolInvocation");
            entity.Property(e => e.InvocationId).UseIdentityColumn();
            entity.Property(e => e.ToolCode).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.IsError).HasDefaultValue(false);
            entity.Property(e => e.StartDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.EndDate).HasColumnType("datetime");

            entity.HasOne(e => e.Execution).WithMany(p => p.ToolInvocation)
                .HasForeignKey(e => e.ExecutionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ToolInvocation_Execution");

            entity.HasIndex(e => e.ExecutionId, "IX_ToolInvocation_ExecutionId");
        });

        // REQ-019 T3: configuración multi-cuenta Zendesk
        modelBuilder.Entity<ZendeskConf>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK_ZendeskConf");

            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Cuenta)
                .HasMaxLength(30)
                .IsUnicode(false)
                .IsRequired();
            entity.Property(e => e.Subdomain)
                .HasMaxLength(100)
                .IsUnicode(false)
                .IsRequired();
            entity.Property(e => e.SecretRef)
                .HasMaxLength(250)
                .IsUnicode(false);
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);

            entity.HasIndex(e => e.IsActive, "IX_ZendeskConf_IsActive");
        });

        // REQ-019 / clasificación de documentos de reembolso -----------------------------------
        // Los nombres de entidad coinciden con los de tabla, así que no hace falta ToTable().

        modelBuilder.Entity<DataFilePage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_DataFilePage");

            entity.HasIndex(e => new { e.DataFileId, e.PageNumber }, "UQ_DataFilePage_File_Page")
                .IsUnique();

            entity.Property(e => e.Text).IsUnicode(false);   // varchar(max), igual que DataFile.Text
            entity.Property(e => e.Unit).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Width).HasColumnType("real");
            entity.Property(e => e.Height).HasColumnType("real");
            entity.Property(e => e.Angle).HasColumnType("real");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.DataFileNavigation).WithMany(p => p.DataFilePage)
                .HasForeignKey(d => d.DataFileId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_DataFilePage_DataFile");
        });

        modelBuilder.Entity<DocumentoClasificacion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_DocumentoClasificacion");

            // Índice FILTRADO: una sola clasificación vigente por archivo
            entity.HasIndex(e => e.DataFileId, "UQ_DocumentoClasificacion_Current")
                .IsUnique()
                .HasFilter("[IsCurrent] = 1");

            entity.HasIndex(e => e.TipoArchivo, "IX_DocumentoClasificacion_TipoArchivo");

            entity.Property(e => e.TipoArchivo).HasMaxLength(30).IsUnicode(false);
            entity.Property(e => e.ListaTipoArchivo).HasMaxLength(400).IsUnicode(false);
            entity.Property(e => e.NumeroFactura).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.ClaveAcceso).HasMaxLength(60).IsUnicode(false);
            entity.Property(e => e.ModelCode).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.ValorTotal).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            entity.Property(e => e.Confianza).HasColumnType("decimal(5,4)");
            entity.Property(e => e.RawJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.EsFacturaValida).HasDefaultValue(false);
            entity.Property(e => e.VersionNumber).HasDefaultValue(1);
            entity.Property(e => e.IsCurrent).HasDefaultValue(true);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.DataFileNavigation).WithMany(p => p.DocumentoClasificacion)
                .HasForeignKey(d => d.DataFileId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_DocumentoClasificacion_DataFile");
        });

        modelBuilder.Entity<DocumentoItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_DocumentoItem");
            entity.Property(e => e.Id).UseIdentityColumn();

            entity.HasIndex(e => new { e.DataFileId, e.PageNumber }, "IX_DocumentoItem_DataFileId");
            entity.HasIndex(e => e.TipoRubro, "IX_DocumentoItem_TipoRubro");

            entity.Property(e => e.Descripcion).HasMaxLength(500).IsUnicode(false);
            entity.Property(e => e.TipoRubro).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.NumeroFactura).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.Cantidad).HasColumnType("decimal(18,4)");
            entity.Property(e => e.ValorUnitario).HasColumnType("decimal(18,4)");
            entity.Property(e => e.ValorTotal).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Confianza).HasColumnType("decimal(5,4)");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.DataFileNavigation).WithMany(p => p.DocumentoItem)
                .HasForeignKey(d => d.DataFileId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_DocumentoItem_DataFile");
        });

        modelBuilder.Entity<DocumentoTag>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_DocumentoTag");
            entity.Property(e => e.Id).UseIdentityColumn();

            entity.HasIndex(e => new { e.DataFileId, e.PageNumber, e.Tag, e.Origen },
                    "UQ_DocumentoTag_Unico")
                .IsUnique();

            entity.HasIndex(e => e.Tag, "IX_DocumentoTag_Tag");

            entity.Property(e => e.Tag).HasMaxLength(60).IsUnicode(false);
            entity.Property(e => e.Categoria).HasMaxLength(30).IsUnicode(false);
            entity.Property(e => e.Origen).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.Valor).HasMaxLength(200).IsUnicode(false);
            entity.Property(e => e.Confianza).HasColumnType("decimal(5,4)");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.DataFileNavigation).WithMany(p => p.DocumentoTag)
                .HasForeignKey(d => d.DataFileId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_DocumentoTag_DataFile");
        });

        modelBuilder.Entity<DocumentoDiagnostico>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_DocumentoDiagnostico");
            entity.Property(e => e.Id).UseIdentityColumn();

            entity.HasIndex(e => new { e.DataFileId, e.Codigo },
                    "UQ_DocumentoDiagnostico_File_Codigo")
                .IsUnique();

            entity.HasIndex(e => e.Codigo, "IX_DocumentoDiagnostico_Codigo");

            entity.Property(e => e.Codigo).HasMaxLength(10).IsUnicode(false);
            entity.Property(e => e.CodigoOriginal).HasMaxLength(15).IsUnicode(false);
            entity.Property(e => e.Descripcion).HasMaxLength(300).IsUnicode(false);
            entity.Property(e => e.ValorAsignado).HasColumnType("decimal(18,2)");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.DataFileNavigation).WithMany(p => p.DocumentoDiagnostico)
                .HasForeignKey(d => d.DataFileId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_DocumentoDiagnostico_DataFile");
        });

        // ── REQ-019m: tipificación profunda ────────────────────────────────
        modelBuilder.Entity<DocumentoProcedimiento>(e =>
        {
            e.ToTable("DocumentoProcedimiento");
            e.HasKey(x => x.Id);
            e.Property(x => x.CodigoCpt).HasMaxLength(20);
            e.Property(x => x.Descripcion).HasMaxLength(500).IsRequired();
            e.Property(x => x.CodigoLiquidacion).HasMaxLength(20);
            e.Property(x => x.RubroLiquidacion).HasMaxLength(250);
            e.Property(x => x.OrigenMatch).HasMaxLength(20);
            e.Property(x => x.NombreLr05).HasMaxLength(400);
            e.Property(x => x.CodigoBeneficio).HasMaxLength(10);
            e.Property(x => x.TipoMedicina).HasMaxLength(20);
            e.Property(x => x.ScoreHomologacion).HasColumnType("decimal(5,3)");
            e.Property(x => x.EstadoCorrelacion).HasMaxLength(20);
            e.Property(x => x.CorrelacionDx).HasMaxLength(10);
            e.Property(x => x.CreatedDate).HasColumnType("datetime");
            e.HasOne(x => x.DataFileNavigation)
             .WithMany()
             .HasForeignKey(x => x.DataFileId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DataFileId);
        });

        modelBuilder.Entity<ClasificacionSobre>(e =>
        {
            e.ToTable("ClasificacionSobre");
            e.HasKey(x => x.Id);
            e.Property(x => x.TipoAtencion).HasMaxLength(30).IsRequired();
            e.Property(x => x.Justificacion).HasMaxLength(2000);
            e.Property(x => x.TipoPredominante).HasMaxLength(30);
            e.Property(x => x.ModelCode).HasMaxLength(50);
            e.Property(x => x.TotalSobre).HasColumnType("decimal(18,2)");
            e.Property(x => x.CreatedDate).HasColumnType("datetime");
            // Una sola vigente por caso: el índice filtrado lo garantiza en BD
            e.HasIndex(x => x.CaseCode)
             .HasFilter("[IsCurrent] = 1")
             .IsUnique()
             .HasDatabaseName("UQ_ClasificacionSobre_Vigente");
        });

        modelBuilder.Entity<SolicitudCliente>(e =>
        {
            e.ToTable("SolicitudCliente");
            e.HasKey(x => x.Id);
            e.Property(x => x.Cedula).HasMaxLength(20).IsRequired();
            e.Property(x => x.NumeroContrato).HasMaxLength(20);
            e.Property(x => x.CodigoProducto).HasMaxLength(20);
            e.Property(x => x.CodigoRegion).HasMaxLength(20);
            e.Property(x => x.CodigoPlan).HasMaxLength(40);
            e.Property(x => x.NombrePlan).HasMaxLength(200);
            e.Property(x => x.NombreTitular).HasMaxLength(200);
            e.Property(x => x.Estado).HasMaxLength(30).IsRequired();
            e.Property(x => x.ValorPresentado).HasColumnType("decimal(18,2)");
            e.Property(x => x.NombreBeneficiario).HasMaxLength(200);
            e.Property(x => x.CedulaBeneficiario).HasMaxLength(20);
            e.Property(x => x.RelacionBeneficiario).HasMaxLength(60);
            e.Property(x => x.GeneroBeneficiario).HasMaxLength(5);
            e.Property(x => x.DeducibleCubierto).HasColumnType("decimal(18,2)");
            // Un caso, una solicitud: el índice único lo garantiza en BD y no
            // depende de que el código se acuerde de comprobarlo.
            e.HasIndex(x => x.CaseCode).IsUnique()
             .HasDatabaseName("IX_SolicitudCliente_CaseCode");
            e.HasIndex(x => new { x.Cedula, x.CreatedDate })
             .HasDatabaseName("IX_SolicitudCliente_Cedula");
        });

        modelBuilder.Entity<CatalogoBeneficioCorrelacion>(e =>
        {
            e.ToTable("CatalogoBeneficioCorrelacion");
            e.HasKey(x => x.CodigoBeneficio);
            e.Property(x => x.CodigoBeneficio).HasMaxLength(10);
            e.Property(x => x.Descripcion).HasMaxLength(200);
            e.Property(x => x.TipoMedicina).HasMaxLength(20);
            e.Property(x => x.CreatedDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<CatalogoCodigoLiquidacion>(e =>
        {
            e.ToTable("CatalogoCodigoLiquidacion");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.Property(x => x.Rubro).HasMaxLength(250).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(1000);
            e.Property(x => x.CreatedDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<DocumentoClasificacion>(e =>
        {
            e.Property(x => x.TipoSoporte).HasMaxLength(40);
            e.Property(x => x.ResumenSoporte).HasMaxLength(600);
            e.Property(x => x.EmisorRuc).HasMaxLength(20);
            e.Property(x => x.EmisorNombre).HasMaxLength(250);
            e.Property(x => x.EmisorNombreComercial).HasMaxLength(250);
            e.Property(x => x.EmisorTipo).HasMaxLength(30);
            e.Property(x => x.EmisorCiudad).HasMaxLength(100);
            e.Property(x => x.EmisorPais).HasMaxLength(60);
            e.Property(x => x.NumeroAutorizacion).HasMaxLength(60);
            e.Property(x => x.FechaEmision).HasColumnType("date");
            e.Property(x => x.FechaAtencion).HasColumnType("date");
            e.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
            e.Property(x => x.Iva).HasColumnType("decimal(18,2)");
            e.Property(x => x.Moneda).HasMaxLength(10);
            e.Property(x => x.PacienteSexo).HasMaxLength(10);
            e.Property(x => x.MedicoNombre).HasMaxLength(250);
            e.Property(x => x.MedicoEspecialidad).HasMaxLength(150);
            e.Property(x => x.MedicoRegistro).HasMaxLength(60);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
