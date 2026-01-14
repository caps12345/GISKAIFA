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
        // 错误写法 (会导致你现在的报错):
        // optionsBuilder.UseSqlite("C:/Users/mmn/Documents/GitHub/GISKAIFA-1/WpfMapApp2/Data/nanjing.db");

        // 正确写法 1 (推荐): 加上 "Data Source=" 并使用 @ 符号避免转义问题
        optionsBuilder.UseSqlite(@"Data Source=D:\GIS_DATA\Data\nanjing.db");

        // 或者 正确写法 2 (使用正斜杠):
        // optionsBuilder.UseSqlite("Data Source=C:/Users/mmn/Documents/GitHub/GISKAIFA-1/WpfMapApp2/Data/nanjing.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Community>(entity =>
        {
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
            entity.Property(e => e.DoctorCount).HasColumnName("doctor_count");
            entity.Property(e => e.BedCount).HasColumnName("bed_count");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
