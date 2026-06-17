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
                    .ThenInclude(g => g!.Policy) // 【修正】加上 ! 消除编译器非空断言警告
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

                // 【修正】使用安全空传播符 asset.Group?.Policy 消除警告 CS8602
                if (asset.Group?.Policy != null)
                {
                    collectHw = asset.Group.Policy.CollectHardware;
                    collectSw = asset.Group.Policy.CollectSoftware;
                    interval = asset.Group.Policy.ScanIntervalMinutes;
                }

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
