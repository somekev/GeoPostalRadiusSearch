using Microsoft.Data.SqlClient;

namespace GeoPostalRadiusSearch;

public sealed record PostalMatch(string PostalCode, double Latitude, double Longitude, double Distance);

public sealed record RadiusSearchResult(string PostalCode, double Latitude, double Longitude, double Radius, string Unit, int Count, IReadOnlyList<PostalMatch> Matches);

public sealed class PostalRadiusSearcher(IConfiguration config)
{
    private const double MetersPerMile = 1609.344;
    private const double MetersPerKm = 1000.0;

    // STDistance(<constant>) <= <value> is the form that lets SQL Server use the spatial index.
    private const string Sql = """
        DECLARE @center geography = geography::Point(@lat, @lon, 4326);
        SELECT pc.PostalCode, pt.Latitude, pt.Longitude, pt.Point.STDistance(@center) AS Meters
        FROM Geo.PostalCodePoint AS pt WITH (NOLOCK)
        INNER JOIN Geo.PostalCode AS pc WITH (NOLOCK) ON pc.PostalCodeID = pt.PostalCodeID
        WHERE pt.Point.STDistance(@center) <= @radiusMeters
          AND pc.CountryID = @countryId
          AND pc.PostalCode <> @postalCode
        ORDER BY Meters, pc.PostalCode;
        """;

    private const string LookupSql = """
        SELECT TOP (1) pt.Latitude, pt.Longitude
        FROM Geo.PostalCode AS pc WITH (NOLOCK)
        INNER JOIN Geo.PostalCodePoint AS pt WITH (NOLOCK) ON pt.PostalCodeID = pc.PostalCodeID
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

        var unitMeters = km ? MetersPerKm : MetersPerMile;
        var matches = new List<PostalMatch>();
        await using var cmd = new SqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("@postalCode", postalCode);
        cmd.Parameters.AddWithValue("@countryId", _usCountryId);
        cmd.Parameters.AddWithValue("@lat", lat);
        cmd.Parameters.AddWithValue("@lon", lon);
        cmd.Parameters.AddWithValue("@radiusMeters", radius * unitMeters);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            matches.Add(new PostalMatch(rd.GetString(0), rd.GetDouble(1), rd.GetDouble(2), Math.Round(rd.GetDouble(3) / unitMeters, 2)));

        return new RadiusSearchResult(postalCode, lat, lon, radius, km ? "km" : "mi", matches.Count, matches);
    }
}
