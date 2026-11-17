# Build the Blazor WebAssembly app from the repository root.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Stria.Client/Stria.Client.csproj", "Stria.Client/"]
RUN dotnet restore "Stria.Client/Stria.Client.csproj"
COPY . .
RUN dotnet publish "Stria.Client/Stria.Client.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

# Serve the published static WebAssembly files with SPA route fallback.
FROM nginx:alpine AS final
COPY --from=publish /app/publish/wwwroot /usr/share/nginx/html
COPY ["Stria.Client/nginx.conf", "/etc/nginx/conf.d/default.conf"]
EXPOSE 80
