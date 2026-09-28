using System;
using TU.ACAMP.Domain.Models.Library;

namespace TU.ACAMP.WebApp.Services.Activity;

public static class EmissionCalculator
{
    #region class methods
    /// <summary>
    /// สูตรมาตรฐานเดียวของทั้งระบบ: Quantity × UnitConversionRatio × EF × (EmissionSharePercent / 100)
    ///
    /// [GHG-15] กติกา: ห้ามเขียน "* Source.EF" เพื่อคำนวณยอดปล่อยที่ไฟล์อื่นเด็ดขาด
    ///          การใช้ Source.EF ที่อื่นได้เฉพาะเพื่อ "แสดงผลค่า EF" เท่านั้น
    ///          ทุกยอด kgCO2e ต้องผ่านเมธอดในคลาสนี้ เพื่อให้ทุกหน้าจอและทุกรายงานตรงกัน
    /// </summary>
    public static decimal CalculateKgCO2e(decimal quantity, TbActivityTopicChild? child)
    {
        #region hide [2026-08-22]
        if (child is null || !child.IsActive || child.IsDeleted || child.Source is null) return 0m;

        var ratio = child.UnitConversionRatio ?? 1m;
        var ef = child.Source.EF;
        var share = (decimal)child.EmissionSharePercent / 100m;

        return quantity * ratio * ef * share;
        #endregion
    }

    /// <summary>
    /// คำนวณปริมาณก๊าซเรือนกระจก (kgCO2e) ของ Child Source หนึ่งรายการตามปริมาณกิจกรรมที่บันทึก (float overload)
    /// </summary>
    public static decimal CalculateKgCO2e(float quantity, TbActivityTopicChild? child)
    {
        #region hide [2026-08-22]
        return CalculateKgCO2e(Convert.ToDecimal(quantity), child);
        #endregion
    }
    #endregion
}
