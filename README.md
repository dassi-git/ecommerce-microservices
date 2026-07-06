# Enterprise E-Commerce Microservices System (.NET 10)

This project is a distributed, production-grade microservices application showcasing a choreography-based saga, polyglot persistence, distributed caching, gateway composition, load balancing, and observability.

## One-command startup
From the repository root:

```bash
docker compose up -d --build
```

## Main endpoints
- Gateway: http://localhost:8080
- BFF dashboard: http://localhost:8080/api/bff/dashboard
- BFF order details: http://localhost:8080/api/bff/order-details/1
- Product catalog via gateway: http://localhost:8080/api/products
- Orders via gateway: http://localhost:8080/api/orders
- Inventory via gateway: http://localhost:8080/api/inventory
- Nginx load balancer: http://localhost:8081/health
- Grafana: http://localhost:3000
- Prometheus: http://localhost:9090
- RabbitMQ UI: http://localhost:15672

## Demo flow
1. Create a product through the gateway.
2. Place an order through the gateway.
3. Observe the happy-path or compensation-path saga in the logs.
4. Read the same product twice to see the Redis cache hit/miss behavior.
5. Call the catalog endpoint repeatedly through the Nginx load balancer to observe replica distribution.

## Architecture highlights
- API Gateway: YARP with correlation ID propagation and BFF endpoints.
- ProductCatalogService: MongoDB document store with Redis cache-aside reads and two replicas behind Nginx.
- OrderService: SQL Server order state and event publication for the saga.
- InventoryService: SQL Server inventory state with compensation handling on cancellation.
- NotificationService: RabbitMQ consumers that log confirmation and rejection outcomes.
- Observability: Prometheus metrics on /metrics and Grafana dashboards.

## Architecture documentation
See [docs/architecture.md](docs/architecture.md) for the full system overview, ADRs, and design rationale.