# ReportServerWeb on Linux - see ../ReportServerAPI/Dockerfile for the build context.
#
#   cd Showcase/ReportServer
#   docker compose up --build

# ---- build ------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Showcase/ReportServer/ReportServerWeb/ Showcase/ReportServer/ReportServerWeb/
RUN find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
RUN dotnet publish Showcase/ReportServer/ReportServerWeb/ReportServerWeb.csproj \
      -c Release --self-contained false -o /app

# ---- run --------------------------------------------------------------------------
# Nothing native: the web only serves files and passes /api on to ReportServerAPI.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV Urls=http://+:8080
# Where ReportServerAPI is; docker-compose.yml sets it to the name of its service.
ENV ReportServerApi=api:8080

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReportServerWeb.dll"]
