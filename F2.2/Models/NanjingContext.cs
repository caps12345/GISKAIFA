using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace WpfMapApp2.Models;

public partial class NanjingContext : DbContext
{
    public NanjingContext()
    {
    }

    public NanjingContext(DbContextOptions<NanjingContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Community> Communities { get; set; }

    public virtual DbSet<Hospital> Hospitals { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // [修改点] 路径更新为 D:\GIS_DATA\Data\nanjing.db
        // 使用 @ 符号处理反斜杠，确保路径正确
        optionsBuilder.UseSqlite(@"Data Source=D:\GIS_DATA\Data\nanjing.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Community>(entity =>
        {
            entity.ToTable("communities"); // 显式指定表名，防止EF默认复数化导致找不到表
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.District).HasColumnName("district");
            entity.Property(e => e.FinalPopulation).HasColumnName("final_population");
            entity.Property(e => e.Latitude).HasColumnName("latitude");
            entity.Property(e => e.Longitude).HasColumnName("longitude");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Street).HasColumnName("street");
            entity.Property(e => e.TessellationArea).HasColumnName("tessellation_area");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.WgsLatitude).HasColumnName("wgs_latitude");
            entity.Property(e => e.WgsLongitude).HasColumnName("wgs_longitude");
        });

        modelBuilder.Entity<Hospital>(entity =>
        {
            entity.ToTable("hospitals"); // 显式指定表名
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DetailType).HasColumnName("detail_type");
            entity.Property(e => e.District).HasColumnName("district");
            entity.Property(e => e.Latitude).HasColumnName("latitude");
            entity.Property(e => e.Level).HasColumnName("level");
            entity.Property(e => e.LevelLabel).HasColumnName("level_label");
            entity.Property(e => e.Longitude).HasColumnName("longitude");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.WgsLatitude).HasColumnName("wgs_latitude");
            entity.Property(e => e.WgsLongitude).HasColumnName("wgs_longitude");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}