# Build the Blazor WebAssembly app from the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Stria.Client/Stria.Client.csproj", "Stria.Client/"]
RUN dotnet restore "Stria.Client/Stria.Client.csproj"
COPY . .
RUN dotnet publish "Stria.Client/Stria.Client.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

# Serve the published static WebAssembly files with SPA route fallback.
FROM nginx:alpine AS final
# Pick up Alpine security fixes published after the nginx image was built (Trivy fails on fixable HIGH/CRITICAL).
RUN apk upgrade --no-cache
COPY --from=publish /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 80
