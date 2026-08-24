using Newtonsoft.Json;

namespace SmartphoneMonitor.Models
{
    public class LlmAnalysisResponse
    {
        [JsonProperty("is_hot_deal")]
        public bool IsHotDeal { get; set; }

        [JsonProperty("calculated_priority")]
        public double CalculatedPriority { get; set; }

        [JsonProperty("reasoning_steps")]
        public string ReasoningSteps { get; set; } = string.Empty;
    }
}
