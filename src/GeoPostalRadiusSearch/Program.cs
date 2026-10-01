using GeoPostalRadiusSearch;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PostalRadiusSearcher>();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/postalcodes/radius", async (string postalCode, double radius, string? unit, PostalRadiusSearcher searcher, CancellationToken ct) =>
{
    var useKm = string.Equals(unit, "km", StringComparison.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(postalCode) || postalCode.Length != 5 || !postalCode.All(char.IsAsciiDigit))
        return Results.BadRequest(new { error = "postalCode must be a 5-digit US postal code." });
    if (radius <= 0 || radius > 1000)
        return Results.BadRequest(new { error = "radius must be greater than 0 and at most 1000." });

    try
    {
        var result = await searcher.SearchAsync(postalCode, radius, useKm, ct);
        return result is null
            ? Results.NotFound(new { error = $"Postal code {postalCode} not found (or has no position)." })
            : Results.Ok(result);
    }
    catch (SqlException ex)
    {
        return Results.Problem($"Database error: {ex.Message}", statusCode: 502);
    }
});

app.Run();
