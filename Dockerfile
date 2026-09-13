# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/OficinaExecucao.API/OficinaExecucao.API.csproj", "src/OficinaExecucao.API/"]
COPY ["src/OficinaExecucao.Infrastructure/OficinaExecucao.Infrastructure.csproj", "src/OficinaExecucao.Infrastructure/"]
COPY ["src/OficinaExecucao.Application/OficinaExecucao.Application.csproj", "src/OficinaExecucao.Application/"]
COPY ["src/OficinaExecucao.Domain/OficinaExecucao.Domain.csproj", "src/OficinaExecucao.Domain/"]

RUN dotnet restore "src/OficinaExecucao.API/OficinaExecucao.API.csproj"

COPY . .
WORKDIR /src/src/OficinaExecucao.API
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser

COPY --from=build /app/publish ./
RUN chown -R appuser:appgroup /app

USER appuser

ENV ASPNETCORE_URLS=http://+:5000
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 5000

ENTRYPOINT ["dotnet", "OficinaExecucao.API.dll"]
