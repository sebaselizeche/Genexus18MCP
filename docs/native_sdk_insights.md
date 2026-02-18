# GeneXus Native SDK Technical Insights (v18+)

This document is a comprehensive technical reference for the GeneXus .NET SDK, documenting patterns, constants, and solutions discovered during the implementation of the GeneXus MCP Server.

## 1. Core SDK Architecture

The SDK consists of two primary assembly layers found in the GeneXus installation directory:

- **`Artech.Architecture.Common.dll`**: The base framework. Defines `KBObject`, `KBObjectPart`, `KBModel`, `IBusinessLogic`, and standard descriptors.
- **`Artech.Genexus.Common.dll`**: The GeneXus-specific layer. Defines objects like `Transaction`, `Procedure`, `Attribute`, `WebPanel`, and specialized parts like `EventsPart` or `RulesPart`.

### Implementation Tip

When building a custom worker, ensure all GeneXus DLLs (and their dependencies like `Unity.dll`, `Newtonsoft.Json.dll`) are in the application's bin directory or GAC.

---

## 2. Comprehensive GUID Dictionary

The following GUIDs are standard identifiers used by the SDK to distinguish object classes and internal parts.

### 2.1 Object Types (`ObjClass`)

| Object Type           | GUID                                   | Note                              |
| :-------------------- | :------------------------------------- | :-------------------------------- |
| **API**               | `36e32e2d-023e-4188-95df-d13573bac2e0` | API Object definition             |
| **Attribute**         | `adbb33c9-0906-4971-833c-998de27e0676` | Individual DB Attribute           |
| **Data Provider**     | `2a9e9aba-d2de-4801-ae7f-5e3819222daf` | Data retrieval logic              |
| **Domain**            | `00972a17-9975-449e-aab1-d26165d51393` | User-defined data types           |
| **Folder**            | `00000000-0000-0000-0000-000000000008` | Virtual folder in the Folder View |
| **Procedure**         | `8e0f6990-23e5-0000-0005-55ff16766446` | Server-side logic                 |
| **SDT**               | `447527b5-9210-4523-898b-5dccb17be60a` | Structured Data Type              |
| **Table**             | `857ca50e-7905-0000-0007-c5d9ff2975ec` | Internal KB Table representation  |
| **Transaction** (Web) | `1db606f2-af09-4cf9-a3b5-b481519d28f6` | **Standard for Web Apps**         |
| **Web Panel**         | `c9584656-94b6-4ccd-890f-332d11fc2c25` | Web User Interface                |

### 2.2 KBObject Parts

