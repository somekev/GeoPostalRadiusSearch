using GeoPostalRadiusSearch;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RadiusSearcher>();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// GET /api/postalcodes/radius?lat=44.82&lon=-93.34&radius=100&unit=mi&countryIds=224,37
app.MapGet("/api/postalcodes/radius", (double lat, double lon, double radius, string? unit, string countryIds, RadiusSearcher s, CancellationToken ct) =>
    Run(lat, lon, radius, countryIds, "countryIds", (ids, km) => s.SearchPostalCodesAsync(ids, lat, lon, radius, km, ct), unit));

// GET /api/cities/radius?lat=44.82&lon=-93.34&radius=50&unit=km&countryIds=224
app.MapGet("/api/cities/radius", (double lat, double lon, double radius, string? unit, string countryIds, RadiusSearcher s, CancellationToken ct) =>
    Run(lat, lon, radius, countryIds, "countryIds", (ids, km) => s.SearchCitiesAsync(ids, lat, lon, radius, km, ct), unit));

// GET /api/postalgroups/radius?lat=44.82&lon=-93.34&radius=100&unit=mi&postalGroupTypeIds=3
app.MapGet("/api/postalgroups/radius", (double lat, double lon, double radius, string? unit, string postalGroupTypeIds, RadiusSearcher s, CancellationToken ct) =>
    Run(lat, lon, radius, postalGroupTypeIds, "postalGroupTypeIds", (ids, km) => s.SearchPostalGroupsAsync(ids, lat, lon, radius, km, ct), unit));

app.Run();

static async Task<IResult> Run<T>(double lat, double lon, double radius, string idList, string idParameterName, Func<int[], bool, Task<T>> search, string? unit)
{
    if (lat is < -90 or > 90 || double.IsNaN(lat)) return Results.BadRequest(new { error = "lat must be between -90 and 90." });
    if (lon is < -180 or > 180 || double.IsNaN(lon)) return Results.BadRequest(new { error = "lon must be between -180 and 180." });
    if (!(radius > 0 && radius <= 1000)) return Results.BadRequest(new { error = "radius must be greater than 0 and at most 1000." });
    if (unit is not null && !unit.Equals("mi", StringComparison.OrdinalIgnoreCase) && !unit.Equals("km", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "unit must be mi or km." });

    var ids = (idList ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(v => int.TryParse(v, out var n) ? n : (int?)null).ToArray();
    if (ids.Length == 0 || ids.Any(i => i is null))
        return Results.BadRequest(new { error = $"{idParameterName} must be a comma separated list of integer IDs." });

    try
    {
        return Results.Ok(await search(ids.Select(i => i!.Value).ToArray(), string.Equals(unit, "km", StringComparison.OrdinalIgnoreCase)));
    }
    catch (SqlException ex)
    {
        return Results.Problem($"Database error: {ex.Message}", statusCode: 502);
    }
}
