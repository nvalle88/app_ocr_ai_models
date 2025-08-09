using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public partial class AzureBlobConf
{
    public string Codigo { get; set; } = null!;

    public string ConnectionString { get; set; } = null!;

    public string ContainerName { get; set; } = null!;
}
