using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AssetDbContext _context;

        public AuthController(AssetDbContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest(new { message = "用户名或密码不能为空" });
            }

            // 计算输入密码的 SHA256 哈希值
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(request.Password));
            string inputHash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();

            // 查询用户
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.PasswordHash == inputHash);
            if (user == null)
            {
                return Unauthorized(new { message = "用户名或密码错误" });
            }

            // 写入审计日志
            _context.SystemLogs.Add(new SystemLog
            {
                Level = "Info",
                Message = $"管理员 [{user.Username}] 成功登录 Web 管理系统。",
                Details = $"Role: {user.Role}"
            });
            await _context.SaveChangesAsync();

            // 返回一个临时的安全令牌 (简单 Token) 供前端校验
            string mockToken = Guid.NewGuid().ToString("N");
            return Ok(new { token = mockToken, username = user.Username, role = user.Role });
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
