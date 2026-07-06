# Architecture Overview

## Overview
This solution implements an e-commerce platform as a set of cooperating microservices. The API gateway exposes a single entry point for clients, while the backend services are split by domain and own their data stores.

## Components
- API Gateway: YARP-based reverse proxy with correlation ID propagation and BFF endpoints.
- ProductCatalogService: MongoDB-backed catalog service with Redis cache-aside reads and two replicas behind Nginx.
- OrderService: SQL Server-backed order service that publishes saga events and handles compensation.
- InventoryService: SQL Server-backed inventory service that reserves stock and restores stock on cancellation.
- NotificationService: RabbitMQ consumer that emits confirmation and rejection notifications.
- Observability: Prometheus metrics, Grafana dashboards, structured logs, and health checks.

## Persistence choices
- Catalog: MongoDB because product documents vary in shape and benefit from flexible schemas.
- Orders and inventory: SQL Server because the business rules require strong ACID guarantees and relational consistency.
- Cache: Redis to reduce repeated catalog reads and improve read-throughput.

## Saga behavior
The order flow is choreography-based. The order service publishes an order event, inventory reserves or rejects stock, and the order service updates its state accordingly. On cancellation, inventory is compensated and the order is marked cancelled.

## Load balancing
Product catalog requests are routed through Nginx to two catalog replicas so the system remains available even if one instance is stopped. The gateway also routes catalog traffic through the load balancer.

## ADRs
- [docs/adr/adr-01-catalog-db.md](docs/adr/adr-01-catalog-db.md)
- [docs/adr/adr-02-orders-db.md](docs/adr/adr-02-orders-db.md)
- [docs/adr/adr-03-gateway-yarp.md](docs/adr/adr-03-gateway-yarp.md)
- [docs/adr/adr-04-cache-redis.md](docs/adr/adr-04-cache-redis.md)
