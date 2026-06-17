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
                // 1. 解析 MAC 地址（作为主键）
                if (!rawPayload.TryGetProperty("MAC地址", out var macProp) || string.IsNullOrEmpty(macProp.GetString()))
                {
                    return BadRequest("Missing required MAC address.");
                }
                string macAddress = macProp.GetString()!;

                // 2. 检查此资产是否已在数据库中存在
                var asset = await _context.Assets
                    .Include(a => a.InstalledSoftware)
                    .FirstOrDefaultAsync(a => a.MacAddress == macAddress);

                bool isNew = false;
                if (asset == null)
                {
                    isNew = true;
                    asset = new Asset { MacAddress = macAddress };
                    
                    // 默认归入第一个分组（默认分组）
                    var defaultGroup = await _context.Groups.FirstOrDefaultAsync();
                    if (defaultGroup != null)
                    {
                        asset.GroupId = defaultGroup.Id;
                    }
                }

                // 3. 提取标准硬件与位置字段，并分流出自定义字段
                var customFields = new Dictionary<string, string>();
                var standardKeys = new HashSet<string> {
                    "server_url", "building", "floor", "department", "type", "report_type", "software_list",
                    "计算机名称", "用户名", "整机型号", "操作系统", "处理器", "内存", "磁盘", "IP地址", "MAC地址", "主板信息", "显卡", "外接显示器"
                };

                foreach (var prop in rawPayload.EnumerateObject())
                {
                    string key = prop.Name;
                    string val = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();

                    // 分流：不属于标准字段的，全都视为自定义动态字段！
                    if (!standardKeys.Contains(key))
                    {
                        customFields[key] = val;
                    }
                }

                // 4. 填充资产标准属性
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

                // 将自定义动态字段转为 JSON 字符串存储
                asset.CustomFieldsJson = JsonSerializer.Serialize(customFields);
                asset.LastReportTime = DateTime.Now;

                if (isNew)
                {
                    _context.Assets.Add(asset);
                }
                else
                {
                    _context.Entry(asset).State = EntityState.Modified;
                }

                // 5. 处理软件清单（如果上报包中包含软件清单）
                if (rawPayload.TryGetProperty("software_list", out var swListProp) && swListProp.ValueKind == JsonValueKind.Array)
                {
                    // 先清空该设备历史存储的软件清单（级联更新，防止垃圾数据）
                    if (!isNew && asset.InstalledSoftware.Count > 0)
                    {
                        _context.SoftwareInfos.RemoveRange(asset.InstalledSoftware);
                    }

                    // 写入新上报的软件明细
                    foreach (var swElement in swListProp.EnumerateArray())
                    {
                        string swName = swElement.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                        string swVer = swElement.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : "";
                        string swDate = swElement.TryGetProperty("InstallDate", out var d) ? d.GetString() ?? "" : "";

                        if (!string.IsNullOrEmpty(swName))
                        {
                            var swInfo = new SoftwareInfo
                            {
                                AssetId = asset.MacAddress,
                                Name = swName,
                                Version = swVer,
                                InstallDate = swDate
                            };
                            _context.SoftwareInfos.Add(swInfo);
                        }
                    }
                }

                // 6. 记入系统日志
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
                // 底层健壮防崩溃
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        private string GetPropVal(JsonElement element, string propName)
        {
            return element.TryGetProperty(propName, out var p) ? p.GetString() ?? "" : "";
        }
    }
}
