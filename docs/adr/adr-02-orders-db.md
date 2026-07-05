# Architectural Decision Record (ADR) 2: Order Service Database Selection

## Context
The Order Service orchestrates user purchases, checkout transactions, and ledger status values (Pending, Confirmed, Cancelled). This service demands high consistency, financial integrity, and relational validation.

## Decision
We decided to utilize **Microsoft SQL Server** (a Relational/SQL Database) mapped via Entity Framework Core for the OrderService.

## Consequences
- **Pros:** Full ACID compliance, guaranteed transactional reliability, and absolute schema safety to prevent transactional corruption or missing state flags.
- **Cons:** Rigid table structures that require database migration scripts for structural code modifications.