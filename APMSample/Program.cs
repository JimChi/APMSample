using Elastic.Apm.SerilogEnricher;
using Elastic.CommonSchema.Serilog;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;

// [除錯用]
Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine($"[Serilog Error] {msg}"));

var builder = WebApplication.CreateBuilder(args);
var mName = Environment.MachineName;

// ==========================================
// 定義您的業務維度 (修改這裡即可擴充)
// ==========================================
var departments = new[] { "B2C", "B2B", "MIS" };
var insuranceTypes = new[] { "CAR", "HAS", "FIR" }; // CAR:車險, HAS:健康險, FIR:火險

// ==========================================
// 1. 設定 Serilog (核心設定)
// ==========================================
var loggerConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext() // 關鍵：讀取 Controller 推送的屬性
    .Enrich.WithMachineName()
    .Enrich.WithElasticApmCorrelationInfo()
    .WriteTo.Console()
    .WriteTo.File(
        formatter: new EcsTextFormatter(),
        path: $@"logs/{mName}log-.json",
        rollingInterval: RollingInterval.Day,
        encoding: System.Text.Encoding.UTF8,
        shared: true);

// ==========================================
// [修改重點] 使用迴圈動態產生分流規則
// ==========================================
// 組合：3 Depts * 3 Types = 9 個 Elastic Index 規則
foreach (var dept in departments)
{
    foreach (var insType in insuranceTypes)
    {
        loggerConfig.WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(e => IsMatching(e, dept, insType))
            .WriteTo.Elasticsearch(GetSinkOptions(builder.Configuration, dept, insType)));
    }
}

// 通道 4: 捕捉漏網之魚 (沒有標記 Department 的 Log)
loggerConfig.WriteTo.Logger(lc => lc
    .Filter.ByExcluding(e => e.Properties.ContainsKey("Department"))
    .WriteTo.Elasticsearch(GetSinkOptions(builder.Configuration, "common", "system")));

// 建立 Logger
Log.Logger = loggerConfig.CreateLogger();

// 接管系統 Log
builder.Host.UseSerilog();

// 2. 註冊 Elastic APM
builder.Services.AddAllElasticApm();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// =========================================================================
// 輔助方法區
// =========================================================================

static ElasticsearchSinkOptions GetSinkOptions(IConfiguration configuration, string dept, string type)
{
    // Index 名稱格式：aplogs-{部門}-{險種}-{日期}
    // 例如: aplogs-b2c-fir-2026.01.08
    var indexPattern = $"aplogs-{dept.ToLower()}-{type.ToLower()}-{{0:yyyy.MM.dd}}";

    return new ElasticsearchSinkOptions(new Uri(configuration["Elasticsearch:Uri"] ?? "http://localhost:9200"))
    {
        CustomFormatter = new EcsTextFormatter(),
        IndexFormat = indexPattern,
        AutoRegisterTemplate = true,
        AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
        NumberOfShards = 1,
        NumberOfReplicas = 0
    };
}

static bool IsMatching(LogEvent logEvent, string targetDept, string targetType)
{
    var isDeptMatch = logEvent.Properties.TryGetValue("Department", out var deptVal) &&
                      GetValue(deptVal) == targetDept;

    var isTypeMatch = logEvent.Properties.TryGetValue("InsuranceType", out var typeVal) &&
                      GetValue(typeVal) == targetType;

    return isDeptMatch && isTypeMatch;
}

static string GetValue(LogEventPropertyValue value)
{
    return value.ToString().Trim('"');
}