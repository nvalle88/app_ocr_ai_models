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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
