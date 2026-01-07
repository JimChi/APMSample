using Elastic.Apm.NetCoreAll;
using Elastic.Apm.SerilogEnricher;
using Elastic.CommonSchema.Serilog;
using Serilog;
using Serilog.Sinks.Elasticsearch;
using System.Reflection;

// [除錯用] 若 Serilog 寫入失敗，錯誤會顯示在 Console (建議保留)
Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine($"[Serilog Error] {msg}"));

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. 設定 Serilog (核心設定)
// ==========================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration) // [優化] 將過濾規則移至 appsettings.json 管理
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithElasticApmCorrelationInfo() // [關鍵] 自動注入 APM TraceId，實現 Log 與 APM 的串接
    .WriteTo.Console()
    .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(builder.Configuration["Elasticsearch:Uri"] ?? "http://localhost:9200"))
    {
        // [關鍵] 使用 ECS 格式，確保 Kibana 能正確解析欄位 (trace.id, transaction.id 等)
        CustomFormatter = new EcsTextFormatter(),

        // Index 名稱格式 (保留你的設定)
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