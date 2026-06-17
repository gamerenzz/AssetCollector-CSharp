using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AssetServer
{
    public class Asset
    {
        [Key]
        public string MacAddress { get; set; } = string.Empty; 
        
        public string Hostname { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string OS { get; set; } = string.Empty;
        public string CPU { get; set; } = string.Empty;
        public string RAM { get; set; } = string.Empty;
        public string Disk { get; set; } = string.Empty;
        public string GPU { get; set; } = string.Empty;
        public string Monitor { get; set; } = string.Empty;
        public string Motherboard { get; set; } = string.Empty;
        public string SystemModel { get; set; } = string.Empty;

        public string Building { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;

        public string CustomFieldsJson { get; set; } = "{}";
        public string Remarks { get; set; } = string.Empty;

        public int? GroupId { get; set; }
        [ForeignKey("GroupId")]
        public Group? Group { get; set; }

        public DateTime LastReportTime { get; set; } = DateTime.Now;

        [JsonIgnore]
        public ICollection<SoftwareInfo> InstalledSoftware { get; set; } = new List<SoftwareInfo>();
    }

    public class SoftwareInfo
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string AssetId { get; set; } = string.Empty; 
        [ForeignKey("AssetId")]
        [JsonIgnore]
        public Asset? Asset { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string InstallDate { get; set; } = string.Empty;
    }

    public class Group
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public Policy? Policy { get; set; }
    }

    public class Policy
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int GroupId { get; set; }
        [ForeignKey("GroupId")]
        [JsonIgnore]
        public Group? Group { get; set; }

        public bool CollectHardware { get; set; } = true;  
        public bool CollectSoftware { get; set; } = true;  
        public int ScanIntervalMinutes { get; set; } = 120; 

        // 【核心新增】策略版本号控制属性，默认版本 1
        public int Version { get; set; } = 1;
    }

    public class SystemLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Level { get; set; } = "Info"; 
        public string Message { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class User
    {
        [Key]
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty; 
        public string Role { get; set; } = "Administrator";
    }
}
