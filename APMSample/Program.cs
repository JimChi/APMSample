using Elastic.Apm.SerilogEnricher;
using Elastic.CommonSchema.Serilog;
using Serilog;
using Serilog.Sinks.Elasticsearch;
using System.Reflection;

// [除錯用] 若 Serilog 內部發生錯誤（如寫入檔案失敗），錯誤會顯示在 Console
Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine($"[Serilog Error] {msg}"));

var builder = WebApplication.CreateBuilder(args);
var mName = Environment.MachineName;
// ==========================================
// 1. 設定 Serilog (核心設定)
// ==========================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration) // 讀取 appsettings.json 中的過濾規則
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithElasticApmCorrelationInfo() // [關鍵] 自動注入 APM TraceId，實現 Log 與 APM 的串接
    .WriteTo.Console() // 保留 Console 輸出，方便開發時除錯
    .WriteTo.File(
        formatter: new EcsTextFormatter(),     // [關鍵] 產出符合 Elastic Common Schema 的 JSON
        path: $@"logs/{mName}log-.json",       // 檔案路徑，Serilog 會自動在 - 後面補上日期
        rollingInterval: RollingInterval.Day,  // 設定以「天」為單位切割檔案
        encoding: System.Text.Encoding.UTF8,   // 明確指定 UTF8 避免亂碼
        shared: true)                          // [關鍵] 允許 Filebeat 或其他程序同時讀取此檔案 (避免鎖死)
     // ---------------------------------------------------------------------------------
     // [暫存] 原本的 Elasticsearch 設定 (目前無 ELK 環境，先註解保留)
     // ---------------------------------------------------------------------------------
     .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(builder.Configuration["Elasticsearch:Uri"] ?? "http://localhost:9200"))
     {
         // 使用 ECS 格式，確保 Kibana 能正確解析欄位
         CustomFormatter = new EcsTextFormatter(),

         // Index 名稱格式
         IndexFormat = $"ap-logs-{Assembly.GetExecutingAssembly().GetName().Name!.ToLower().Replace(".", "-")}-{DateTime.UtcNow:yyyy.MM.dd}",
         AutoRegisterTemplate = true,
         NumberOfShards = 1,
         NumberOfReplicas = 0
     })
    .CreateLogger();

// 接管系統 Log
builder.Host.UseSerilog();

// ==========================================
// 2. 註冊 Elastic APM 服務
// ==========================================
// 這行會自動讀取 appsettings 的 ElasticApm 設定，並注入監控服務
builder.Services.AddAllElasticApm();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 這裡不需要額外呼叫 UseAllElasticApm，AddAllElasticApm 已自動處理 Middleware 注入

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();