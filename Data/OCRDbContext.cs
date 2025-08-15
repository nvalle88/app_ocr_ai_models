using System;
using System.Collections.Generic;
using app_tramites.Models.ModelAi;
using Microsoft.EntityFrameworkCore;
namespace app_ocr_ai_models.Data;

public partial class OCRDbContext: DbContext
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

    public virtual DbSet<ProcessStep> ProcessStep { get; set; }

    public virtual DbSet<StepExecution> StepExecution { get; set; }

    public virtual DbSet<Usage> Usage { get; set; }

    public virtual DbSet<FinalResponseConfig> FinalResponseConfig { get; set; }

    public virtual DbSet<FinalResponseResult> FinalResponseResult { get; set; }
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

            entity.HasOne(d => d.ModelCodeNavigation).WithMany(p => p.ProcessStep)
                .HasForeignKey(d => d.ModelCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProcessStep_Agent");

            entity.HasOne(d => d.ProcessCodeNavigation).WithMany(p => p.ProcessStep)
                .HasForeignKey(d => d.ProcessCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProcessStep_Process");
        });

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

            entity.HasOne(d => d.ModelCodeNavigation).WithMany(p => p.StepExecution)
                .HasForeignKey(d => d.ModelCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StepExecution_Agent");
        });

        modelBuilder.Entity<Usage>(entity =>
        {
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Execution).WithMany(p => p.Usage)
                .HasForeignKey(d => d.ExecutionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Usage_StepExecution");
        });

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
            // MetadataJson mapping
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
            entity.Property(e => e.ExecutionSummary)
                  .HasMaxLength(1000);
            entity.Property(e => e.CreatedDate)
                  .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(e => e.CaseCode);
            entity.HasIndex(e => e.ConfigCode);
            entity.HasIndex(e => e.FileId);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
