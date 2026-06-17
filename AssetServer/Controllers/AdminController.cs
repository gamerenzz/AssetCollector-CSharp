using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;

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

        // 1. 获取所有资产台账 (含分组名称和软件统计数量)
        [HttpGet("assets")]
        public async Task<IActionResult> GetAssets()
        {
            var assets = await _context.Assets
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

        // 3. 修改设备备注 (需求 3)
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

        // 4. 修改设备分组 (需求 2)
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

        // 5. 获取分组及规则列表 (需求 4)
        [HttpGet("groups")]
        public async Task<IActionResult> GetGroups()
        {
            var groups = await _context.Groups.Include(g => g.Policy).ToListAsync();
            return Ok(groups);
        }

        // 6. 新增分组 (需求 2)
        [HttpPost("groups")]
        public async Task<IActionResult> CreateGroup([FromBody] Group req)
        {
            if (string.IsNullOrEmpty(req.Name)) return BadRequest("Group name is required.");

            _context.Groups.Add(req);
            await _context.SaveChangesAsync();

            // 为新分组自动生成一个默认规则策略
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

        // 7. 更新分组规则 (需求 4)
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

        // 8. 获取系统安全日志 (需求 7)
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs()
        {
            var logs = await _context.SystemLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(200) // 取最新的 200 条
                .ToListAsync();
            return Ok(logs);
        }

        // 9. 【双 Sheet 汇总导出】一键打包全网硬件台账与所有设备软件明细 (需求 6)
        [HttpGet("export")]
        public async Task<IActionResult> ExportExcel()
        {
            var assets = await _context.Assets.Include(a => a.Group).ToListAsync();
            var software = await _context.SoftwareInfos.Include(s => s.Asset).ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                // Tab 页 1：全网资产台账汇总
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

                // Tab 页 2：全网软件分布汇总表
                var ws2 = workbook.Worksheets.Add("全网软件清单明细");
                string[] headers2 = { "归属电脑名", "IP地址", "网卡MAC", "软件名称", "版本号", "安装日期" };
                for (int i = 0; i < headers2.Length; i++)
                {
                    ws2.Cell(1, i + 1).Value = headers2[i];
                    ws2.Cell(1, i + 1).Style.Font.Bold = true;
                    ws2.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightSlateGray;
                    ws2.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
                }
                r = 2;
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

                using (var ms = new MemoryStream())
                {
                    workbook.SaveAs(ms);
                    var fileBytes = ms.ToArray();
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"全网终端资产台账_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
        }
    }

    public class UpdateRemarksRequest { public string Remarks { get; set; } = string.Empty; }
    public class UpdateGroupRequest { public int GroupId { get; set; } }
}
