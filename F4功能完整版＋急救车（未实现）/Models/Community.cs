using System;
using System.Collections.Generic;

namespace WpfMapApp2.Models;

public partial class Community
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? District { get; set; }

    public string? Street { get; set; }

    public string? Type { get; set; }

    public double? Longitude { get; set; }

    public double? Latitude { get; set; }

    public double? TessellationArea { get; set; }

    public int? FinalPopulation { get; set; }

    public DateTime? CreatedAt { get; set; }

    public double? WgsLongitude { get; set; }

    public double? WgsLatitude { get; set; }
}
