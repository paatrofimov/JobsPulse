# One image for every role: `--role webhook` on Cloud Run, `--role bot` as a long-living process, one-shot jobs.
# `docker build -t jobspulse . && docker run --env-file .env jobspulse`
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo
COPY . .
# ReadyToRun trims the JIT work of a cold start - it matters on a host that scales to zero.
RUN dotnet publish src/JobsPulse.Host -c Release -r linux-x64 --self-contained false -p:PublishReadyToRun=true -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "JobsPulse.Host.dll"]
CMD ["--role", "bot"]
