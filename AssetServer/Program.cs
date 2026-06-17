using Microsoft.EntityFrameworkCore;
using AssetServer;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. 配置 EF Core SQLite 数据库上下文
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=assets.db";
builder.Services.AddDbContext<AssetDbContext>(options =>
    options.UseSqlite(connectionString));

// 2. 注册控制器并启用跨域 (CORS) 支持
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

// 3. 【核心自动化】零配置自动初始化 SQLite 数据库与数据种子注入
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AssetDbContext>();
        context.Database.EnsureCreated();

        // 播种种子数据一：默认分组和规则策略
        if (!context.Groups.Any())
        {
            var defaultGroup = new Group
            {
                Name = "默认分组",
                Description = "所有新上报客户端默认归属于此组"
            };
            context.Groups.Add(defaultGroup);
            context.SaveChanges();

            var defaultPolicy = new Policy
            {
                GroupId = defaultGroup.Id,
                CollectHardware = true,
                CollectSoftware = true,
                ScanIntervalMinutes = 120
            };
            context.Policies.Add(defaultPolicy);
            context.SaveChanges();
        }

        // 播种种子数据二：默认管理员账号 (admin / admin123)
        if (!context.Users.Any())
        {
            string password = "admin123";
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            string passwordHash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();

            var adminUser = new User
            {
                Username = "admin",
                PasswordHash = passwordHash,
                Role = "Administrator"
            };
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

// 4. 启用中间件
app.UseCors("AllowAll");
app.UseAuthorization();

// 【新增】为主页绑定一个极简的健康检查接口，彻底消除404，方便排查服务状态
app.MapGet("/", () => new { status = "Online", message = "终端资产管理平台 WebAPI 服务端已成功启动！" });

app.MapControllers();

app.Run();
