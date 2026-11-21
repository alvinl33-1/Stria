# Build from the repository root so the host can reference Stria.API.Client.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Stria.API/Stria.API.csproj", "Stria.API/"]
COPY ["Stria.Client/Stria.Client.csproj", "Stria.Client/"]
RUN dotnet restore "Stria.API/Stria.API.csproj"
COPY . .
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
COPY . .
RUN dotnet publish "Stria.API/Stria.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

# EF migrations bundle: one self-contained executable that applies pending migrations. The
# release job runs it before each new revision; the app never migrates itself outside Compose.
# The placeholder connection string is never opened; it only lets the design-time factory build
# a context if the tools ask for one while bundling.
FROM build AS bundle
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
RUN dotnet tool restore
RUN rid="linux-$([ "$TARGETARCH" = amd64 ] && echo x64 || echo "$TARGETARCH")" \
    && ConnectionStrings__Stria="Host=build-placeholder" \
       dotnet ef migrations bundle --project Stria.API/Stria.API.csproj --configuration $BUILD_CONFIGURATION \
       --self-contained --target-runtime "$rid" --output /app/efbundle

# Migration-job image (`--target migrator`). runtime-deps is enough: the bundle carries its own runtime.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrator
WORKDIR /app
COPY --from=bundle /app/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

FROM runtime AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Run unprivileged, as the base image's `app` user. The key ring directory must already exist and
# belong to that user: the app can't create it under root-owned /var/lib, and a new named volume
# copies this owner and mode on first mount. 700, because the keys decrypt every auth cookie.
RUN install -d -o "$APP_UID" -g "$APP_UID" -m 700 /var/lib/stria/dpkeys
USER $APP_UID
ENTRYPOINT ["dotnet", "Stria.dll"]
