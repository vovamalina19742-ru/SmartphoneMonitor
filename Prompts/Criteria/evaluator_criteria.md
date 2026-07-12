# Evaluator Prompt — Quality Criteria
This rubric evaluates the quality and effectiveness of the system prompt used by the AI Valuator (Evaluator Agent) to analyze smartphone listings.

## Dimensions (total: 100)

### Defect Analysis and Cost Estimation (40 points)
- **Defect Detection Rules (20 points):** The prompt clearly instructs how to identify specific defects (screen cracks, FaceID, battery health, iCloud locks) from descriptions.
- **Repair Cost Calculation (20 points):** The prompt defines a clear, unambiguous pricing model for repairs and instructs the model to sum them up correctly.

### Extraction Precision (30 points)
- **Model and Storage (15 points):** The prompt ensures the model correctly isolates the specific model name and storage capacity in GB from the listing title and description.
- **Reseller and Lock Flags (15 points):** The prompt defines clear criteria for flagging commercial listings (is_commercial) and locked/stolen devices (is_stolen).

### Formatting and Constraints (20 points)
- **Strict JSON Output (10 points):** The prompt mandates returning ONLY a valid JSON block, avoiding conversational filler or explanations.
- **Structure and Schema (10 points):** The prompt defines the exact keys and data types expected in the JSON payload, ensuring compatibility with C# parsing.

### Instruction Clarity and Efficiency (10 points)
- **Conciseness (5 points):** The prompt is clear, direct, and avoids redundant phrasing that increases token usage.
- **System Role Definition (5 points):** The prompt establishes a clear persona (assessment expert) to align the model's tone and analytical accuracy.
