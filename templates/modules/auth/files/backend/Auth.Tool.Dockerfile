FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

COPY . .

RUN dotnet restore \
    src/__PROJECT_NAMESPACE__.Auth.Tool/__PROJECT_NAMESPACE__.Auth.Tool.csproj

RUN dotnet publish \
    src/__PROJECT_NAMESPACE__.Auth.Tool/__PROJECT_NAMESPACE__.Auth.Tool.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "__PROJECT_NAMESPACE__.Auth.Tool.dll"]