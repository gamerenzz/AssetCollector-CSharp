using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AssetServer
{
    // 1. 资产台账表
    public class Asset
    {
        [Key]
        public string MacAddress { get; set; } = string.Empty; // 以 MAC 地址为主键（唯一标识）
        
        public string Hostname { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string OS { get; set; } = string.Empty;
        public string CPU { get; set; } = string.Empty;
        public string RAM { get; set; } = string.Empty;
        public string Disk { get; set; } = string.Empty;
        public string GPU { get; set; } = string.Empty;
        public string Motherboard { get; set; } = string.Empty;
        public string Monitor { get; set; } = string.Empty;
        public string SystemModel { get; set; } = string.Empty;

        // 位置信息
        public string Building { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;

        // 动态自定义字段 (存储为 JSON 字符串)
        public string CustomFieldsJson { get; set; } = "{}";

        // 管理员在后台手动设置的备注 (需求 3)
        public string Remarks { get; set; } = string.Empty;

        // 分组关联 (需求 2, nullable)
        public int? GroupId { get; set; }
        [ForeignKey("GroupId")]
        public Group? Group { get; set; }

        public DateTime LastReportTime { get; set; } = DateTime.Now;

        // 一对多关联：一台设备拥有多个已安装软件
        public ICollection<SoftwareInfo> InstalledSoftware { get; set; } = new List<SoftwareInfo>();
    }

    // 2. 已安装软件明细表
    public class SoftwareInfo
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string AssetId { get; set; } = string.Empty; // 关联的资产 MAC 地址
        [ForeignKey("AssetId")]
        public Asset? Asset { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string InstallDate { get; set; } = string.Empty;
    }

    // 3. 分组表 (需求 2)
    public class Group
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // 一个分组对应一条上报规则 (需求 4)
        public Policy? Policy { get; set; }
    }

    // 4. 采集规则/策略表 (需求 4)
    public class Policy
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int GroupId { get; set; }
        [ForeignKey("GroupId")]
        public Group? Group { get; set; }

        public bool CollectHardware { get; set; } = true;  // 是否要求采集硬件
        public bool CollectSoftware { get; set; } = true;  // 是否要求采集软件
        public int ScanIntervalMinutes { get; set; } = 120; // 默认心跳/扫描间隔
    }

    // 5. 安全审计/系统日志表 (需求 7)
    public class SystemLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Level { get; set; } = "Info"; // Info, Warning, Error
        public string Message { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    // 6. 管理后台用户表 (需求 1)
    public class User
    {
        [Key]
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty; // 加密后的密码
        public string Role { get; set; } = "Administrator";
    }
}
