FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

COPY . .

RUN dotnet restore \
    src/__PROJECT_NAMESPACE__.Operations/__PROJECT_NAMESPACE__.Operations.csproj

RUN dotnet publish \
    src/__PROJECT_NAMESPACE__.Operations/__PROJECT_NAMESPACE__.Operations.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "__PROJECT_NAMESPACE__.Operations.dll"]
