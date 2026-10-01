using Microsoft.Data.SqlClient;

namespace GeoPostalRadiusSearch;

public sealed record PostalMatch(string PostalCode, double Latitude, double Longitude, double Distance);

public sealed record RadiusSearchResult(string PostalCode, double Latitude, double Longitude, double Radius, string Unit, int Count, IReadOnlyList<PostalMatch> Matches);

public sealed class PostalRadiusSearcher(IConfiguration config)
{
    private const double MilesPerDegreeLat = 69.0;
    private const double KmPerDegreeLat = 111.32;
    private const double EarthRadiusMiles = 3958.8;
    private const double EarthRadiusKm = 6371.0;

    // Bounding box on lat/long first (cheap), then exact great-circle distance on the survivors.
    private const string Sql = """
        SELECT pc.PostalCode, p.Latitude, p.Longitude, d.Distance
        FROM Geo.PostalCode AS pc WITH (NOLOCK)
        INNER JOIN Geo.PostalCodePosition AS p WITH (NOLOCK) ON p.PostalCodeID = pc.PostalCodeID
        CROSS APPLY (
            SELECT @earthRadius * ACOS(CASE WHEN x.v > 1 THEN 1 WHEN x.v < -1 THEN -1 ELSE x.v END) AS Distance
            FROM (SELECT SIN(RADIANS(@lat)) * SIN(RADIANS(p.Latitude))
                       + COS(RADIANS(@lat)) * COS(RADIANS(p.Latitude)) * COS(RADIANS(p.Longitude - @lon)) AS v) AS x
        ) AS d
        WHERE pc.CountryID = @countryId
          AND pc.PostalCode <> @postalCode
          AND p.Latitude  BETWEEN @lat - @dLat AND @lat + @dLat
          AND p.Longitude BETWEEN @lon - @dLon AND @lon + @dLon
          AND d.Distance <= @radius
        ORDER BY d.Distance, pc.PostalCode;
        """;

    private const string LookupSql = """
        SELECT TOP (1) p.Latitude, p.Longitude
        FROM Geo.PostalCode AS pc WITH (NOLOCK)
        INNER JOIN Geo.PostalCodePosition AS p WITH (NOLOCK) ON p.PostalCodeID = pc.PostalCodeID
        WHERE pc.PostalCode = @postalCode AND pc.CountryID = @countryId;
        """;

    private readonly string _connectionString = config.GetConnectionString("RatingGeography")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:RatingGeography");
    private readonly int _usCountryId = config.GetValue("UsCountryId", 224);

    public async Task<RadiusSearchResult?> SearchAsync(string postalCode, double radius, bool km, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        double lat, lon;
        await using (var lookup = new SqlCommand(LookupSql, conn))
        {
            lookup.Parameters.AddWithValue("@postalCode", postalCode);
            lookup.Parameters.AddWithValue("@countryId", _usCountryId);
            await using var r = await lookup.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            lat = r.GetDouble(0);
            lon = r.GetDouble(1);
        }

        var perDegreeLat = km ? KmPerDegreeLat : MilesPerDegreeLat;
        var dLat = radius / perDegreeLat;
        var cos = Math.Max(Math.Cos(lat * Math.PI / 180.0), 0.01);
        var dLon = radius / (perDegreeLat * cos);

        var matches = new List<PostalMatch>();
        await using var cmd = new SqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("@postalCode", postalCode);
        cmd.Parameters.AddWithValue("@countryId", _usCountryId);
        cmd.Parameters.AddWithValue("@lat", lat);
        cmd.Parameters.AddWithValue("@lon", lon);
        cmd.Parameters.AddWithValue("@dLat", dLat);
        cmd.Parameters.AddWithValue("@dLon", dLon);
        cmd.Parameters.AddWithValue("@radius", radius);
        cmd.Parameters.AddWithValue("@earthRadius", km ? EarthRadiusKm : EarthRadiusMiles);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            matches.Add(new PostalMatch(rd.GetString(0), rd.GetDouble(1), rd.GetDouble(2), Math.Round(rd.GetDouble(3), 2)));

        return new RadiusSearchResult(postalCode, lat, lon, radius, km ? "km" : "mi", matches.Count, matches);
    }
}
