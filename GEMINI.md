# Protocolo GeneXus MCP Nirvana (Elite Edition v19.0)

> [!IMPORTANT]
> **Skills Required**: Before performing ANY task in this repo, the agent MUST load and follow:
>
> 1. `[GeneXus MCP Mastery](file:///.gemini/skills/genexus-mastery/SKILL.md)` - For tool performance and cache usage.
> 2. `[GeneXus 18 Guidelines](file:///.gemini/skills/genexus18-guidelines/SKILL.md)` - For official GeneXus development rules.

## 🏎️ Performance: Formula 1 Engine (v19.0)
The MCP now operates with extreme performance optimizations:
1.  **Background Flushing**: Cache updates are asynchronous. `patch` and `write` operations are instant.
2.  **Super Cache**: Object metadata (Types, Lengths, Parm rules) is stored in memory for zero-latency lookups.
3.  **Smart Read**: Reading source code automatically injects local variable definitions used in that snippet.

## 🔍 Intelligence: Unified Search & Impact Analysis
1.  **Unified Search**: `genexus_list_objects` and `genexus_search` use the same heuristic engine. You can search by Name, Type, or Description.
2.  **Enriched Results**: Search results now include the **Parm Rule** and a **Code Snippet**, saving multiple "Read" calls.
3.  **Impact Analysis**: Use `genexus_search(query="usedby:TableName")` to find every object referencing a specific table or attribute.

## [Tools] Elite Tool Usage Guide

### 1. `genexus_patch` (Surgical Edit) - **PREFER THIS**
**Purpose**: High-precision editing without transporting full objects.
- **Usage**: Use `Replace` with exact `context` (old_string) to target lines.
- **Indentation**: Indentation and whitespace are preserved exactly.

### 2. `genexus_read_source` (Paginated)
**Purpose**: Token-efficient code reading.
- **Usage**: Use `offset` and `limit` to read specific blocks of large objects.
- **Metadata**: Automatically returns definitions for `&Variables` found in the snippet.

### 3. `genexus_validate` (Pre-save)
**Purpose**: Native SDK syntax check. Returns real GX18 diagnostics.

### 4. `genexus_test` (GXtest Integration)
**Purpose**: Executes Unit Tests via MSBuild Task and returns real-time assertion results.

---

## [Workflow] "Nirvana" Elite Workflow

| Step | Action | Tool |
| :--- | :--- | :--- |
| **1. Explore** | Find objects and their signatures | `genexus_search` |
| **2. Impact** | Check what else needs to change | `genexus_search(query="usedby:...")` |
| **3. Read** | Read specific code blocks | `genexus_read_source(offset, limit)` |
| **4. Patch** | Apply surgical code changes | `genexus_patch(operation="Replace")` |
| **5. Verify** | Run native validation & tests | `genexus_validate` -> `genexus_test` |

---

## [Debt] Dívida Técnica & Evolução (Resolved in v19.0)
1.  **I/O Latency**: Fixed via Background Flushing.
2.  **Search Ambiguity**: Fixed via Unified Heuristic Engine.
3.  **Token Bloat**: Fixed via Paginated Reading and Enriched Search.
4.  **SDK Stability**: Fixed via PATH and Working Directory injection in Gateway.
