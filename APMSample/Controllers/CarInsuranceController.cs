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
    private readonly Random _random = new Random();

    public CarInsuranceController(ILogger<CarInsuranceController> logger)
    {
        _logger = logger;
    }

    [HttpPost("auto-test-proposal")]
    public IActionResult AutoTestProposal()
    {
        // 1. 隨機決定情境變數
        var depts = new[] { "B2C", "B2B", "MIS" };
        var insTypes = new[] { "CAR", "HAS", "FIR" }; // 車險, 健康險, 火險

        var currentDept = depts[_random.Next(depts.Length)];
        var currentIns = insTypes[_random.Next(insTypes.Length)];

        // 產生虛擬 Proposal ID
        var proposalId = $"{currentIns[0]}{DateTime.Now:yyMMdd}{_random.Next(1000, 9999)}";

        // 2. 設定 LogContext 範圍
        using (LogContext.PushProperty("ProposalId", proposalId))
        using (LogContext.PushProperty("Department", currentDept))
        using (LogContext.PushProperty("InsuranceType", currentIns))
        {
            // 設定 APM 標籤
            Agent.Tracer.CurrentTransaction?.SetLabel("ProposalId", proposalId);
            Agent.Tracer.CurrentTransaction?.SetLabel("Department", currentDept);
            Agent.Tracer.CurrentTransaction?.SetLabel("InsuranceType", currentIns);

            _logger.LogInformation("【模擬測試開始】部門: {Department}, 險種: {InsuranceType}, 案號: {ProposalId}",
                currentDept, currentIns, proposalId);

            try
            {
                // 3. 隨機產生資料與結果
                // 產生隨機 1~100 的數字來決定命運
                int destiny = _random.Next(1, 101);

                return ExecuteSimulation(destiny, proposalId, currentDept, currentIns);
            }
            catch (BusinessRuleException bex)
            {
                // 情境 400: 業務邏輯錯誤 (Log Warning)
                _logger.LogWarning("核保檢核不過: {Reason}", bex.Message);
                return BadRequest(new
                {
                    Status = 400,
                    Dept = currentDept,
                    Type = currentIns,
                    Error = bex.Message,
                    ProposalId = proposalId
                });
            }
            catch (Exception ex)
            {
                // 情境 500: 系統崩潰 (Log Error)
                _logger.LogError(ex, "核心系統發生預期外錯誤！");
                return StatusCode(500, new
                {
                    Status = 500,
                    Dept = currentDept,
                    Type = currentIns,
                    Error = "Internal Server Error",
                    Detail = ex.Message,
                    ProposalId = proposalId
                });
            }
        }
    }

    /// <summary>
    /// 執行模擬邏輯 (拆分出來比較乾淨)
    /// </summary>
    private IActionResult ExecuteSimulation(int destiny, string proposalId, string dept, string insType)
    {
        // 定義機率區間：
        // 1~30  (30%): 系統錯誤 (Throw Exception)
        // 31~60 (30%): 核保失敗 (Throw BusinessRuleException)
        // 61~100(40%): 成功

        // --- 階段 1: 模擬驗證 (Span) ---
        Agent.Tracer.CurrentTransaction?.CaptureSpan("ValidateRules", "BusinessLogic", () =>
        {
            Thread.Sleep(_random.Next(10, 50)); // 模擬極短運算

            // [情境 A] 30% 機率發生業務規則錯誤 (400) - destiny 31~60
            if (destiny > 30 && destiny <= 60)
            {
                string ruleError = GetRandomRuleError(insType);
                throw new BusinessRuleException(ruleError);
            }
        });

        // --- 階段 2: 模擬計費/核心呼叫 (Span) ---
        decimal premium = 0;
        Agent.Tracer.CurrentTransaction?.CaptureSpan("CoreCalculation", "ExternalSystem", () =>
        {
            _logger.LogInformation("呼叫核心計費系統...");
            Thread.Sleep(_random.Next(100, 300)); // 模擬 API Latency

            // [情境 B] 30% 機率發生系統崩潰 (500) - destiny 1~30
            if (destiny <= 30)
            {
                string sysError = GetRandomSystemError();
                throw new Exception(sysError);
            }

            // 隨機產生保費
            premium = _random.Next(1000, 50000);
        });

        // --- 階段 3: 成功 (200) ---
        // 剩下的 40% 機率成功 (destiny 61~100)
        _logger.LogInformation("承保成功。保費: {Premium}", premium);

        return Ok(new
        {
            Status = 200,
            Message = "投保成功",
            ProposalId = proposalId,
            Department = dept,
            InsuranceType = insType,
            Premium = premium
        });
    }

    // ==========================================
    // 輔助方法：產生隨機錯誤訊息
    // ==========================================

    private string GetRandomRuleError(string insType)
    {
        // 每個險種 10 種情境
        string[] errors = insType switch
        {
            "CAR" => new[] {
                "車齡超過 20 年，不予承保",
                "車主未滿 18 歲，無法作為要保人",
                "此車型屬於高失竊風險車輛，需進行額外審核",
                "車牌號碼格式與監理站資料不符",
                "過去三年內理賠次數過多 (High Claim Ratio)",
                "營業用車輛不可投保此方案",
                "車輛定期檢驗已過期",
                "非法改裝車體未申報",
                "通訊地址位於非服務區",
                "車主身分證字號驗證錯誤"
            },
            "HAS" => new[] {
                "既往症未告知 (糖尿病/高血壓)",
                "BMI 指數過高，超過承保範圍",
                "被保險人年齡超過 65 歲上限",
                "吸菸狀態與體檢報告不符",
                "近三個月內有住院手術紀錄",
                "直系親屬有重大遺傳病史風險",
                "重複投保同類型實支實付醫療險",
                "職業等級屬於第六類高風險職業",
                "年度健康檢查報告未附",
                "每日住院日額已達同業累積上限"
            },
            "FIR" => new[] {
                "建物結構代碼錯誤 (非鋼筋混凝土)",
                "標的物位於地質敏感區 (土壤液化高潛勢)",
                "住宅區違規作為商業用途使用",
                "消防安全設備檢查未通過",
                "屋齡超過 40 年且未進行水電重拉",
                "頂樓加蓋違章建築超過合法比例",
                "鄰近加油站或瓦斯行距離過近",
                "租客使用性質不明確",
                "投保坪數與權狀登記不符",
                "位於公告之易淹水潛勢區域"
            },
            _ => new[] { "未知業務規則錯誤" }
        };

        return errors[_random.Next(errors.Length)];
    }

    private string GetRandomSystemError()
    {
        // 10 種系統錯誤情境
        var errors = new[] {
            "Core System Connection Timeout (504 Gateway Timeout)",
            "Database Deadlock Detected on Table: Policies",
            "External Rating API responded with 502 Bad Gateway",
            "NullReferenceException in CalculationEngine.Validate()",
            "Disk I/O Error: Storage quota exceeded",
            "Memory Overflow: System.OutOfMemoryException",
            "Service Unavailable: Backend is in maintenance mode",
            "Network Packet Loss detected during transmission",
            "Security Token Expired (401 Unauthorized)",
            "Unknown Internal Error code: 0x800401"
        };
        return errors[_random.Next(errors.Length)];
    }
}

// 簡單自訂一個 Exception 用來區分業務錯誤
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}