using Microsoft.AspNetCore.Mvc;

namespace APMSample.Controllers;

[ApiController]
[Route("[controller]")]
public class LogDemoController : ControllerBase
{
    // 注入標準的 ILogger，底層已經被 Serilog 接管了
    private readonly ILogger<LogDemoController> _logger;

    public LogDemoController(ILogger<LogDemoController> logger)
    {
        _logger = logger;
    }

    [HttpGet("checkout")]
    public IActionResult Checkout(int userId, int amount)
    {
        // 1. 【基本用法】
        // 注意：不要用 $"User {userId}"，要用 {UserId}
        // 這樣 Kibana 才能把 UserId 當成一個獨立欄位來搜尋！
        _logger.LogInformation("收到結帳請求，會員ID: {UserId}, 金額: {Amount}", userId, amount);

        try
        {
            if (amount < 0)
            {
                throw new ArgumentException("金額不能為負數");
            }

            // 模擬商業邏輯耗時
            Thread.Sleep(100);

            // 2. 【帶有邏輯的 Log】
            _logger.LogWarning("庫存水位過低，商品ID: {ProductId}, 剩餘庫存: {Stock}", 999, 2);

            return Ok(new { Message = "結帳成功" });
        }
        catch (Exception ex)
        {
            // 3. 【錯誤處理】
            // Serilog 會自動把 Exception 物件解析成漂亮的 JSON 結構
            _logger.LogError(ex, "結帳過程發生例外錯誤！會員ID: {UserId}", userId);
            return BadRequest("系統錯誤");
        }
    }
}