using System;
using System.Collections.Generic;

namespace WpfMapApp2.Models;

public partial class Hospital
{
    public int Id { get; set; }

    public string? Category { get; set; }

    public int? Level { get; set; }

    public string Name { get; set; } = null!;

    public string? District { get; set; }

    public string? LevelLabel { get; set; }

    public string? DetailType { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public double? Longitude { get; set; }

    public double? Latitude { get; set; }

    public decimal? Score { get; set; }

    public DateTime? CreatedAt { get; set; }

    public double? WgsLongitude { get; set; }

    public double? WgsLatitude { get; set; }
}