| Part Name         | GUID                                   | Property in SDK (C#)            |
| :---------------- | :------------------------------------- | :------------------------------ |
| **Structure**     | `264be5fb-1b28-4b25-a598-6ca900dd059f` | `p as StructurePart`            |
| **Rules**         | `9b0a32a3-de6d-4be1-a4dd-1b85d3741534` | `p as RulesPart`                |
| **Web Events**    | `c44bd5ff-f918-415b-98e6-aca44fed84fa` | `p as EventsPart` (Web Context) |
| **Win Events**    | `c414ed00-8cc4-4f44-8820-4baf93547173` | `p as EventsPart` (Win Context) |
| **Variables**     | `ae9ffd6c-972f-4ed1-8924-6f054806c57f` | `p as VariablesPart`            |
| **Web Form**      | `ad3ca970-19d0-44e1-a7b7-db05556e820c` | `p as WebFormPart`              |
| **Documentation** | `42945d02-4de0-4880-b7d8-c26be104788c` | `p as DocumentationPart`        |
| **Table Rules**   | `00000000-0000-0000-0002-000000000004` | Specific to Table entities      |

---

## 3. Data Types Mapping (`eDBType`)

When creating Attributes programmatically, the `eDBType` enum governs the underlying database representation.

| Enum Member   | Code Value | Common Usage                     |
| :------------ | :--------- | :------------------------------- |
| `NUMERIC`     | 0          | Numeric, Integer                 |
| `CHARACTER`   | 1          | Char(N)                          |
| `VARCHAR`     | 12         | VarChar(N)                       |
| `DATE`        | 2          | Date                             |
| `DATETIME`    | 3          | DateTime                         |
| `LONGVARCHAR` | 13         | LongVarChar, Text, Memo          |
| `BITMAP`      | 7          | Image, Blob                      |
| `GUID`        | 11         | Global Identifier                |
| `Boolean`     | 16         | True/False (Check casing in SDK) |

---

## 4. Crucial Discovery: The Transaction-Table Collision

One of the most elusive bugs solved was the **Name Collision** between Transactions and Tables.

### The Problem

When you create a Transaction named `Invoice`, GeneXus automatically generates a corresponding Table named `Invoice`. If your search logic iterates through the `DesignModel.Objects` collection looking for an object named "Invoice" without checking its type, it may return the **Table** object (`857c...`) instead of the **Transaction** (`1db6...`).

### The Symptom

Since Tables do not have `Events` or `WebForm` parts, any attempt to write code to these parts will return a "Part not found" error, even if you see the Transaction in the GeneXus IDE.

### The Solution: Type-Prefixed Search

Always include the object type in your search query or logic:

```csharp
// Example Logic from ObjectService.cs
if (target.StartsWith("Trn:")) {
    expectedType = ObjClass.Transaction;
    namePart = target.Substring(4);
}
// Filter by BOTH Name and Type
var obj = kb.DesignModel.Objects.FirstOrDefault(o => o.Name == name && o.Type == expectedType);
```

---

## 5. Web vs. Win Transactions

GeneXus distinguishes between different "flavors" of Transactions. By default, `new Transaction(model)` creates a general/Win transaction.

- **Web Transaction**: `TypeIdentifier = 1db606f2-af09-4cf9-a3b5-b481519d28f6`. Supports WebEvents and WebForms.
- **Win Transaction**: Supports WinEvents and WinForms.

**Resolution**: Always use `Transaction.Create(model)` or `KBObject.Create(model, ObjClass.Transaction)` to ensure you are creating a modern Web-compatible Transaction.

---

## 6. Programmatic Creation Patterns

### 6.1 Creating an Attribute

```csharp
Artech.Genexus.Common.Objects.Attribute att = new Artech.Genexus.Common.Objects.Attribute(model);
att.Name = "CustomerId";
att.Type = eDBType.NUMERIC;
att.Length = 10;
att.Save();
```

### 6.2 Modifying a Transaction Structure

To add attributes to a transaction, you must work with the `StructurePart`:

```csharp
var root = trn.Structure.Root;
var trnAtt = new TransactionAttribute();
trnAtt.Attribute = att; // The Attribute object created above
trnAtt.IsKey = true;
root.AddAttribute(trnAtt);
trn.Save();
```

---

## 7. Persistence and Cache Nuances

### 7.1 Explicit Part Saving

On first-time object creation, simply saving the `KBObject` might not persist the newly added parts.
**Pattern**:

1. Create the object.
2. Instantiate the part (`new EventsPart(obj)`).
3. Set part content (`p.Source = "..."`).
4. **Call `p.Save()`**.
5. Call `obj.Save()`.

### 7.2 Cache Invalidation

The GeneXus worker maintains a memory cache of object XMLs for performance. After any write operation via `genexus_write_object` or native SDK, the cache must be invalidated:

```csharp
_objectService.Invalidate(targetName);
```

---

## 8. Reflection Cheat Sheet

If direct API calls fail (common when dealing with dynamically loaded extensions like WorkWithPlus), use Reflection to discover and set properties.

```csharp
// Discovering parts dynamically
foreach (KBObjectPart p in obj.Parts) {
    var typeName = p.GetType().Name;
    if (typeName.Contains("WorkWithPlus")) {
        var sourceProp = p.GetType().GetProperty("Source");
        sourceProp?.SetValue(p, myCode, null);
    }
}
```

---

_Documented by Google Deepmind Advanced Coding Agent on 2026-02-18._
