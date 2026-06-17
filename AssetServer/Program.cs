using Microsoft.EntityFrameworkCore;
using AssetServer;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. 配置 EF Core SQLite 数据库
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=assets.db";
builder.Services.AddDbContext<AssetDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 2. 自动初始化数据库与种子数据
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AssetDbContext>();
        context.Database.EnsureCreated();

        if (!context.Groups.Any())
        {
            var defaultGroup = new Group { Name = "默认分组", Description = "所有新上报客户端默认归属于此组" };
            context.Groups.Add(defaultGroup);
            context.SaveChanges();

            var defaultPolicy = new Policy { GroupId = defaultGroup.Id, CollectHardware = true, CollectSoftware = true, ScanIntervalMinutes = 120 };
            context.Policies.Add(defaultPolicy);
            context.SaveChanges();
        }

        if (!context.Users.Any())
        {
            string password = "admin123";
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            string passwordHash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();

            var adminUser = new User { Username = "admin", PasswordHash = passwordHash, Role = "Administrator" };
            context.Users.Add(adminUser);

            var log = new SystemLog
            {
                Level = "Info",
                Message = "系统初次启动，自动初始化默认数据库、管理员账户与采集策略成功。",
                Details = "Created database assets.db, default administrator user 'admin' and default group successfully."
            };
            context.SystemLogs.Add(log);
            context.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "初始化数据库时发生错误。");
    }
}

// 3. 启用静态网页服务文件流
app.UseDefaultFiles(); 
app.UseStaticFiles();  

app.UseCors("AllowAll");
app.UseAuthorization();

// 【核心修改一】网页端：访问根目录 / 时，自动优雅重定向到 index.html
app.MapGet("/", async context =>
{
    context.Response.Redirect("/index.html");
    await Task.CompletedTask;
});

// 【核心修改二】API 端：将原有的健康检查移动到 /api/health，绝不阻挡网页访问
app.MapGet("/api/health", () => new { status = "Online", message = "终端资产管理平台 WebAPI 服务端正常运行中" });

app.MapControllers();

// 动态读取 appsettings.json 中的端口配置
var customPort = builder.Configuration.GetValue<string>("ServerPort") ?? "5000";

app.Run($"http://*:{customPort}");
