﻿# CartMS

## How to setup dapr
arch -arm64 brew install dapr/tap/dapr-cli
dapr init
dapr run --app-id basket --app-port 5001 dotnet run Basket.API.csproj

to set up another redis:
docker run -d --name redis -p 6379:6379 redis:alpine

daprd:1.8.4 for .net 6.0

## How to run a migration
dotnet ef migrations add CartMigration -c CartDbContext

## How to setup the environment

### with metrics
dapr run --app-port 5001 --app-id cart --app-protocol http --dapr-http-port 3501 --metrics-port 9091 
dotnet run --urls "http://*:5001" --project CartMS/CartMS.csproj

### without metrics
dapr run --app-port 5001 --app-id cart --app-protocol http --dapr-http-port 3501 
dotnet run --urls "http://*:5001" --project CartMS/CartMS.csproj

"In self-hosted mode, running the Dapr CLI run command launches the daprd executable
with the provided application executable. This is the recommended way of running the
Dapr sidecar when working locally in scenarios such as development and testing."

More details are found in: https://docs.dapr.io/concepts/dapr-services/sidecar/

How to build a dockerfile:
https://learn.microsoft.com/en-us/dotnet/architecture/dapr-for-net-developers/getting-started

How Redis is automatically configured in Dapr:
https://docs.dapr.io/developing-applications/building-blocks/state-management/howto-get-save-state/

Locale issues in Ubuntu:
https://askubuntu.com/questions/1219271/im-having-a-problem-with-locale-and-locale-gen-in-ubuntu

