using Xunit;
using SmartphoneMonitor.Services;

namespace SmartphoneMonitor.Tests
{
    public class ModelPriceBaselineTests
    {
        [Fact]
        public void Redmi9_ReturnsRealisticBaselinePrice()
        {
            var baseline = ModelPriceBaselineService.GetBaseline("Xiaomi", "Redmi 9", 64);
            Assert.NotNull(baseline);
            Assert.True(baseline!.BaselinePrice <= 1500m, $"Expected Redmi 9 baseline <= 1500 MDL, got {baseline.BaselinePrice}");
            Assert.True(baseline.IsLegacyBudget);
        }

        [Fact]
        public void GalaxyA12_ReturnsRealisticBaselinePrice()
        {
            var baseline = ModelPriceBaselineService.GetBaseline("Samsung", "Galaxy A12", 64);
            Assert.NotNull(baseline);
            Assert.True(baseline!.BaselinePrice <= 1500m, $"Expected Galaxy A12 baseline <= 1500 MDL, got {baseline.BaselinePrice}");
            Assert.True(baseline.IsLegacyBudget);
        }
    }
}
