# Build context is the repo root (not this service folder), so this can COPY the shared/
# projects it references by relative path — see .github/workflows/ci.yml's docker-build-push
# job, which passes context-path: . accordingly.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/OrderSystem.Contracts/OrderSystem.Contracts.csproj shared/OrderSystem.Contracts/
COPY shared/OrderSystem.Messaging/OrderSystem.Messaging.csproj shared/OrderSystem.Messaging/
COPY services/order-service/src/OrderSystem.OrderService/OrderSystem.OrderService.csproj services/order-service/src/OrderSystem.OrderService/
RUN dotnet restore services/order-service/src/OrderSystem.OrderService/OrderSystem.OrderService.csproj

COPY shared/OrderSystem.Contracts/ shared/OrderSystem.Contracts/
COPY shared/OrderSystem.Messaging/ shared/OrderSystem.Messaging/
COPY services/order-service/src/OrderSystem.OrderService/ services/order-service/src/OrderSystem.OrderService/

RUN dotnet publish services/order-service/src/OrderSystem.OrderService/OrderSystem.OrderService.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Matches the base image's default ASPNETCORE_HTTP_PORTS (8080) — the Container App's ingress
# target_port (task 10 Terraform) is set to the same value.
EXPOSE 8080

# The same image also serves as the azurerm_container_app_job image, invoked as
# `dotnet OrderSystem.OrderService.dll migrate` instead of the default web-host entrypoint.
ENTRYPOINT ["dotnet", "OrderSystem.OrderService.dll"]
