# Build context: repository root.  docker build -f Stria.SampleService/Dockerfile .
# Match the image tags to the TargetFramework of the projects (10.0).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Stria.SampleService/Stria.SampleService.csproj Stria.SampleService/
RUN dotnet restore Stria.SampleService/Stria.SampleService.csproj
COPY Stria.SampleService/ Stria.SampleService/
RUN dotnet publish Stria.SampleService/Stria.SampleService.csproj -c Release -o /app/publish --no-restore
 
FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Stria.SampleService.dll"]
 
