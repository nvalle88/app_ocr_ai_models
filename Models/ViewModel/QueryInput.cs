namespace app_tramites.Models.ViewModel
{
    public class OcrFile
    {
        public string FileName { get; set; }
        public string Content { get; set; }
        public string Extension { get; set; }
    }

    public class QueryInput
    {
        public string ProcessCode { get; set; }
        public string Query { get; set; }
        public List<OcrFile> Files { get; set; }
    }

    public class FileDetails
    {
        public string Content { get; set; } = string.Empty;  // Can be base64 if it's a binary file
        public string Extension { get; set; } = string.Empty; // Example: ".pdf"
    }



    public class ResultOpenAi
    {
        public Choice[] choices { get; set; }
        public int created { get; set; }
        public string id { get; set; }
        public string model { get; set; }
        public string _object { get; set; }
        public Prompt_Filter_Results[] prompt_filter_results { get; set; }
        public string system_fingerprint { get; set; }
        public Usage usage { get; set; }
    }

    public class Usage
    {
        public int completion_tokens { get; set; }
        public Completion_Tokens_Details completion_tokens_details { get; set; }
        public int prompt_tokens { get; set; }
        public Prompt_Tokens_Details prompt_tokens_details { get; set; }
        public int total_tokens { get; set; }
    }

    public class Completion_Tokens_Details
    {
        public int accepted_prediction_tokens { get; set; }
        public int audio_tokens { get; set; }
        public int reasoning_tokens { get; set; }
        public int rejected_prediction_tokens { get; set; }
    }

    public class Prompt_Tokens_Details
    {
        public int audio_tokens { get; set; }
        public int cached_tokens { get; set; }
    }

    public class Choice
    {
        public Content_Filter_Results content_filter_results { get; set; }
        public string finish_reason { get; set; }
        public int index { get; set; }
        public object logprobs { get; set; }
        public Message message { get; set; }
    }

    public class Content_Filter_Results
    {
        public Hate hate { get; set; }
        public Protected_Material_Code protected_material_code { get; set; }
        public Protected_Material_Text protected_material_text { get; set; }
        public Self_Harm self_harm { get; set; }
        public Sexual sexual { get; set; }
        public Violence violence { get; set; }
    }

    public class Hate
    {
        public bool filtered { get; set; }
        public string severity { get; set; }
    }

    public class Protected_Material_Code
    {
        public bool filtered { get; set; }
        public bool detected { get; set; }
    }

    public class Protected_Material_Text
    {
        public bool filtered { get; set; }
        public bool detected { get; set; }
    }

    public class Self_Harm
    {
        public bool filtered { get; set; }
        public string severity { get; set; }
    }

    public class Sexual
    {
        public bool filtered { get; set; }
        public string severity { get; set; }
    }

    public class Violence
    {
        public bool filtered { get; set; }
        public string severity { get; set; }
    }

    public class Message
    {
        public object[] annotations { get; set; }
        public string content { get; set; }
        public object refusal { get; set; }
        public string role { get; set; }
    }

    public class Prompt_Filter_Results
    {
        public int prompt_index { get; set; }
        public Content_Filter_Results1 content_filter_results { get; set; }
    }

    public class Content_Filter_Results1
    {
        public Hate1 hate { get; set; }
        public Jailbreak jailbreak { get; set; }
        public Self_Harm1 self_harm { get; set; }
        public Sexual1 sexual { get; set; }
        public Violence1 violence { get; set; }
    }

    public class Hate1
    {
        public bool filtered { get; set; }
        public string severity { get; set; }
    }

    public class Jailbreak
    {
        public bool filtered { get; set; }
        public bool detected { get; set; }
    }

    public class Self_Harm1
    {
        public bool filtered { get; set; }
        public string severity { get; set; }
    }

    public class Sexual1
    {
        public bool filtered { get; set; }
        public string severity { get; set; }
    }

    public class Violence1
    {
        public bool filtered { get; set; }
        public string severity { get; set; }
    }


}
