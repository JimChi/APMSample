using Elastic.Apm.SerilogEnricher;
using Elastic.CommonSchema.Serilog;
using Serilog;
using Serilog.Sinks.Elasticsearch;
using System.Reflection;
// 【關鍵除錯】將 Serilog 的錯誤輸出到 Visual Studio 的 "輸出" 視窗 (Console)
Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine($"[Serilog Error] {msg}"));
// ... 下面接原本的程式碼
var builder = WebApplication.CreateBuilder(args);
// ==========================================
// 1. 設定 Serilog (Log 的核心設定)
// ==========================================
Log.Logger = new LoggerConfiguration()
    // 1. 設定預設層級 (通常是 Information)
    .MinimumLevel.Information()

    // ================================================================
    // 【強力過濾區】 針對你截圖中的那些雜訊來源直接封鎖！
    // ================================================================
    // 1. 封鎖 Elasticsearch 傳輸層 (原本的保留)
    .MinimumLevel.Override("Elastic.Transport", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Elastic.Apm", Serilog.Events.LogEventLevel.Warning)

    // 2. 【新增】直接封鎖截圖中出現的那些討厭鬼 (精準打擊)
    .MinimumLevel.Override("RequestPipelineDiagnosticsListener", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("HttpConnectionDiagnosticsListener", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("SerializerDiagnosticsListener", Serilog.Events.LogEventLevel.Warning)

    // 3. 封鎖 HTTP Client (原本的保留)
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    // ================================================================
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    // [關鍵] 這一行讓 Log 自動帶上 APM 的 TraceId
    .Enrich.WithElasticApmCorrelationInfo()
    .WriteTo.Console() // 寫一份到黑色視窗
    .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri("http://localhost:9200"))
    {
        // 設定 Index 名稱格式，例如: log-mydotnet8app-2024.01.06
        IndexFormat = $"ap-logs-{Assembly.GetExecutingAssembly().GetName().Name.ToLower().Replace(".", "-")}-{DateTime.UtcNow:yyyy.MM.dd}",
        AutoRegisterTemplate = true, // 自動建立欄位對應
        CustomFormatter = new EcsTextFormatter(),
        NumberOfShards = 1,
        NumberOfReplicas = 0
    })
    .CreateLogger();

// 讓 .NET 使用 Serilog 取代預設 Logger
builder.Host.UseSerilog();
// ==========================================
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// [修正點] 改用 AddAllElasticApm
builder.Services.AddAllElasticApm();
var app = builder.Build();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
