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

// 3. 启用静态网页服务文件流（支持 wwwroot）
app.UseDefaultFiles(); 
app.UseStaticFiles();  

app.UseCors("AllowAll");
app.UseAuthorization();

// 为主页绑定一个健康检查接口
app.MapGet("/health", () => new { status = "Online", message = "终端资产管理平台 WebAPI 服务端正常运行中" });

app.MapControllers();

// 【核心修改】
// 默认 app.Run() 仅绑定 localhost，会导致局域网 IP 访问被拒绝拒连。
// 改为绑定 "http://*:5000" 即可完美向全网、局域网所有网卡 IP 开放服务！
app.Run("http://*:5000");
