using System.Data;
using Microsoft.Data.SqlClient;

namespace GeoPostalRadiusSearch;

public sealed record PostalMatch(string PostalCode, double Latitude, double Longitude, double Distance);

public sealed record RadiusSearchResult(string PostalCode, double Latitude, double Longitude, double Radius, string Unit, int Count, IReadOnlyList<PostalMatch> Matches);

public sealed class PostalRadiusSearcher(IConfiguration config)
{
    private readonly string _connectionString = config.GetConnectionString("RatingGeography")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:RatingGeography");
    private readonly int _usCountryId = config.GetValue("UsCountryId", 224);

    public async Task<RadiusSearchResult?> SearchAsync(string postalCode, double radius, bool km, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand("Geo.SearchPostalCodeByRadius", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PostalCode", postalCode);
        cmd.Parameters.AddWithValue("@CountryID", _usCountryId);
        cmd.Parameters.AddWithValue("@Radius", radius);
        cmd.Parameters.AddWithValue("@Unit", km ? "KM" : "MI");
        cmd.Parameters.AddWithValue("@NTUser", "GeoRadius");
        cmd.Parameters.AddWithValue("@System", "GeoPostalRadiusSearch");
        cmd.Parameters.AddWithValue("@SessionKey", "");
        cmd.Parameters.AddWithValue("@Token", "");

        await using var rd = await cmd.ExecuteReaderAsync(ct);

        // Result set 1: the center point (empty when the postal code has no point).
        if (!await rd.ReadAsync(ct)) return null;
        var lat = rd.GetDouble(0);
        var lon = rd.GetDouble(1);

        // Result set 2: matches, nearest first.
        await rd.NextResultAsync(ct);
        var matches = new List<PostalMatch>();
        while (await rd.ReadAsync(ct))
            matches.Add(new PostalMatch(rd.GetString(1), rd.GetDouble(2), rd.GetDouble(3), rd.GetDouble(4)));

        return new RadiusSearchResult(postalCode, lat, lon, radius, km ? "km" : "mi", matches.Count, matches);
    }
}
