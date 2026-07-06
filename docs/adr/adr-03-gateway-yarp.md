# ADR-03: Use YARP as the API Gateway for the E-commerce Platform

- Status: Accepted
- Date: 2026-07-06

## Context
The solution requires a modern API Gateway that can route requests to backend microservices, support a Backend-for-Frontend (BFF) aggregation pattern, and integrate cleanly with the ASP.NET Core pipeline. The gateway must also support efficient parallel calls to downstream services for composite views such as the dashboard and order-details experience.

We evaluated Ocelot as a possible gateway choice, but the platform also requires tight integration with the .NET 10 request pipeline, high-performance routing, and straightforward implementation of custom gateway behavior.

## Decision
We chose YARP (Yet Another Reverse Proxy) as the API Gateway for the platform.

YARP was selected because it is:
- A modern reverse proxy built specifically for ASP.NET Core.
- Highly performant and designed for modern cloud-native and microservice workloads.
- Deeply integrated with the .NET request pipeline, middleware, and dependency injection model.
- Well-suited for implementing custom gateway endpoints and advanced composition logic.

## Consequences
### Positive
- The gateway integrates natively with ASP.NET Core and .NET 10 middleware.
- Routing and request forwarding are efficient and modern.
- We were able to implement a custom parallel BFF aggregation endpoint for dashboard-style views, where multiple backend calls execute concurrently and return a unified JSON payload.
- The gateway can be extended easily for future cross-cutting concerns such as authentication, observability, retries, and request transformation.

### Negative
- YARP requires a slightly more explicit configuration model than some simpler gateway tools.
- Developers must understand the proxy pipeline and the role of routes and clusters when implementing custom behaviors.

## Rationale
YARP was preferred over Ocelot because the project needs more than basic routing. The gateway must also act as a composition layer for the web client. In this architecture, the gateway performs parallel HTTP calls to the OrderService and ProductCatalogService to build a richer aggregated BFF response. YARP’s native integration with the .NET pipeline and its modern proxy model made this implementation more natural, efficient, and maintainable than using Ocelot.

This choice also aligns with the broader solution architecture: the gateway is not just a router, but an active orchestration component for the frontend experience.

## Notes
The BFF endpoint implemented in the gateway demonstrates the benefit of this decision by aggregating multiple backend data sources in parallel, reducing user-perceived latency and improving the client experience.
