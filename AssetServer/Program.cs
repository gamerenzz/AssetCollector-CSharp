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
        
        // 自动迁移/建表 (assets.db 会自动在 exe 旁边产生)
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
            context.SaveChanges(); // 先保存以获取自增 GroupId

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

        // 播种种子数据二：默认管理员账号 (用户名: admin, 默认密码: admin123)
        if (!context.Users.Any())
        {
            // 简单的 SHA256 密码哈希保护
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

            // 写入系统日志
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
app.MapControllers();

// 服务端默认在本地的 5000 端口（HTTP）和 5001 端口（HTTPS）运行
app.Run();
