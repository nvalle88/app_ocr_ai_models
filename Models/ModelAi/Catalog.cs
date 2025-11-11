using System.ComponentModel.DataAnnotations;

namespace app_tramites.Models.ModelAi;

public class Catalog
{
    [Key]
    public int Id { get; set; }

    // UK_Catalog: Código único del catálogo (VARCHAR(30) NOT NULL)
    [MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    // Tipo de código (VARCHAR(30) NOT NULL)
    [MaxLength(30)]
    public string CodeType { get; set; } = string.Empty;

    // Descripción (NVARCHAR(200) NOT NULL)
    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    // CreatedDate (datetime2(7) NOT NULL)
    public DateTime CreatedDate { get; set; }
}
