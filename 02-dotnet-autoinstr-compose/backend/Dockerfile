FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Install OpenTelemetry .NET auto-instrumentation
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl unzip && \
    curl -sSfL https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/releases/latest/download/otel-dotnet-auto-install.sh \
      -o /tmp/otel-install.sh && \
    OTEL_DOTNET_AUTO_HOME=/otel sh /tmp/otel-install.sh && \
    rm /tmp/otel-install.sh && \
    apt-get remove -y curl && \
    rm -rf /var/lib/apt/lists/*

ENV OTEL_DOTNET_AUTO_HOME=/otel
ENV CORECLR_ENABLE_PROFILING=1
ENV CORECLR_PROFILER={918728DD-259F-4A6A-AC2B-B85E1B658318}
ENV CORECLR_PROFILER_PATH=/otel/linux-x64/OpenTelemetry.AutoInstrumentation.Native.so
ENV DOTNET_ADDITIONAL_DEPS=/otel/AdditionalDeps
ENV DOTNET_SHARED_STORE=/otel/store
ENV DOTNET_STARTUP_HOOKS=/otel/net/OpenTelemetry.AutoInstrumentation.StartupHook.dll

EXPOSE 8080
ENTRYPOINT ["dotnet", "backend.dll"]
