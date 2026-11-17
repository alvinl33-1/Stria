# Build from the repository root so the host can reference Stria.API.Client.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Stria.API/Stria.API.csproj", "Stria.API/"]
COPY ["Stria.Client/Stria.Client.csproj", "Stria.Client/"]
RUN dotnet restore "Stria.API/Stria.API.csproj"
COPY . .
RUN dotnet publish "Stria.API/Stria.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM runtime AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Stria.dll"]
