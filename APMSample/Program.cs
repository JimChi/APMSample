using Elastic.Apm.SerilogEnricher;
using Elastic.CommonSchema.Serilog;
using Serilog;
using Serilog.Events; // 新增引用，用於 LogEvent
using Serilog.Sinks.Elasticsearch;

// [除錯用] 若 Serilog 內部發生錯誤，顯示在 Console
Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine($"[Serilog Error] {msg}"));

var builder = WebApplication.CreateBuilder(args);
var mName = Environment.MachineName;

// ==========================================
// 1. 設定 Serilog (核心設定)
// ==========================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext() // [絕對關鍵] 必須有這行，Controller 推送的屬性才能被 Filter 讀到
    .Enrich.WithMachineName()
    .Enrich.WithElasticApmCorrelationInfo()
    .WriteTo.Console() // 保留原本功能
    .WriteTo.File(     // 保留原本功能
        formatter: new EcsTextFormatter(),
        path: $@"logs/{mName}log-.json",
        rollingInterval: RollingInterval.Day,
        encoding: System.Text.Encoding.UTF8,
        shared: true)

    // ---------------------------------------------------------------------------------
    // [修改重點] 依據業務情境 (Department, InsuranceType) 分流寫入不同 Index
    // ---------------------------------------------------------------------------------

    // 通道 1: B2C - CAR -> 寫入 ap-logs-b2c-car-yyyy.MM.dd
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => IsMatching(e, "B2C", "CAR"))
        .WriteTo.Elasticsearch(GetSinkOptions(builder.Configuration, "b2c", "car")))

    // 通道 2: MIS - CAR -> 寫入 ap-logs-mis-car-yyyy.MM.dd
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => IsMatching(e, "MIS", "CAR"))
        .WriteTo.Elasticsearch(GetSinkOptions(builder.Configuration, "mis", "car")))

    // 通道 3: B2B - CAR -> 寫入 ap-logs-b2b-car-yyyy.MM.dd
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => IsMatching(e, "B2B", "CAR"))
        .WriteTo.Elasticsearch(GetSinkOptions(builder.Configuration, "b2b", "car")))

    // 通道 4: 預設 (捕捉沒有被分類到的系統 Log，避免 Log 消失)
    .WriteTo.Logger(lc => lc
        .Filter.ByExcluding(e => e.Properties.ContainsKey("Department")) // 排除掉上面已經處理過的
        .WriteTo.Elasticsearch(GetSinkOptions(builder.Configuration, "common", "system")))

    .CreateLogger();

// 接管系統 Log
builder.Host.UseSerilog();

// ==========================================
// 2. 註冊 Elastic APM 服務
// ==========================================
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
// [新增] 輔助方法區 (放在 Program.cs 最下方即可)
// =========================================================================

/// <summary>
/// 產生 Elasticsearch 設定物件，動態組裝 Index 名稱
/// </summary>
static ElasticsearchSinkOptions GetSinkOptions(IConfiguration configuration, string dept, string type)
{
    // 注意：這裡將日期部分改成 {0:yyyy.MM.dd} 讓 Sink 執行時動態替換
    var indexPattern = $"aplogs-{dept.ToLower()}-{type.ToLower()}-{{0:yyyy.MM.dd}}";
    return new ElasticsearchSinkOptions(new Uri(configuration["Elasticsearch:Uri"] ?? "http://localhost:9200"))
    {
        CustomFormatter = new EcsTextFormatter(),
        IndexFormat = indexPattern, // 傳入帶有格式化參數的字串
        AutoRegisterTemplate = true,
        AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7, // 建議明確指定
        NumberOfShards = 1,
        NumberOfReplicas = 0
    };
}

/// <summary>
/// 判斷 Log 是否符合特定的部門與險種
/// </summary>
static bool IsMatching(LogEvent logEvent, string targetDept, string targetType)
{
    // 檢查是否有 Department 屬性且值相符
    var isDeptMatch = logEvent.Properties.TryGetValue("Department", out var deptVal) &&
                      GetValue(deptVal) == targetDept;

    // 檢查是否有 InsuranceType 屬性且值相符
    var isTypeMatch = logEvent.Properties.TryGetValue("InsuranceType", out var typeVal) &&
                      GetValue(typeVal) == targetType;

    return isDeptMatch && isTypeMatch;
}

/// <summary>
/// 簡單取得 LogEventProperty 的字串值 (去除引號)
/// </summary>
static string GetValue(LogEventPropertyValue value)
{
    return value.ToString().Trim('"');
}