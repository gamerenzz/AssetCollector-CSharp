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

                var asset = await _context.Assets
                    .Include(a => a.Group)
                    .ThenInclude(g => g!.Policy) 
                    .FirstOrDefaultAsync(a => a.MacAddress == req.MacAddress);

                if (asset == null)
                {
                    return NotFound("Asset not registered yet.");
                }

                asset.LastReportTime = DateTime.Now;
                _context.Entry(asset).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                bool collectHw = true;
                bool collectSw = true;
                int interval = 120; 
                int policyVersion = 1; // 默认版本号 1

                if (asset.Group?.Policy != null)
                {
                    collectHw = asset.Group.Policy.CollectHardware;
                    collectSw = asset.Group.Policy.CollectSoftware;
                    interval = asset.Group.Policy.ScanIntervalMinutes;
                    policyVersion = asset.Group.Policy.Version; // 【核心新增】获取服务器最新的策略版本
                }

                return Ok(new
                {
                    collect_hardware = collectHw,
                    collect_software = collectSw,
                    scan_interval_minutes = interval,
                    policy_version = policyVersion // 【核心新增】将版本号一并下发
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
