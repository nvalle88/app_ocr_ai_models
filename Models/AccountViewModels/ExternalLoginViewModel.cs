#region Using

using System.ComponentModel.DataAnnotations;

#endregion

namespace app_ocr_ai_models.Models.AccountViewModels
{
    public class ExternalLoginViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
    }
}
