using System.Data;
using Microsoft.Data.SqlClient;

namespace GeoPostalRadiusSearch;

public sealed record PostalCodeMatch(int PostalCodeId, string PostalCode, int CountryId, double Latitude, double Longitude, double Distance);

public sealed record CityMatch(int CityId, int CountryId, double Latitude, double Longitude, double Distance);

public sealed record PostalGroupMatch(int PostalGroupId, int PostalGroupTypeId, string Name, double Latitude, double Longitude, double Distance);

public sealed record RadiusSearchResult<T>(double Latitude, double Longitude, double Radius, string Unit, int Count, IReadOnlyList<T> Matches);

public sealed class RadiusSearcher(IConfiguration config)
{
    private readonly string _connectionString = config.GetConnectionString("RatingGeography")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:RatingGeography");

    public Task<RadiusSearchResult<PostalCodeMatch>> SearchPostalCodesAsync(IEnumerable<int> countryIds, double lat, double lon, double radius, bool km, CancellationToken ct) =>
        SearchAsync("Geo.SearchPostalCodeByRadius", "@CountryIDTable", countryIds, lat, lon, radius, km,
            r => new PostalCodeMatch(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5)), ct);

    public Task<RadiusSearchResult<CityMatch>> SearchCitiesAsync(IEnumerable<int> countryIds, double lat, double lon, double radius, bool km, CancellationToken ct) =>
        SearchAsync("Geo.SearchCityByRadius", "@CountryIDTable", countryIds, lat, lon, radius, km,
            r => new CityMatch(r.GetInt32(0), r.GetInt32(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4)), ct);

    public Task<RadiusSearchResult<PostalGroupMatch>> SearchPostalGroupsAsync(IEnumerable<int> postalGroupTypeIds, double lat, double lon, double radius, bool km, CancellationToken ct) =>
        SearchAsync("Geo.SearchPostalGroupByRadius", "@PostalGroupTypeIDTable", postalGroupTypeIds, lat, lon, radius, km,
            r => new PostalGroupMatch(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5)), ct);

    private async Task<RadiusSearchResult<T>> SearchAsync<T>(string procedure, string idTableParameter, IEnumerable<int> ids, double lat, double lon,
        double radius, bool km, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        var idTable = new DataTable();
        idTable.Columns.Add("ID", typeof(int));
        foreach (var id in ids.Distinct()) idTable.Rows.Add(id);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add(new SqlParameter(idTableParameter, SqlDbType.Structured) { TypeName = "Geo.IDType", Value = idTable });
        cmd.Parameters.AddWithValue("@Latitude", lat);
        cmd.Parameters.AddWithValue("@Longitude", lon);
        cmd.Parameters.AddWithValue("@Radius", radius);
        cmd.Parameters.AddWithValue("@Unit", km ? "KM" : "MI");
        cmd.Parameters.AddWithValue("@NTUser", "GeoRadius");
        cmd.Parameters.AddWithValue("@System", "GeoPostalRadiusSearch");
        cmd.Parameters.AddWithValue("@SessionKey", "");
        cmd.Parameters.AddWithValue("@Token", "");

        var matches = new List<T>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) matches.Add(map(rd));

        return new RadiusSearchResult<T>(lat, lon, radius, km ? "km" : "mi", matches.Count, matches);
    }
}
