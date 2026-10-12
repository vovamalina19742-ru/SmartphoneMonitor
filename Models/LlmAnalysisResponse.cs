using System.Collections.Generic;
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

        [JsonProperty("brand")]
        public string Brand { get; set; } = string.Empty;

        [JsonProperty("model")]
        public string Model { get; set; } = string.Empty;

        [JsonProperty("storage_gb")]
        public int StorageGB { get; set; }

        [JsonProperty("defects")]
        public List<string> Defects { get; set; } = new List<string>();

        [JsonProperty("repair_cost_mdl")]
        public int RepairCostMdl { get; set; }

        [JsonProperty("is_stolen")]
        public bool IsStolen { get; set; }

        [JsonProperty("is_commercial")]
        public bool IsCommercial { get; set; }

        [JsonProperty("condition_score")]
        public int ConditionScore { get; set; } = 10;
    }
}
