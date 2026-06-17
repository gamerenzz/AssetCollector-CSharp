using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly AssetDbContext _context;

        public UploadController(AssetDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] JsonElement rawPayload)
        {
            try
            {
                // 【核心修复】清洗接收到的 MAC 地址，剔除 "Ethernet0:" 前缀，保证与心跳主键绝对对齐！
                string macAddress = "";
                if (rawPayload.TryGetProperty("MAC地址", out var macProp) && !string.IsNullOrEmpty(macProp.GetString()))
                {
                    macAddress = macProp.GetString()!;
                    if (macAddress.Contains("|")) macAddress = macAddress.Split('|')[0].Trim();
                    if (macAddress.Contains(":")) macAddress = macAddress.Split(':')[1].Trim();
                }

                if (string.IsNullOrEmpty(macAddress)) return BadRequest("Missing required MAC address.");

                var asset = await _context.Assets
                    .Include(a => a.InstalledSoftware)
                    .FirstOrDefaultAsync(a => a.MacAddress == macAddress);

                bool isNew = false;
                if (asset == null)
                {
                    isNew = true;
                    asset = new Asset { MacAddress = macAddress };
                    
                    var defaultGroup = await _context.Groups.FirstOrDefaultAsync();
                    if (defaultGroup != null) asset.GroupId = defaultGroup.Id;
                }

                var customFields = new Dictionary<string, string>();
                var standardKeys = new HashSet<string> {
                    "server_url", "building", "floor", "department", "type", "report_type", "software_list",
                    "计算机名称", "用户名", "整机型号", "操作系统", "处理器", "内存", "磁盘", "IP地址", "MAC地址", "主板信息", "显卡", "外接显示器"
                };

                foreach (var prop in rawPayload.EnumerateObject())
                {
                    string key = prop.Name;
                    string val = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
                    if (!standardKeys.Contains(key)) customFields[key] = val;
                }

                asset.Hostname = GetPropVal(rawPayload, "计算机名称");
                asset.Username = GetPropVal(rawPayload, "用户名");
                asset.OS = GetPropVal(rawPayload, "操作系统");
                asset.CPU = GetPropVal(rawPayload, "处理器");
                asset.RAM = GetPropVal(rawPayload, "内存");
                asset.Disk = GetPropVal(rawPayload, "磁盘");
                asset.IpAddress = GetPropVal(rawPayload, "IP地址");
                asset.Motherboard = GetPropVal(rawPayload, "主板信息");
                asset.GPU = GetPropVal(rawPayload, "显卡");
                asset.Monitor = GetPropVal(rawPayload, "外接显示器");
                asset.SystemModel = GetPropVal(rawPayload, "整机型号");

                asset.Building = GetPropVal(rawPayload, "building");
                asset.Floor = GetPropVal(rawPayload, "floor");
                asset.Department = GetPropVal(rawPayload, "department");
                asset.AssetType = GetPropVal(rawPayload, "type");
                asset.CustomFieldsJson = JsonSerializer.Serialize(customFields);
                asset.LastReportTime = DateTime.Now;

                if (isNew) _context.Assets.Add(asset);
                else _context.Entry(asset).State = EntityState.Modified;

                if (rawPayload.TryGetProperty("software_list", out var swListProp) && swListProp.ValueKind == JsonValueKind.Array)
                {
                    if (!isNew && asset.InstalledSoftware.Count > 0)
                    {
                        _context.SoftwareInfos.RemoveRange(asset.InstalledSoftware);
                    }

                    foreach (var swElement in swListProp.EnumerateArray())
                    {
                        string swName = swElement.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                        string swVer = swElement.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : "";
                        string swDate = swElement.TryGetProperty("InstallDate", out var d) ? d.GetString() ?? "" : "";

                        if (!string.IsNullOrEmpty(swName))
                        {
                            _context.SoftwareInfos.Add(new SoftwareInfo { AssetId = asset.MacAddress, Name = swName, Version = swVer, InstallDate = swDate });
                        }
                    }
                }

                var auditLog = new SystemLog
                {
                    Level = "Info",
                    Message = $"资产上报成功：主机名 [{asset.Hostname}]，IP [{asset.IpAddress}]",
                    Details = $"MAC [{asset.MacAddress}] " + (isNew ? "【注册新设备】" : "【更新已有设备】")
                };
                _context.SystemLogs.Add(auditLog);

                await _context.SaveChangesAsync();

                return Ok(new { asset_id = asset.MacAddress, status = "Success" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        private string GetPropVal(JsonElement element, string propName)
        {
            return element.TryGetProperty(propName, out var p) ? p.GetString() ?? "" : "";
        }
    }
}
