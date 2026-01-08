using APMSample.Models;
using Elastic.Apm; // 引用 APM API 用來畫 Span
using Microsoft.AspNetCore.Mvc;
using Serilog.Context; // 引用 LogContext

namespace APMSample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CarInsuranceController : ControllerBase
{
    private readonly ILogger<CarInsuranceController> _logger;

    public CarInsuranceController(ILogger<CarInsuranceController> logger)
    {
        _logger = logger;
    }

    [HttpPost("submit-proposal")]
    public IActionResult SubmitProposal([FromBody] CarInsuranceRequest request)
    {
        // 1. 產生符合保險業規則的要保書號碼
        var proposalId = $"13C{DateTime.Now:yyMMdd}{Random.Shared.Next(10000, 99999)}";

        // ========================================================================
        // [新增] 模擬隨機業務情境 (用於測試 Kibana 權限分流)
        // ========================================================================
        var depts = new[] { "B2C", "B2B", "MIS" };
        var currentDept = depts[Random.Shared.Next(depts.Length)]; // 隨機挑選部門
        var currentIns = "CAR"; // 固定險種

        // ========================================================================
        // 【關鍵技術】使用 LogContext 建立「Log 範圍」
        // 利用巢狀 using 同時注入多個屬性，確保 Log 包含路由所需資訊
        // ========================================================================
        using (LogContext.PushProperty("ProposalId", proposalId))
        using (LogContext.PushProperty("Department", currentDept))   // [新增] 注入部門
        using (LogContext.PushProperty("InsuranceType", currentIns)) // [新增] 注入險種
        {
            _logger.LogInformation("收到投保請求，開始處理。部門: {Department}, 險種: {InsuranceType}, 車牌: {Plate}",
                currentDept, currentIns, request.CarPlate);

            try
            {
                // [新增] 讓 APM 也紀錄這些標籤，方便在效能圖表中篩選
                Agent.Tracer.CurrentTransaction?.SetLabel("ProposalId", proposalId);
                Agent.Tracer.CurrentTransaction?.SetLabel("Department", currentDept);
                Agent.Tracer.CurrentTransaction?.SetLabel("InsuranceType", currentIns);

                // --- 步驟 1: 驗證規則 (模擬耗時) ---
                Agent.Tracer.CurrentTransaction?.CaptureSpan("ValidateBusinessRules", "BusinessLogic", () =>
                {
                    _logger.LogInformation("步驟 1/3: 進行核保規則檢核...");
                    Thread.Sleep(50); // 模擬運算

                    // 模擬情境 A: 拒保 (Log Warning)
                    if (request.CarAge > 20)
                    {
                        throw new BusinessRuleException("車齡超過 20 年，不予承保");
                    }
                });

                // --- 步驟 2: 計算保費 (模擬耗時) ---
                decimal premium = 0;
                Agent.Tracer.CurrentTransaction?.CaptureSpan("CalculatePremium", "Calculation", () =>
                {
                    _logger.LogInformation("步驟 2/3: 計算保費費率...");
                    Thread.Sleep(150); // 模擬呼叫精算引擎

                    // 簡單邏輯
                    premium = request.CoverageType == "甲式" ? 50000 : 12000;

                    // 模擬情境 B: 系統爆掉 (Log Error)
                    if (request.CarPlate.StartsWith("ERR"))
                    {
                        throw new Exception("連線精算核心系統逾時 (Connection Timeout)");
                    }
                });

                // --- 步驟 3: 寫入資料庫/完成投保 ---
                Agent.Tracer.CurrentTransaction?.CaptureSpan("SaveToCoreSystem", "Db", () =>
                {
                    _logger.LogInformation("步驟 3/3: 寫入核心系統...");
                    Thread.Sleep(100);
                });

                // 成功 Log
                _logger.LogInformation("投保成功！要保號: {ProposalId}, 保費: {Premium}", proposalId, premium);

                return Ok(new
                {
                    ProposalId = proposalId,
                    Department = currentDept, // 回傳部門方便您確認結果
                    Message = "投保受理成功",
                    Premium = premium
                });
            }
            catch (BusinessRuleException bex)
            {
                _logger.LogWarning("投保檢核失敗: {Reason}", bex.Message);
                return BadRequest(new { ProposalId = proposalId, Department = currentDept, Error = bex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "投保過程發生系統例外！要保號: {ProposalId}", proposalId);
                return StatusCode(500, new { ProposalId = proposalId, Department = currentDept, Error = "系統忙碌中，請稍後再試" });
            }
        }
    }
}

// 簡單自訂一個 Exception 用來區分業務錯誤
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}