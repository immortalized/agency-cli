FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

COPY . .

RUN dotnet restore
RUN dotnet publish src/__PROJECT_NAMESPACE__.Api/__PROJECT_NAMESPACE__.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "__PROJECT_NAMESPACE__.Api.dll"]