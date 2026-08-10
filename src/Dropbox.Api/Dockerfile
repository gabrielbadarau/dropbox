# Pinned to match the SDK version this project is developed against
# (10.0.302) and the runtime package versions already pinned throughout
# Dropbox.Api.csproj (10.0.10) - same reproducibility reasoning as pinning
# NuGet packages and the Postgres/MinIO image tags.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

# Restore first, in its own layer, so editing source code later doesn't
# invalidate the (slow) package restore cache.
COPY src/Dropbox.Api/Dropbox.Api.csproj src/Dropbox.Api/
RUN dotnet restore src/Dropbox.Api/Dropbox.Api.csproj

COPY src/Dropbox.Api/ src/Dropbox.Api/
RUN dotnet publish src/Dropbox.Api/Dropbox.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS runtime
WORKDIR /app

# curl needed for the container HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=5s --retries=5 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Dropbox.Api.dll"]
