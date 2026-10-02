# GeoPostalRadiusSearch

.NET 10 demo: radius search by latitude/longitude over postal codes, cities and postal groups, restricted to the given country IDs (or postal group type IDs).

- Data: `RatingGeography` on the DEV SQL server (`DBRating_DEV`): `Geo.PostalCodePoint`, `Geo.CityPoint`, `Geo.PostalGroupPoint` (lat/long + persisted `geography` column + `LatBand`).
- Method: sprocs `Geo.SearchPostalCodeByRadius`, `Geo.SearchCityByRadius`, `Geo.SearchPostalGroupByRadius`. A bounding box is seeked per 0.25 degree latitude band on index `(CountryID | PostalGroupTypeID, LatBand, Longitude)`, then exact `STDistance` filters the candidates. No spatial index (it cannot filter by country).
- Auth: Windows integrated security; you must be on the network with access to DEV.

## Run
```
cd src\GeoPostalRadiusSearch
dotnet run --urls http://localhost:5199
```
Open http://localhost:5199 or call the API (unit: mi | km, default mi):
```
GET /api/postalcodes/radius?lat=44.82&lon=-93.34&radius=100&unit=mi&countryIds=224,37
GET /api/cities/radius?lat=44.82&lon=-93.34&radius=50&countryIds=224
GET /api/postalgroups/radius?lat=44.82&lon=-93.34&radius=100&postalGroupTypeIds=3
```
Change the connection string in `appsettings.json` (`ConnectionStrings:RatingGeography`) to target another environment.
