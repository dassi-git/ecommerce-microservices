# ADR-04: Use Redis for Product Catalog Cache

- Status: Accepted
- Date: 2026-07-06

## Context
The ProductCatalogService serves frequent read traffic, especially for browsing and product detail views. Repeated reads of the same product should avoid redundant database work and reduce latency.

## Decision
We use Redis as a distributed cache with a cache-aside pattern for product reads.

## Consequences
- Reads that hit the cache are served with low latency and without hitting MongoDB.
- Cache invalidation is required when products are updated or stock changes, and we remove the corresponding cache entry after writes.
- This adds a small amount of operational complexity, but it materially improves read scalability.
