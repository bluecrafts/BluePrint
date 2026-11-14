# ReportServerAPI on Linux.
#
# The build context is the GitHub.Contribute folder of the repository (two levels up
# from docker-compose.yml), because the image takes the Northwind database from
# NorthwindDB/. docker-compose.yml sets this up:
#
#   cd Showcase/ReportServer
#   docker compose up --build
#
# or on its own, from the top of the repository:
#
#   docker build -f Showcase/ReportServer/ReportServerAPI/Dockerfile -t reportserver-api .

# ---- build ------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Showcase/ReportServer/ReportServerAPI/ Showcase/ReportServer/ReportServerAPI/
COPY NorthwindDB/SQLite/northwind.db NorthwindDB/SQLite/northwind.db

# bin/ and obj/ copied from a Windows build would make MSBuild skip the compile and
# publish the Windows output; they are left out by Dockerfile.dockerignore, and
# removed here as well in case the image is built without it.
RUN find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

RUN dotnet publish Showcase/ReportServer/ReportServerAPI/ReportServerAPI.csproj \
      -c Release -r linux-x64 --self-contained false -o /app

# ---- run --------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# libfontconfig1: SkiaSharp links to it, even though the fonts are read from files.
# libicu: BluePrint breaks lines and sorts text with the ICU of the system.
# tzdata:  the time zone in TZ, for the print date on the reports.
RUN apt-get update \
 && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends libfontconfig1 libicu74 tzdata \
 && rm -rf /var/lib/apt/lists/*

# Never set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT: dates, numbers and line breaking
# all need the culture data.

WORKDIR /app
COPY --from=build /app .

# Listen on 8080 inside the container (appsettings.json says 5081 for a run on a
# machine). The unprefixed variable overrides the setting of the same name.
ENV Urls=http://+:8080

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReportServerAPI.dll"]
