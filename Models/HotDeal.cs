using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartphoneMonitor.Models
{
    public class HotDeal : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private double _recommendationScore;
        public double RecommendationScore
        {
            get => _recommendationScore;
            set
            {
                if (_recommendationScore != value)
                {
                    _recommendationScore = value;
                    OnPropertyChanged(nameof(RecommendationScore));
                    OnPropertyChanged(nameof(ArbitrageScore));
                    OnPropertyChanged(nameof(ScoreBadgeText));
                    OnPropertyChanged(nameof(ScoreBadgeBg));
                    OnPropertyChanged(nameof(ScoreBadgeFg));
                }
            }
        }

        private double _arbitrageScore;
        public double ArbitrageScore
        {
            get => _arbitrageScore > 0 ? _arbitrageScore : RecommendationScore;
            set
            {
                if (_arbitrageScore != value)
                {
                    _arbitrageScore = value;
                    _recommendationScore = value;
                    OnPropertyChanged(nameof(ArbitrageScore));
                    OnPropertyChanged(nameof(RecommendationScore));
                    OnPropertyChanged(nameof(ScoreBadgeText));
                    OnPropertyChanged(nameof(ScoreBadgeBg));
                    OnPropertyChanged(nameof(ScoreBadgeFg));
                }
            }
        }

        private bool _isVisionInspected;
        public bool IsVisionInspected
        {
            get => _isVisionInspected;
            set
            {
                if (_isVisionInspected != value)
                {
                    _isVisionInspected = value;
                    OnPropertyChanged(nameof(IsVisionInspected));
                    OnPropertyChanged(nameof(ScoreBadgeText));
                    OnPropertyChanged(nameof(ScoreBadgeBg));
                    OnPropertyChanged(nameof(ScoreBadgeFg));
                }
            }
        }

        public double VisionDelta { get; set; }
        public bool HasDemandMatch { get; set; }

        public string ScoreBadgeText
        {
            get
            {
                double score = ArbitrageScore;
                if (!IsVisionInspected)
                {
                    return $"⚡ Базовый: {score:F0}/100";
                }
                else
                {
                    return $"👁️ ИИ-Осмотр: {score:F0}/100";
                }
            }
        }

        public string ScoreBadgeBg
        {
            get
            {
                double score = ArbitrageScore;
                if (!IsVisionInspected)
                {
                    return "#F0F9FF";
                }
                if (score >= 75.0) return "#E8F5E9";
                if (score < 50.0) return "#FFEBEE";
                return "#FFF3E0";
            }
        }

        public string ScoreBadgeFg
        {
            get
            {
                double score = ArbitrageScore;
                if (!IsVisionInspected)
                {
                    return "#0284C7";
                }
                if (score >= 75.0) return "#2E7D32";
                if (score < 50.0) return "#C62828";
                return "#E65100";
            }
        }

        public string Title { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public decimal PriceValue { get; set; }
        public decimal BrandMedian { get; set; }
        public double DiscountPercent { get; set; }
        public string Url { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string SellerName { get; set; } = string.Empty;
        public int Views { get; set; }
        public int BatteryHealth { get; set; }
        public int StorageGB { get; set; }
        public int DaysOld { get; set; } = -1;
        public DateTime PostedDate { get; set; } = DateTime.Now;
        public string AuthorLogin { get; set; } = string.Empty;
        public bool IsNew { get; set; }

        public string NewSmartphoneCategory { get; set; } = "PrivateUsed";

        public string CategoryBadgeText
        {
            get
            {
                return NewSmartphoneCategory switch
                {
                    "RetailChain" => "🏢 Крупный ритейл",
                    "Shop999" => "🏪 Магазин 999",
                    "PrivateNew" => "👤 Частник (Новый)",
                    _ => IsNew ? "✨ Новый" : "📱 б/у"
                };
            }
        }

        public string CategoryBadgeBg
        {
            get
            {
                return NewSmartphoneCategory switch
                {
                    "RetailChain" => "#F3E8FF",
                    "Shop999" => "#E0F2FE",
                    "PrivateNew" => "#FEF3C7",
                    _ => "#F3F4F6"
                };
            }
        }

        public string CategoryBadgeFg
        {
            get
            {
                return NewSmartphoneCategory switch
                {
                    "RetailChain" => "#6B21A8",
                    "Shop999" => "#0369A1",
                    "PrivateNew" => "#B45309",
                    _ => "#374151"
                };
            }
        }

        public decimal RetailPrice { get; set; }
        public string RetailShopName { get; set; } = string.Empty;
        public decimal RetailSavings { get; set; }
        public double RetailSavingsPercent { get; set; }
        public bool HasRetailComparison => RetailPrice > 0 && RetailSavings > 0;

        public string RetailComparisonText
        {
            get
            {
                if (!HasRetailComparison) return string.Empty;
                return $"🛒 В магазинах ({RetailShopName}): {RetailPrice:F0} MDL  |  💡 Реальная выгода: -{RetailSavings:F0} MDL (-{RetailSavingsPercent:F0}%)";
            }
        }

        public string StorageLabel
        {
            get
            {
                string label = "";
                if (StorageGB > 0)
                {
                    if (StorageGB != 1024)
                    {
                        label = $"{StorageGB} GB";
                    }
                    else
                    {
                        label = "1 TB";
                    }
                }
                if (BatteryHealth > 0)
                {
                    if (!string.IsNullOrEmpty(label))
                    {
                        label += " | ";
                    }
                    label += $"🔋 {BatteryHealth}%";
                }
                return label;
            }
        }

        public string AgeLabel
        {
            get
            {
                int daysOld = DaysOld;
                if (daysOld == 0) return "сегодня";
                if (daysOld == 1) return "вчера";
                if (daysOld > 1)
                {
                    return PostedDate.ToString("d MMMM", new System.Globalization.CultureInfo("ru-RU"));
                }
                return "";
            }
        }

        public string ViewsLabel
        {
            get
            {
                if (Views <= 0)
                {
                    return "";
                }
                return $"👁 {Views}";
            }
        }

        public List<string> Defects { get; set; } = new List<string>();
        public decimal RepairCost { get; set; }
        public decimal NetProfitMargin { get; set; }
        public bool IsStolen { get; set; }

        public string DefectsLabel
        {
            get
            {
                if (Defects == null || Defects.Count == 0)
                {
                    return string.Empty;
                }
                return "🔧 " + string.Join(", ", Defects) + (RepairCost > 0m ? $" (+{RepairCost:F0} MDL ремонт)" : "");
            }
        }

        public string MarginLabel
        {
            get
            {
                if (NetProfitMargin <= 0m)
                {
                    return string.Empty;
                }
                return $"💸 Маржа: +{NetProfitMargin:F0} MDL";
            }
        }

        public decimal RecommendedResellPrice { get; set; }

        private string _aiReasoning = string.Empty;
        public string AiReasoning
        {
            get => _aiReasoning;
            set
            {
                if (_aiReasoning != value)
                {
                    _aiReasoning = value;
                    OnPropertyChanged(nameof(AiReasoning));
                }
            }
        }

        private List<string> _imageUrls = new List<string>();
        public List<string> ImageUrls
        {
            get => _imageUrls;
            set
            {
                if (_imageUrls != value)
                {
                    _imageUrls = value;
                    OnPropertyChanged(nameof(ImageUrls));
                }
            }
        }

        private QuantEvaluationResult? _quantEvaluation;
        public QuantEvaluationResult? QuantEvaluation
        {
            get => _quantEvaluation;
            set
            {
                if (_quantEvaluation != value)
                {
                    _quantEvaluation = value;
                    OnPropertyChanged(nameof(QuantEvaluation));
                    OnPropertyChanged(nameof(QuantBadgeText));
                    OnPropertyChanged(nameof(QuantBadgeColor));
                }
            }
        }

        public string QuantBadgeText => QuantEvaluation?.BadgeText ?? "";
        public string QuantBadgeColor => QuantEvaluation?.BadgeColor ?? "#6B7280";
    }
}
