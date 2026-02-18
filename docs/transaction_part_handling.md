# Transaction Part Handling & Force-Upgrade Logic

This document describes the specialized logic implemented in the GeneXus MCP to handle Transaction object parts, specifically addressing the creation of "ghosted" sections like `Events` and the distinction between Win and Web Transactions.

## 1. Technical Context

In GeneXus 18, Transactions can be either **Win** or **Web**.

- **Win Transactions** (older style) do not natively support the `WebEvents` part used by modern Web applications.
- **Ghosted Parts**: Some parts (like `Events` or `Rules`) may appear in the IDE but don't actually exist in the Knowledge Base (KB) until they have content. This prevents the SDK from finding them via standard lookups.

## 2. Object Type & Part GUIDs

The MCP uses the following GUIDs for identification and "force-upgrade" operations:

### Object Types

| Type            | GUID                                   |
| :-------------- | :------------------------------------- |
| Win Transaction | `857ca50e-7905-0000-0007-c5d9ff2975ec` |
| Web Transaction | `1db606f2-af09-4cf9-a3b5-b481519d28f6` |

### Part Types

| Part Name              | GUID                                   | Usage                    |
| :--------------------- | :------------------------------------- | :----------------------- |
| **Rules** (Standard)   | `9b0a32a3-de6d-4be1-a4dd-1b85d3741534` | Procedures, etc.         |
| **Rules** (Trn)        | `00000000-0000-0000-0002-000000000004` | Transactions             |
| **Events** (Standard)  | `c414ed00-8cc4-4f44-8820-4baf93547173` | Procedures               |
| **WebEvents** (Trn/WP) | `c44bd5ff-f918-415b-98e6-aca44fed84fa` | Transactions & WebPanels |

## 3. Specialized Automation Flow

To ensure the MCP can write to `Events` even on newly created Transactions, the following logic was implemented in `WriteService.cs`:

### A. Force-Upgrade Hack

If a `write_object` request is made for the `Events` part of a **Win Transaction**, the worker performs an automatic upgrade:

1. Detects the Win Type GUID.
2. Updates `obj.TypeGuid` to the Web Transaction GUID via reflection.
3. Calls `obj.Save()` immediately to persist the change in the KB.
4. This transformation enables the object to support the `WebEvents` part.

### B. Dynamic Part Lookup

Since GUIDs can occasionally vary between GeneXus versions or local environments, the worker implements `GetPartTypeByDynamicName`:

- Scans the KB's internal `PartTypes` collection via reflection.
- Matches names like "Events", "WebEvents", or "TransactionEvents".
- Retrieves the actual GUID used by that specific Knowledge Base.

### C. Multi-Stage Part Creation (Stage 4)

When standard SDK methods like `LasyGet` or `Get(Guid, true)` fail (common for ghosted parts), the worker uses **Stage 4**:

1. Locates the actual `PartType` object in the KB Design Model.
2. Calls `obj.Parts.Add(pt)` via reflection.
3. This is the most aggressive way to force the KB to materialize a part.

## 4. Troubleshooting & Best Practices

> [!TIP]
> **IDE Synchronization**: If the MCP claims a part cannot be created even after the upgrade, open the object in the GeneXus IDE, type a single character in the missing tab, and **Save**. This "materializes" the part manually and often resolves lock-up issues.

> [!IMPORTANT]
> **Locking**: The `GxMcp.Worker` process maintains a persistent connection to the KB. If schema changes aren't reflecting, restart the MCP Gateway to force a fresh SDK initialization.
