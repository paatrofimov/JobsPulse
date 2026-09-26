# Long-living Telegram bot host: `docker build -t jobspulse . && docker run --env-file .env jobspulse`
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/JobsPulse.Host -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "JobsPulse.Host.dll"]
CMD ["--role", "bot"]
