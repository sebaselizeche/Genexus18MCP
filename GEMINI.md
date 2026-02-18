# Protocolo GeneXus MCP Nirvana (Sentient Edition v18.5)

> [!IMPORTANT]
> **Skills Required**: Before performing ANY task in this repo, the agent MUST load and follow:
>
> 1. `[GeneXus MCP Mastery](file:///.gemini/skills/genexus-mastery/SKILL.md)` - For tool performance and cache usage.
> 2. `[GeneXus 18 Guidelines](file:///.gemini/skills/genexus18-guidelines/SKILL.md)` - For official GeneXus development rules.

## 🏗️ Architecture: Dual-Process .NET

1.  **Gateway (.NET 8)**: `GxMcp.Gateway.exe`. Handles MCP protocol, Stdio, and process orchestration.
2.  **Worker (.NET 4.8)**: `GxMcp.Worker.exe`. Loads GeneXus DLLs natively. Isolated process.

## 🛠️ Tool Usage Guide (Optimized for AI)

### 1. `genexus_list_objects`

**Purpose**: Initial discovery.

- **Params**: `limit` (max 50), `offset`.
- **Output**: JSON with `total`, `count`, and list of object names/types.
- **Tip**: Use pagination for exploring large KBs. Don't request all objects at once.

### 2. `genexus_search`

**Purpose**: Context retrieval (RAG).

- **Params**: `query` (keywords).
- **Output**: JSON `results` array with **Top 5 matches**, `score`, and **context snippet**.
- **Tip**: Use specific terms (e.g., "Customer Transaction") for better results.

### 3. `genexus_read_object`

**Purpose**: Deep analysis.

- **Params**: `name` (e.g., `Trn:Customer`).
- **Output**: Full object XML structure parsed to JSON (Source, Rules, Variables).
- **Tip**: Heavy operation. Use only when needed.

### 4. `genexus_analyze`

**Purpose**: Understanding complexity.

- **Params**: `name`.
- **Output**: JSON with `complexity` score, `calls`, `tables`, and `insights`.
- **Tip**: Use before refactoring to gauge risk.

### 5. `genexus_doctor`

**Purpose**: Diagnosing build failures.

- **Params**: `logPath` (optional).
- **Output**: JSON `diagnoses` array with `code`, `severity` (Critical/High), `line`, and `prescription`.
- **Tip**: Run immediately after a failed `genexus_build`.

### 6. `genexus_refactor`

**Purpose**: Cleanup.

- **Params**: `name`, `action` ("CleanVars").
- **Output**: JSON with `removedVariables` list.
- **Tip**: Safe to use; verifies variable usage before removal.

### 7. `genexus_history`

**Purpose**: Version control / Undo.

- **Params**: `name`, `action` ("Save"/"Restore").
- **Output**: JSON with `timestamp` and `size`.
- **Tip**: Always `Save` before making manual edits.

### 8. `genexus_wiki`

**Purpose**: Documentation.

- **Params**: `name`.
- **Output**: JSON with parsed `dependencies`, `rules`, and full `markdown`.
- **Tip**: Use to generate documentation artifacts.

### 9. `genexus_batch`

**Purpose**: Atomic operations.

- **Params**: `action` ("Add"/"Commit"), `name`, `code`.
- **Output**: JSON with `bufferedObjects` or `committedObjects` list.
- **Tip**: Use for multi-file edits to minimize build overhead.

### 10. `genexus_create_object`

**Purpose**: Scaffolding.

- **Params**: `name`, `definition` (JSON).
- **Output**: JSON representation of the created object.
- **Tip**: Define `Attributes` and `Structure` clearly.

## 🧠 Best Practices

- **Prefer Batching**: When editing multiple objects, use `genexus_batch` to avoid repeated MSBuild startups.
- **Check Doctor**: Always check `genexus_doctor` if a build fails.
- **Use Search**: Don't guess object names; use `genexus_search` to find them.
- **Verify**: After `write_object`, use `read_object` or the returned JSON to verify changes.
