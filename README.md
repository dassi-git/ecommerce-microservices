# Enterprise E-Commerce Microservices System (.NET 10)

This project is a distributed, production-grade microservices application showcasing a hybrid Event-Driven Distributed Saga with Polyglot Persistence, distributed caching, and built-in observability.

## System Topology
- **ApiGateway:** YARP Proxy with Custom Correlation ID Tracing Middleware (Port 8080)
- **ProductCatalogService:** Replica-scaled MongoDB + Cache-Aside Distributed Redis Caching
- **OrderService:** Microsoft SQL Server (EF Core) + MassTransit Messaging Publisher
- **InventoryService:** Microsoft SQL Server (EF Core) + Stock Management Consumer
- **NotificationService:** Centralized AMQP Event Logging Consumer
- **Telemetry Nodes:** Prometheus Dashboard (9090) & Grafana Analytics (3000)

## How to Run the Project (Single Command Launch)
To spin up the entire infrastructure stack, run the following single command from the repository root:

```bash
docker compose up -d --build