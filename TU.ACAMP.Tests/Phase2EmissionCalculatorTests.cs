using Xunit;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Services.Activity;

namespace TU.ACAMP.Tests;

public class Phase2EmissionCalculatorTests
{
    [Fact]
    public void CalculateKgCO2e_StandardParameters_ReturnsExactCalculation()
    {
        // Diesel B7 test data: Qty = 10,000, Ratio = 1.0, EF = 2.6335, Share = 50%
        // Expected = 10,000 * 1.0 * 2.6335 * (50 / 100) = 13,167.5000 kgCO2e
        var source = new TbEmissionSource
        {
            Title = "Diesel B7",
            CO2 = 2.6335m,
            gwpCO2 = 1.0m
        };

        var child = new TbActivityTopicChild
        {
            Source = source,
            UnitConversionRatio = 1.0m,
            EmissionSharePercent = 50.0f
        };

        var result = EmissionCalculator.CalculateKgCO2e(10000m, child);

        Assert.Equal(13167.5000m, result);
    }

    [Fact]
    public void CalculateKgCO2e_CustomSourceType_CalculatesWithRealEF()
    {
        // [GHG-03] Custom EF must calculate with actual EF value (e.g. 2.6335) rather than 0
        var source = new TbEmissionSource
        {
            Title = "Proprietary Fuel Blend",
            SourceType = eSourceType.Custom,
            CO2 = 2.6335m,
            gwpCO2 = 1.0m
        };

        var child = new TbActivityTopicChild
        {
            Source = source,
            UnitConversionRatio = 1.0m,
            EmissionSharePercent = 100.0f
        };

        var result = EmissionCalculator.CalculateKgCO2e(10000m, child);

        Assert.Equal(26335.0000m, result);
    }
}
