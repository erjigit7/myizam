# Multi-stage: sdk -> aspnet runtime (ТЗ v2.0 §14)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Myizam.sln .
COPY src/Myizam.Core/Myizam.Core.csproj src/Myizam.Core/
COPY src/Myizam.Data/Myizam.Data.csproj src/Myizam.Data/
COPY src/Myizam.Api/Myizam.Api.csproj src/Myizam.Api/
COPY src/Myizam.Ingestion/Myizam.Ingestion.csproj src/Myizam.Ingestion/
COPY tests/Myizam.Ingestion.Tests/Myizam.Ingestion.Tests.csproj tests/Myizam.Ingestion.Tests/
RUN dotnet restore src/Myizam.Api/Myizam.Api.csproj
COPY src/ src/
RUN dotnet publish src/Myizam.Api/Myizam.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Myizam.Api.dll"]
