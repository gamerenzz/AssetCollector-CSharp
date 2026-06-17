using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using AssetServer;

namespace AssetServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly AssetDbContext _context;

        public AdminController(AssetDbContext context)
        {
            _context = context;
        }

        // 1. 获取所有资产台账 (支持基本搜索 & 穿透式已安装软件精确检索)
        [HttpGet("assets")]
        public async Task<IActionResult> GetAssets([FromQuery] string? search, [FromQuery] string? software)
        {
            var query = _context.Assets.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                string s = search.ToLower();
                query = query.Where(a => 
                    a.Hostname.ToLower().Contains(s) || 
                    a.IpAddress.ToLower().Contains(s) || 
                    a.MacAddress.ToLower().Contains(s) || 
                    a.Department.ToLower().Contains(s) || 
                    a.Building.ToLower().Contains(s)
                );
            }

            if (!string.IsNullOrEmpty(software))
            {
                string s = software.ToLower();
                query = query.Where(a => _context.SoftwareInfos.Any(sw => sw.AssetId == a.MacAddress && sw.Name.ToLower().Contains(s)));
            }

            var assets = await query
                .Include(a => a.Group)
                .Select(a => new
                {
                    a.MacAddress,
                    a.Hostname,
                    a.Username,
                    a.IpAddress,
                    a.OS,
                    a.CPU,
                    a.RAM,
                    a.Disk,
                    a.GPU,
                    a.Monitor,
                    a.Motherboard,
                    a.SystemModel,
                    a.Building,
                    a.Floor,
                    a.Department,
                    a.AssetType,
                    a.CustomFieldsJson,
                    a.Remarks,
                    a.LastReportTime,
                    GroupName = a.Group != null ? a.Group.Name : "未分配",
                    a.GroupId,
                    SoftwareCount = _context.SoftwareInfos.Count(s => s.AssetId == a.MacAddress)
                })
                .OrderByDescending(a => a.LastReportTime)
                .ToListAsync();

            return Ok(assets);
        }

        // 2. 获取单台电脑的详细软件清单
        [HttpGet("assets/{mac}/software")]
        public async Task<IActionResult> GetAssetSoftware(string mac)
        {
            var software = await _context.SoftwareInfos
                .Where(s => s.AssetId == mac)
                .OrderBy(s => s.Name)
                .ToListAsync();

            return Ok(software);
        }

        // 3. 修改设备备注
        [HttpPut("assets/{mac}/remarks")]
        public async Task<IActionResult> UpdateRemarks(string mac, [FromBody] UpdateRemarksRequest req)
        {
            var asset = await _context.Assets.FindAsync(mac);
            if (asset == null) return NotFound("Asset not found");

            string oldRemarks = asset.Remarks;
            asset.Remarks = req.Remarks ?? "";
            
            _context.SystemLogs.Add(new SystemLog
            {
                Level = "Info",
                Message = $"修改设备备注：[{asset.Hostname}]",
                Details = $"原备注: [{oldRemarks}] -> 新备注: [{asset.Remarks}]"
            });

            await _context.SaveChangesAsync();
            return Ok();
        }

        // 4. 修改设备分组
        [HttpPut("assets/{mac}/group")]
        public async Task<IActionResult> UpdateGroup(string mac, [FromBody] UpdateGroupRequest req)
        {
            var asset = await _context.Assets.FindAsync(mac);
            if (asset == null) return NotFound("Asset not found");

            var group = await _context.Groups.FindAsync(req.GroupId);
            if (group == null) return BadRequest("Group not found");

            asset.GroupId = req.GroupId;
            
            _context.SystemLogs.Add(new SystemLog
            {
                Level = "Info",
                Message = $"变更设备分组：[{asset.Hostname}] -> [{group.Name}]",
                Details = $"Device MAC: {mac}"
            });

            await _context.SaveChangesAsync();
            return Ok();
        }

        // 4-2. 批量修改选中设备的分组 
        [HttpPut("assets/batch-group")]
        public async Task<IActionResult> BatchGroup([FromBody] BatchGroupRequest req)
        {
            if (req.Macs == null || req.Macs.Count == 0) return BadRequest("未选中任何设备。");
            
            var group = await _context.Groups.FindAsync(req.GroupId);
            if (group == null) return BadRequest("目标分组不存在。");

            var targetAssets = await _context.Assets.Where(a => req.Macs.Contains(a.MacAddress)).ToListAsync();
            foreach (var a in targetAssets)
            {
                a.GroupId = req.GroupId;
            }

            _context.SystemLogs.Add(new SystemLog
            {
                Level = "Info",
                Message = $"批量变更分组：共 {targetAssets.Count} 台设备归入 [{group.Name}]"
            });

            await _context.SaveChangesAsync();
            return Ok();
        }

        // 5. 获取分组及规则列表
        [HttpGet("groups")]
        public async Task<IActionResult> GetGroups()
        {
            var groups = await _context.Groups.Include(g => g.Policy).ToListAsync();
            return Ok(groups);
        }

        // 6. 新增分组
        [HttpPost("groups")]
        public async Task<IActionResult> CreateGroup([FromBody] Group req)
        {
            if (string.IsNullOrEmpty(req.Name)) return BadRequest("Group name is required.");

            _context.Groups.Add(req);
            await _context.SaveChangesAsync();

            var policy = new Policy { GroupId = req.Id, CollectHardware = true, CollectSoftware = true, ScanIntervalMinutes = 120 };
            _context.Policies.Add(policy);

            _context.SystemLogs.Add(new SystemLog
            {
                Level = "Info",
                Message = $"新建分组成功：[{req.Name}]",
                Details = req.Description
            });

            await _context.SaveChangesAsync();
            return Ok(req);
        }

        // 7. 更新分组规则
        [HttpPut("groups/{id}/policy")]
        public async Task<IActionResult> UpdatePolicy(int id, [FromBody] Policy req)
        {
            var policy = await _context.Policies.FirstOrDefaultAsync(p => p.GroupId == id);
            if (policy == null) return NotFound();

            policy.CollectHardware = req.CollectHardware;
            policy.CollectSoftware = req.CollectSoftware;
            policy.ScanIntervalMinutes = req.ScanIntervalMinutes;

            var group = await _context.Groups.FindAsync(id);
            _context.SystemLogs.Add(new SystemLog
            {
                Level = "Warning",
                Message = $"分组采集策略变更：[{group?.Name}]",
                Details = $"采集硬件: {req.CollectHardware} | 采集软件: {req.CollectSoftware} | 周期: {req.ScanIntervalMinutes}分钟"
            });

            await _context.SaveChangesAsync();
            return Ok();
        }

        // 8. 获取系统安全日志
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs()
        {
            var logs = await _context.SystemLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(200) 
                .ToListAsync();
            return Ok(logs);
        }

        // 【高阶重构】支持根据 target 参数（all, hardware, software）动态裁切工作簿生成
        [HttpGet("export")]
        public async Task<IActionResult> ExportExcel([FromQuery] string? macs, [FromQuery] string? target)
        {
            var queryAssets = _context.Assets.AsQueryable();
            var querySoftware = _context.SoftwareInfos.AsQueryable();

            if (!string.IsNullOrEmpty(macs))
            {
                var macList = macs.Split(',').Select(m => m.Trim()).ToList();
                queryAssets = queryAssets.Where(a => macList.Contains(a.MacAddress));
                querySoftware = querySoftware.Where(s => macList.Contains(s.AssetId));
            }

            var assets = await queryAssets.Include(a => a.Group).ToListAsync();
            var software = await querySoftware.Include(s => s.Asset).ToListAsync();

            string exportType = string.IsNullOrEmpty(target) ? "all" : target.ToLower();

            using (var workbook = new XLWorkbook())
            {
                // 1. 如果需要导出硬件 (all 或 hardware)
                if (exportType == "all" || exportType == "hardware")
                {
                    var ws1 = workbook.Worksheets.Add("全网资产台账");
                    string[] headers1 = { "MAC地址", "计算机名称", "用户名", "IP地址", "所属分组", "操作系统", "处理器", "内存", "磁盘", "显卡", "外接显示器", "主板信息", "整机型号", "楼号", "楼层", "科室", "资产类型", "备注", "最后上报时间" };
                    for (int i = 0; i < headers1.Length; i++)
                    {
                        ws1.Cell(1, i + 1).Value = headers1[i];
                        ws1.Cell(1, i + 1).Style.Font.Bold = true;
                        ws1.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                    }
                    int r = 2;
                    foreach (var a in assets)
                    {
                        ws1.Cell(r, 1).Value = a.MacAddress;
                        ws1.Cell(r, 2).Value = a.Hostname;
                        ws1.Cell(r, 3).Value = a.Username;
                        ws1.Cell(r, 4).Value = a.IpAddress;
                        ws1.Cell(r, 5).Value = a.Group != null ? a.Group.Name : "未分配";
                        ws1.Cell(r, 6).Value = a.OS;
                        ws1.Cell(r, 7).Value = a.CPU;
                        ws1.Cell(r, 8).Value = a.RAM;
                        ws1.Cell(r, 9).Value = a.Disk;
                        ws1.Cell(r, 10).Value = a.GPU;
                        ws1.Cell(r, 11).Value = a.Monitor;
                        ws1.Cell(r, 12).Value = a.Motherboard;
                        ws1.Cell(r, 13).Value = a.SystemModel;
                        ws1.Cell(r, 14).Value = a.Building;
                        ws1.Cell(r, 15).Value = a.Floor;
                        ws1.Cell(r, 16).Value = a.Department;
                        ws1.Cell(r, 17).Value = a.AssetType;
                        ws1.Cell(r, 18).Value = a.Remarks;
                        ws1.Cell(r, 19).Value = a.LastReportTime.ToString("yyyy-MM-dd HH:mm:ss");
                        r++;
                    }
                    ws1.Columns().AdjustToContents();
                }

                // 2. 如果需要导出软件 (all 或 software)
                if (exportType == "all" || exportType == "software")
                {
                    var ws2 = workbook.Worksheets.Add("全网软件清单明细");
                    string[] headers2 = { "归属电脑名", "IP地址", "网卡MAC", "软件名称", "版本号", "安装日期" };
                    for (int i = 0; i < headers2.Length; i++)
                    {
                        ws2.Cell(1, i + 1).Value = headers2[i];
                        ws2.Cell(1, i + 1).Style.Font.Bold = true;
                        ws2.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightSlateGray;
                        ws2.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
                    }
                    int r = 2;
                    foreach (var s in software)
                    {
                        ws2.Cell(r, 1).Value = s.Asset != null ? s.Asset.Hostname : "Unknown";
                        ws2.Cell(r, 2).Value = s.Asset != null ? s.Asset.IpAddress : "Unknown";
                        ws2.Cell(r, 3).Value = s.AssetId;
                        ws2.Cell(r, 4).Value = s.Name;
                        ws2.Cell(r, 5).Value = s.Version;
                        ws2.Cell(r, 6).Value = s.InstallDate;
                        r++;
                    }
                    ws2.Columns().AdjustToContents();
                }

                using (var ms = new MemoryStream())
                {
                    workbook.SaveAs(ms);
                    var fileBytes = ms.ToArray();
                    return new Microsoft.AspNetCore.Mvc.FileContentResult(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
                    {
                        FileDownloadName = $"全网终端资产台账_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                    };
                }
            }
        }

        // ========== 用户与子账户列表管理 ==========
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _context.Users
                .Select(u => new { u.Username, u.Role })
                .ToListAsync();
            return Ok(users);
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
        {
            if (string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Password))
                return BadRequest("Username and Password are required.");

            var exists = await _context.Users.AnyAsync(u => u.Username == req.Username);
            if (exists) return BadRequest("用户已存在！");

            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(req.Password));
            string passwordHash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();

            var newUser = new User { Username = req.Username, PasswordHash = passwordHash, Role = "User" };
            _context.Users.Add(newUser);

            _context.SystemLogs.Add(new SystemLog { Level = "Warning", Message = $"新建后台登录子账号: [{req.Username}]" });
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("users/{username}")]
        public async Task<IActionResult> DeleteUser(string username)
        {
            if (username.ToLower() == "admin") return BadRequest("无法删除系统主管理员账户！");

            var user = await _context.Users.FindAsync(username);
            if (user == null) return NotFound();

            _context.Users.Remove(user);
            _context.SystemLogs.Add(new SystemLog { Level = "Warning", Message = $"删除了登录子账号: [{username}]" });
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("users/password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            if (string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.NewPassword))
                return BadRequest("参数错误");

            var user = await _context.Users.FindAsync(req.Username);
            if (user == null) return NotFound();

            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(req.NewPassword));
            string passwordHash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();

            user.PasswordHash = passwordHash;
            _context.SystemLogs.Add(new SystemLog { Level = "Warning", Message = $"用户 [{req.Username}] 在线修改了登录密码。" });
            await _context.SaveChangesAsync();
            return Ok();
        }

        // ========== 修改监听服务端口并保存至 appsettings.json ==========
        [HttpPost("settings/port")]
        public async Task<IActionResult> UpdatePort([FromBody] PortRequest req)
        {
            if (req.Port < 1024 || req.Port > 65535) return BadRequest("端口范围错误 (1024 ~ 65535)");

            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                
                if (System.IO.File.Exists(path))
                {
                    string json = await System.IO.File.ReadAllTextAsync(path, Encoding.UTF8);
                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                    
                    configDict["ServerPort"] = req.Port.ToString();

                    await System.IO.File.WriteAllTextAsync(path, JsonConvert.SerializeObject(configDict, Formatting.Indented), Encoding.UTF8);

                    _context.SystemLogs.Add(new SystemLog
                    {
                        Level = "Warning",
                        Message = $"修改服务运行端口为: {req.Port}",
                        Details = "写入 appsettings.json 成功。由于需要重新绑定物理网卡监听，请手动重启服务端 exe 以生效。"
                    });
                    await _context.SaveChangesAsync();
                    return Ok();
                }
                return NotFound("appsettings.json 丢失，无法在线更改。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }

    public class UpdateRemarksRequest { public string Remarks { get; set; } = string.Empty; }
    public class UpdateGroupRequest { public int GroupId { get; set; } }
    public class CreateUserRequest { public string Username { get; set; } = string.Empty; public string Password { get; set; } = string.Empty; }
    public class ChangePasswordRequest { public string Username { get; set; } = string.Empty; public string NewPassword { get; set; } = string.Empty; }
    public class PortRequest { public int Port { get; set; } }
    public class BatchGroupRequest { public List<string> Macs { get; set; } = new List<string>(); public int GroupId { get; set; } }
}
