# ProductMS

## How to run a migration
dotnet ef migrations add ProductMigration -c ProductDbContext

## How to setup the environment

# with metrics
dotnet run --urls "http://*:5008" --project ProductMS/ProductMS.csproj

# without metrics
dotnet run --urls "http://*:5008" --project ProductMS/ProductMS.csproj

