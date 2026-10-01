# GeoPostalRadiusSearch

.NET 10 demo: given a US postal code and a radius, returns every other US postal code within that radius.

- Data: `RatingGeography` on the DEV SQL server (`DBRating_DEV`), tables `Geo.PostalCode` + `Geo.PostalCodePosition` (a single lat/long point per postal code, not a shape).
- Method: look up the center point, apply a lat/long bounding box (cheap prefilter), then compute exact great-circle (haversine/spherical law of cosines) distance in SQL.
- Auth: Windows integrated security; you must be on the network with access to DEV.

## Run
```
cd src\GeoPostalRadiusSearch
dotnet run --urls http://localhost:5199
```
Open http://localhost:5199 or call the API:
```
GET /api/postalcodes/radius?postalCode=55344&radius=10&unit=mi   (unit: mi | km)
```
Change the connection string in `appsettings.json` (`ConnectionStrings:RatingGeography`) to target another environment.
