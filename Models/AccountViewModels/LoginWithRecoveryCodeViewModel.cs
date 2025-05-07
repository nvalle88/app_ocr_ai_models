#region Using

using System.ComponentModel.DataAnnotations;

#endregion

namespace app_ocr_ai_models.Models.AccountViewModels
{
    public class LoginWithRecoveryCodeViewModel
    {
        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "Recovery Code")]
        public string RecoveryCode { get; set; }
    }
}
