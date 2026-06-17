using Microsoft.EntityFrameworkCore;

namespace AssetServer
{
    public class AssetDbContext : DbContext
    {
        public AssetDbContext(DbContextOptions<AssetDbContext> options) : base(options)
        {
        }

        public DbSet<Asset> Assets { get; set; }
        public DbSet<SoftwareInfo> SoftwareInfos { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<Policy> Policies { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置级联删除关系：资产被删除时，其关联的已安装软件明细自动清空
            modelBuilder.Entity<SoftwareInfo>()
                .HasOne(s => s.Asset)
                .WithMany(a => g => a.InstalledSoftware)
                .HasForeignKey(s => s.AssetId)
                .OnDelete(DeleteBehavior.Cascade);

            // 确保 Group 和 Policy 是一对一关系
            modelBuilder.Entity<Group>()
                .HasOne(g => g.Policy)
                .WithOne(p => p.Group)
                .HasForeignKey<Policy>(p => p.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
