﻿using app_tramites.Models.ViewModel;
using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public partial class OPAIModelPrompt
{
    public string ModelCode { get; set; } = null!;

    public string PromptCode { get; set; } = null!;

    public int Order { get; set; }

    public virtual Agent ModelCodeNavigation { get; set; } = null!;

    public virtual OPAIPrompt PromptCodeNavigation { get; set; } = null!;
}
