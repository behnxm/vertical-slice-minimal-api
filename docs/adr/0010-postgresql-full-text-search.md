# 10. Native PostgreSQL Full-Text Search Over External Search Infrastructure

Date: 2026-10-31
Status: Accepted

## Context

Task tracking and collaboration systems require fast, intuitive search capabilities across task titles, descriptions, and labels. External search engines (such as Elasticsearch, OpenSearch, or Algolia) provide rich full-text capabilities but introduce severe operational and architectural complexity:
- Additional distributed infrastructure to provision, patch, monitor, and scale.
- Eventual consistency and synchronization lag between the primary relational database and the search index (dual-write problem, CDC/Debezium pipeline overhead).
- Increased infrastructure cost and operational failure modes.

PostgreSQL has mature, high-performance native full-text search primitives with linguistic stemming, lexemes, ranking, and GIN indexing.

## Decision

I chose **native PostgreSQL full-text search** powered by generated `tsvector` columns, English stemming, and `GIN` indexes.

1. `TaskItem` defines a generated column:
   ```sql
   search_vector tsvector GENERATED ALWAYS AS (
       to_tsvector('english', coalesce(title, '') || ' ' || coalesce(description, ''))
   ) STORED
   ```
2. A PostgreSQL Generalized Inverted Index (`GIN`) is created over `search_vector`:
   ```csharp
   builder.HasIndex(t => t.SearchVector).HasMethod("gin");
   ```
3. Full-text queries are executed via EF Core Npgsql integration using `EF.Functions.WebSearchToTsQuery`:
   ```csharp
   query = query.Where(t => t.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", searchTrimmed)));
   ```
4. Labels use PostgreSQL native array containment (`@>`) indexed via a second GIN index.

## Consequences

### Positive
- **Zero Synchronization Lag:** The `tsvector` is updated atomically in the same database transaction as row inserts and updates; zero dual-write lag.
- **Zero External Infrastructure:** No Elasticsearch clusters or sync daemons required.
- **Human-Friendly Syntax:** `websearch_to_tsquery` supports intuitive Google-like search syntax (e.g. `"quoted phrases"`, `OR`, `-negation`) safely without syntax injection crashes.
- **Extreme Speed:** GIN-indexed lookups execute in sub-millisecond time.

### Negative
- Advanced relevance ranking or cross-entity fuzzy typo-tolerance is limited compared to dedicated search engines.
- English dictionary stemming is configured; multi-language dictionary switching requires dynamic `to_tsvector` configuration.

### Alternatives Considered
- **Elasticsearch / OpenSearch:** Rejected due to operational complexity and dual-write synchronization requirements.
- **SQL `LIKE %search%` / `ILIKE`:** Rejected due to table scans and lack of stemming or linguistic parsing.
