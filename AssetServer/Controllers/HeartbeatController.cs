using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HeartbeatController : ControllerBase
    {
        private readonly AssetDbContext _context;

        public HeartbeatController(AssetDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] HeartbeatRequest req)
        {
            try
            {
                if (string.IsNullOrEmpty(req.MacAddress))
                {
                    return BadRequest("Missing MAC address.");
                }

                // 查找该资产及其所属分组和策略
                var asset = await _context.Assets
                    .Include(a => a.Group)
                    .ThenInclude(g => g.Policy)
                    .FirstOrDefaultAsync(a => a.MacAddress == req.MacAddress);

                if (asset == null)
                {
                    return NotFound("Asset not registered yet.");
                }

                // 1. 更新设备最后在线时间 (需求 5: 标识在线状态)
                asset.LastReportTime = DateTime.Now;
                _context.Entry(asset).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                // 2. 提取并准备下发给该设备的策略规则 (需求 4)
                bool collectHw = true;
                bool collectSw = true;
                int interval = 120; // 默认 120 分钟上报一次

                if (asset.Group != null && asset.Group.Policy != null)
                {
                    collectHw = asset.Group.Policy.CollectHardware;
                    collectSw = asset.Group.Policy.CollectSoftware;
                    interval = asset.Group.Policy.ScanIntervalMinutes;
                }

                // 返回策略包给客户端
                return Ok(new
                {
                    collect_hardware = collectHw,
                    collect_software = collectSw,
                    scan_interval_minutes = interval
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }

    public class HeartbeatRequest
    {
        public string MacAddress { get; set; } = string.Empty;
    }
}
