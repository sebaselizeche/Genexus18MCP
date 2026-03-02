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
4.  **Turbo Bulk Index**: Selective gathering (36k objects in 3s) with deferred deep analysis.
5.  **Base64 Pipeline**: Bidirectional binary transport for source code, ensuring 100% immunity against encoding/acentuação issues.

## 🔍 Intelligence: Unified Search & Impact Analysis

1.  **Unified Search**: `genexus_list_objects` and `genexus_search` use the same heuristic engine. You can search by Name, Type, or Description.
2.  **Enriched Results**: Search results now include the **Parm Rule** and a **Code Snippet**, saving multiple "Read" calls.
3.  **Background Crawler**: Automatically analyzes object references (`usedby`) after indexing without blocking the user.
4.  **Impact Analysis**: Use `genexus_search(query="usedby:TableName")` to find every object referencing a specific table or attribute.

## 🛠️ Integrated Experience: Nexus-IDE (VS Code)

The project includes a mini IDE for VS Code (**Nexus-IDE**) that complements the MCP:

1.  **Physical Mirroring**: KB objects are exported to `.gx_mirror/` for native AI indexing (Gemini CLI/Copilot).
2.  **Anti-Loop Mutex**: Protection against feedback loops (prevents saves triggered by the extension's own mirroring).
3.  **Part Switching**: Nexus-IDE uses the same "Parts" logic as the MCP (`Source`, `Rules`, `Events`, `Variables`).
4.  **Virtual FS**: Files are accessed via `genexus:/[Type]/[Name]` in VS Code, powered by the same Gateway.
5.  **ALWAYS COMPILE**: After ANY change to C# or TypeScript code, the agent MUST run the full build (`.\build.ps1` and `npm run compile`).

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

---

## [Workflow] "Nirvana" Elite Workflow

| Step           | Action                            | Tool                                 |
| :------------- | :-------------------------------- | :----------------------------------- |
| **1. Explore** | Find objects and their signatures | `genexus_search`                     |
| **2. Impact**  | Check what else needs to change   | `genexus_search(query="usedby:...")` |
| **3. Read**    | Read specific code blocks         | `genexus_read_source(offset, limit)` |
| **4. Patch**   | Apply surgical code changes       | `genexus_patch(operation="Replace")` |
| **5. Verify**  | Run native validation & tests     | `genexus_validate` -> `genexus_test` |

---

## [Debt] Dívida Técnica & Evolução (Resolved in v19.0)

1.  **I/O Latency**: Fixed via Background Flushing.
2.  **Search Ambiguity**: Fixed via Unified Heuristic Engine.
3.  **Encoding Corruption**: Resolved via 100% Base64 Bridge.
4.  **Feedback Loops**: Prevented via Mutex ignoredPaths and identical-check.
5.  **Large KB Performance**: Fixed via Selective Gathering + Background Reference Crawler.
6.  **SDK Stability**: Fixed via PATH and Working Directory injection in Gateway.

---

## [SDK Secrets] Manipulating Genexus SDK Properties

When working directly with objects like `Attribute` or `TransactionAttribute`, keep these rules in mind:

1. **Avoid `Properties.Set()` for Complex Types**: Using `props.Set("Formula", "SUM(x)")` or `SetPropertyValue` directly with strings will fail (`Cannot convert System.String to Formula`) because the SDK expects AST objects.
2. **Safe Property Writing**: Always use the native converter:
   ```csharp
   object parsedVal = attr.GetPropertyValueFromString("PropertyName", "StringValue");
   attr.SetPropertyValue("PropertyName", parsedVal);
   ```
   For formulas specifically, if the above fails, use `Formula.Parse("your_string", attribute_context, null)`.
3. **Safe Property Reading**: For properties mapped to Enums or internal strings (like `Nullable`), it is often safer to use dedicated native properties if they exist. For example, for Nullable, use `IsNullable` on `TransactionAttribute` or `Attribute` objects.
4. **IsNullable Enum Mapping**: When using the native `IsNullable` property, it maps to an internal Enum (`0 = False/No`, `1 = True/Yes`, `2 = Compatible/Managed`).
5. **Performance: `Save()` vs `EnsureSave()`**: In large loops (like syncing a Transaction structure), prefer calling `KBObject.Save()` instead of `EnsureSave()`. `EnsureSave()` triggers full SDK re-evaluations and structural validation that can lead to massive worker timeouts and deadlocks in complex KBs. Call `EnsureSave()` only once at the end for the root object (e.g., the Transaction itself).
6. **Discovering Hidden SDK APIs**: The Genexus SDK is mostly undocumented. To find properties, constructors, or methods (e.g., how to build a Formula), always use **PowerShell Reflection** directly on the DLLs:
   ```powershell
   $asm = [Reflection.Assembly]::LoadFrom("C:\Program Files (x86)\GeneXus\GeneXus18\Artech.Genexus.Common.dll")
   $type = $asm.GetType("Artech.Genexus.Common.Objects.Formula")
   $type.GetMethods() | ForEach-Object { $_.ToString() }
   ```

## 🛠️ Utility Scripts & Debugging

The following refined scripts (located in `/scripts/sdk_reflection`) are state-of-the-art tools for GeneXus SDK exploration:

1.  **`reflect_gx_sdk.ps1`**: The primary high-fidelity reflection tool.
    - **Usage**: `.\scripts\sdk_reflection\reflect_gx_sdk.ps1 -TypeName "Artech.Genexus.Common.Objects.Table" [-Methods] [-Full] [-Json]`
    - **Features**: Displays full hierarchy (Base, Interfaces), detailed method signatures (params/returns), and Enums. Supports JSON output.
2.  **`search_sdk_member.ps1`**: Cross-DLL semantic search.
    - **Usage**: `.\scripts\sdk_reflection\search_sdk_member.ps1 "Keyword"`
    - **Features**: Scans `Common`, `Architecture`, and `Framework` DLLs for types or members matching the keyword.
3.  **`inspect_sdk_method.ps1`**: Deep dive into overloads.
    - **Usage**: `.\scripts\sdk_reflection\inspect_sdk_method.ps1 "ClassName" "MethodName"`
    - **Features**: Lists all overloads of a specific method with full parameter details (names, types, defaults).
4.  **`list_sdk_constants.ps1`**: Static field and Enum dumper.
    - **Usage**: `.\scripts\sdk_reflection\list_sdk_constants.ps1 "ClassName" [-Filter "Name"]`
    - **Features**: Extracts all public static fields or Enum values for a class. Permanent tool for discovering `PropertyId` values (e.g., from `ATT` or `TRNATTR`).
5.  **`verify_sdk_behavior.ps1`**: Prototyping and logic verification environment.
    - **Usage**: Template for executing custom C#/SDK snippets in a pre-configured environment.

## ⌨️ Shell & Automation: Anti-Mistake Protocol (v19.1)

> [!IMPORTANT]
> **CRITICAL RULE**: NEVER use the `cd` command within a `run_command` string. Always use the `Cwd` parameter. Failure to follow this is a violation of the protocol.

1.  **NO EXPLICIT `cd`**: **Never** use the `cd` command within a `run_command` string.
2.  **MANDATORY `Cwd`**: Always use the `Cwd` parameter of the tool to define the execution directory.
3.  **Command Separators**: On Windows (PowerShell), always use `;` instead of `&&`.
4.  **Junior Error Prevention**: Double-check basic commands (`dotnet`, `npm`, `powershell`). Avoid single-letter typos (e.g., `d` instead of `cd`).
5.  **Terminal Freshness**: When a task involves multiple shell steps, prioritize fresh `run_command` calls to ensure a clean environment state.
