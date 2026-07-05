# Architectural Decision Record (ADR) 1: Product Catalog Database Selection

## Context
The Product Catalog Service handles product definitions, variations, metadata, and pricing. This data requires highly efficient read-throughput and structural flexibility, as products can have dynamic, changing fields (e.g., electronic specs vs. apparel sizes).

## Decision
We decided to utilize **MongoDB** (a NoSQL Document Database) for the ProductCatalogService.

## Consequences
- **Pros:** Highly flexible JSON schema model that supports dynamic product attributes without rigid table migrations. Excellent horizontal read scaling.
- **Cons:** Lack of native relational join enforcement or complex multi-document ACID transactions, which are not required for static catalog browsing.