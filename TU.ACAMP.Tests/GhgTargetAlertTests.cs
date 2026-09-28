// [2026-09-25][Antigravity] Unit tests สำหรับระบบแจ้งเตือนทางอีเมลเมื่อค่า GHG สูงขึ้นเข้าใกล้เป้าหมายการลดประจำปี (>80%)
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.WebApp.Services.Identity;

namespace TU.ACAMP.Tests;

public class GhgTargetAlertTests
{
    [Fact]
    public void LinearTrajectoryCalculation_CalculatesAnnualCapAndConsumptionRatioAccurately()
    {
        // Arrange: Base Year 2023 with 1,000 tCO2e, Target Year 2030 with 50% reduction
        int baseYear = 2023;
        int targetYear = 2030;
        decimal targetReductionPercent = 50.0m;
        decimal baseYearEmissionsTon = 1000.0m;
        int currentYear = 2026;

        // Act
        decimal elapsedYears = Math.Max(0, currentYear - baseYear);
        decimal totalTargetYears = Math.Max(1, targetYear - baseYear);
        decimal targetRatioProgress = Math.Min(1.0m, elapsedYears / totalTargetYears);
        decimal currentTargetReductionRatio = 1.0m - ((targetReductionPercent / 100.0m) * targetRatioProgress);
        decimal annualTargetCapTon = baseYearEmissionsTon * currentTargetReductionRatio;

        // Current emissions at 650 tCO2e
        decimal currentEmissionsTon = 650.0m;
        decimal consumptionRatio = currentEmissionsTon / annualTargetCapTon;
        decimal consumptionPercent = consumptionRatio * 100.0m;

        // Assert: 3/7 progress -> reduction ratio ~ 0.785714 -> cap ~ 785.71 tCO2e
        // 650 / 785.7142 ~ 82.727%
        Assert.True(annualTargetCapTon > 785.0m && annualTargetCapTon < 786.0m);
        Assert.True(consumptionPercent >= 80.0m, "Consumption should exceed 80% threshold.");
        Assert.True(consumptionPercent < 100.0m, "Consumption should be below 100% cap.");
    }

    [Theory]
    [InlineData(600.0, 785.714, false, "None")]        // ~76.3% -> No alert
    [InlineData(630.0, 785.714, true, "Warning_80")]    // ~80.18% -> Warning_80
    [InlineData(785.0, 785.714, true, "Warning_80")]    // ~99.9% -> Warning_80
    [InlineData(800.0, 785.714, true, "Exceeded_100")]  // ~101.8% -> Exceeded_100
    public void AlertThresholdEvaluation_ClassifiesTiersAccurately(
        double currentEmissions, double cap, bool shouldAlert, string expectedTier)
    {
        decimal currentTon = (decimal)currentEmissions;
        decimal capTon = (decimal)cap;
        decimal thresholdPercent = 80.0m;

        decimal consumptionRatio = currentTon / capTon;
        decimal consumptionPercent = consumptionRatio * 100.0m;

        bool isAlert = consumptionPercent >= thresholdPercent;
        string tier = consumptionPercent >= 100.0m ? "Exceeded_100" : (consumptionPercent >= thresholdPercent ? "Warning_80" : "None");

        Assert.Equal(shouldAlert, isAlert);
        Assert.Equal(expectedTier, tier);
    }

    [Fact]
    public void RecipientEmails_SplitsAndCleansMultipleEmailDelimiters()
    {
        // Test parsing comma, semicolon, space delimited recipient lists
        string rawEmails = "admin@example.com; sustainability@company.com, esg-team@acamp.or.th  invalid-email ";

        var list = rawEmails.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.Contains('@') && e.Contains('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(3, list.Count);
        Assert.Contains("admin@example.com", list);
        Assert.Contains("sustainability@company.com", list);
        Assert.Contains("esg-team@acamp.or.th", list);
        Assert.DoesNotContain("invalid-email", list);
    }

    [Fact]
    public void EmailModel_Properties_MaintainDataIntegrity()
    {
        var model = new EmailSender.GhgTargetAlertEmailModel
        {
            OrganizationName = "Bang Sue Chia Meng Rice Mill Co., Ltd.",
            ReportingYear = 2026,
            BaseYear = 2023,
            BaseYearEmissionsTon = 1200.0m,
            AnnualTargetCapTon = 1000.0m,
            CurrentEmissionsTon = 850.0m,
            ConsumptionPercent = 85.0m,
            Scope1Ton = 350.0m,
            Scope2Ton = 400.0m,
            Scope3Ton = 100.0m,
            AlertTier = "Warning_80",
            DashboardUrl = "https://carbon-one.acamp.local/dashboard"
        };

        Assert.Equal("Warning_80", model.AlertTier);
        Assert.Equal(85.0m, model.ConsumptionPercent);
        Assert.Equal(850.0m, model.Scope1Ton + model.Scope2Ton + model.Scope3Ton);
    }
}
