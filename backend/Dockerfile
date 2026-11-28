# syntax=docker/dockerfile:1
# API de Turnero. Build desde la RAÍZ del repo:  docker build -f backend/Dockerfile -t turnero-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# product.json es la fuente única del nombre del producto (lo enlaza Api.csproj).
COPY product.json ./
COPY backend/global.json backend/nuget.config backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/Api/Api.csproj backend/src/Api/
RUN dotnet restore backend/src/Api/Api.csproj
COPY backend/ backend/
# El documento OpenAPI ya está commiteado; no hace falta regenerarlo en la imagen.
RUN dotnet publish backend/src/Api/Api.csproj -c Release --no-restore -o /app \
    -p:OpenApiGenerateDocuments=false -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
COPY --from=build /app ./
EXPOSE 8080
# Usuario sin privilegios que traen las imágenes oficiales de .NET.
USER $APP_UID
ENTRYPOINT ["dotnet", "Api.dll"]
