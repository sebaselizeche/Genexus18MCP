# Protocolo GeneXus MCP Nirvana (Sentient Edition v18.7)

> [!IMPORTANT]
> **Skills Required**: Before performing ANY task in this repo, the agent MUST load and follow:
>
> 1. `[GeneXus MCP Mastery](file:///.gemini/skills/genexus-mastery/SKILL.md)` - For tool performance and cache usage.
> 2. `[GeneXus 18 Guidelines](file:///.gemini/skills/genexus18-guidelines/SKILL.md)` - For official GeneXus development rules.

## [Arch] Architecture: Native SDK Dual-Process

1.  **Gateway (.NET 8)**: `GxMcp.Gateway.exe`. Handles MCP protocol, Stdio, and process orchestration.
2.  **Worker (.NET 4.8 x86)**: `GxMcp.Worker.exe`. Loads GeneXus SDK DLLs natively for high-performance KB access.
3.  **Single-Threaded STA**: The Worker runs in a dedicated STA thread to prevent COM deadlocks and ensure SDK stability.

## [Tools] Tool Usage Guide (SDK Optimized)

### 1. `genexus_list_objects`
**Purpose**: Fast KB discovery via local cache.
- **Unified Logic**: Internally uses the Search Engine for instant results.
- **Params**: `filter` (Type filter like 'Procedure' or Name substring), `limit`.

### 2. `genexus_search`
**Purpose**: Advanced semantic search across the KB.
- **Features**: Case-insensitive, supports synonyms (e.g., 'acad' -> 'aluno'), and ranking by authority.
- **Params**: `query`.

### 3. `genexus_read_source`
**Purpose**: Reads source code with **Direct GUID Access**.
- **Mapping**: Automatically maps logical names (Source, Rules, Events) to GX18 internal GUIDs.
- **Bilingual**: Supports names in English and Portuguese (e.g., Rules/Regras).

### 4. `genexus_write_object`
**Purpose**: Native writing to KB objects. 
- **Validation**: Ensures logical parts are correctly mapped to internal SDK parts before saving.

### 5. `genexus_analyze` (Semantic Intelligence)
**Purpose**: Deep static analysis, BI extraction, and Linter.
- **Output**: Hybrid dependency graph, business rules, and proactive linter insights.

### 6. `genexus_bulk_index`
**Purpose**: Full KB crawl to build the `SearchIndex.json`. **Mandatory for large KBs (30k+ objects)** to enable instant search.

---

## [Workflow] "IDE-Free" Workflow Strategy

| Task             | MCP Tool to Use                                                          |
| ---------------- | ------------------------------------------------------------------------ |
| **Discovery**    | `genexus_search` (instant) or `genexus_list_objects`                     |
| **Analysis**     | `genexus_analyze` (deep) or `genexus_visualize` (graph)                  |
| **Reading**      | `genexus_read_source` (fast via GUID) or `genexus_read_object`           |
| **Editing**      | `genexus_write_object` (direct)                                          |
| **Build**        | `genexus_build` (supports Build, Sync, Reorg)                            |

---

## [Intel] Intelligence & Best Practices (GX18 Special)

- **One-Line JSON Protocol**: All communication between processes is minified (no line breaks) to prevent pipe hangs.
- **Direct GUID Access**: Accessing parts via GUID bypasses the slow UI lazy-loading (turning 2min into 2ms).
- **Prefix Intelligence**: Use prefixes for precision: `Prc:Name`, `Trn:Name`, `Wp:Name`.
- **Offline Mode**: The motor defaults to offline to prevent hangs waiting for GXserver.

For deeper technical details, consult `[docs/sdk_gx18_discovery.md](file:///c:/Projetos/GenexusMCP/docs/sdk_gx18_discovery.md)`.
