# Protocolo GeneXus MCP Nirvana (Sentient Edition v18.7)

> [!IMPORTANT]
> **Skills Required**: Before performing ANY task in this repo, the agent MUST load and follow:
>
> 1. `[GeneXus MCP Mastery](file:///.gemini/skills/genexus-mastery/SKILL.md)` - For tool performance and cache usage.
> 2. `[GeneXus 18 Guidelines](file:///.gemini/skills/genexus18-guidelines/SKILL.md)` - For official GeneXus development rules.

## 🧠 Intelligence: Smart Variable Injection (v18.7)
The MCP now automatically handles variables in `genexus_write_object`. You do NOT need to create variables manually for common patterns:

1.  **Attribute Inheritance**: If you use `&ClienteId`, the MCP searches for the attribute `ClienteId` and copies its Type, Length, and Decimals.
2.  **Semantic Heuristics**:
    *   `Data`, `Dt`, `Emissao` → `Date`
    *   `Hora`, `Moment`, `Timestamp` → `DateTime`
    *   `Is...`, `Has...`, `Flg...`, `Ativo` → `Boolean`
    *   `Valor`, `Preco`, `Total`, `Id`, `Seq` → `Numeric` (Auto-decimals for money)
3.  **Fallback**: Defaults to `VarChar(100)` if no pattern is matched.

## [Tools] Tool Usage Guide (Nirvana Optimized)

### 1. `genexus_validate` (Pre-save)
**Purpose**: Surgical syntax check.
- **Usage**: Call before `write_object` to ensure the logic is sound. Returns SDK diagnostics.
- **Params**: `name`, `part`, `code`.

### 2. `genexus_test` (GXtest Integration)
**Purpose**: Run Unit Tests and get real-time feedback.
- **Mechanism**: Executes via MSBuild/Abstracta Runner Task.
- **Output**: Returns "Success" or "Failed" with the full execution log and assertion results.

### 3. `genexus_scaffold` (Object Factory)
**Purpose**: Create new objects from templates.
- **Templates**: Supports `Procedure` (Prc) and `Transaction` (Trn).
- **Usage**: `genexus_scaffold(type='Prc', name='MyNewProc', properties={ 'description': '...', 'code': '...' })`.

### 4. `genexus_analyze` (Linter & Semantic Intelligence)
- **Features**: Case-insensitive, checks complexity, identifies `COMMIT` in loops, and maps object hierarchy.
- **Rules**:
    *   🚫 **Commit in Loop**: Critical performance/locking check (ignores comments).
    *   ⚠️ **Unfiltered Loop**: Scans for `For Each` without `Where`.
    *   ℹ️ **Parm Rule**: Warns if a Procedure/WebPanel lacks parameters in `Rules`.
    *   ℹ️ **New Duplicate**: Suggests `When Duplicate` handling.

### 5. `genexus_read_source` / `genexus_write_object`
- **Native**: Direct manipulation of the GeneXus Object Model. Supports Rules, Events, Source, and Variables.

### 6. `genexus_get_data_context`
- **Deep Insight**: Returns Table structure including **Subtypes**, **Indices**, and **Formulas**. Essential for understanding data relationships.

---

## [Workflow] "Nirvana" Zero-IDE Workflow

| Step | Action | Tool |
| :--- | :--- | :--- |
| **1. Design** | Create new object structure | `genexus_scaffold` |
| **2. Edit** | Write business logic | `genexus_write_object` |
| **3. Verify** | Check syntax & logic | `genexus_validate` |
| **4. Build** | Compile the object | `genexus_build(action='Build', target='...')` |
| **5. Test** | Run Unit Tests | `genexus_test(name='...')` |

---

## [Intel] Intelligence & Best Practices (GX18 Special)

- **One-Line JSON Protocol**: All communication between processes is minified to prevent pipe hangs.
- **Direct GUID Access**: Accessing parts via GUID bypasses the slow UI lazy-loading.
- **Prefix Intelligence**: Use prefixes for precision: `Prc:Name`, `Trn:Name`, `Wp:Name`.
- **Offline Mode**: The motor defaults to offline to prevent hangs waiting for GXserver.

## [Debt] Dívida Técnica & Limitações do MCP (Solved in v18.7)

Durante a evolução do projeto, superamos os principais bloqueadores de autonomia:

1.  **Gestão de Variáveis (Resolvido)**: O sistema agora injeta automaticamente variáveis detectadas no código via `VariableInjector` com inferência de tipo.
2.  **Dependências de Tabelas (Resolvido)**: O `TableDependencyInjector` força a ancoragem de tabelas necessárias.
3.  **Feedback de Validação (Resolvido)**: `genexus_validate` permite correções precisas antes do save.
4.  **Estabilidade do Worker**: Monitorado via Heartbeat e execução em STA thread dedicada.
