# GeneXus Linter Rules & Documentation Alignment

## 1. Commit inside Loop (Critical)

- **Concept**: Logical Unit of Work (LUW).
- **Why**: GeneXus manages transactions automatically. Forcing a `Commit` inside a `For each` breaks the cursor and can cause "Cursor not open" errors or severe database locking/contention.
- **Docs**: "It is recommended to incorrectly use the Commit command... effectively ending the LUW."

## 2. Unfiltered Loop (Critical)

- **Concept**: Base Table Navigation.
- **Why**: A `For each` without a `Where` or `Defined by` implies a Full Table Scan of the base table. In large tables (DTOs), this is a performance killer.

## 3. Sleep/Wait (Warning)

- **Concept**: User Experience & Threading.
- **Why**: In Web/Mobile, `Sleep` blocks the IIS thread. If 100 users do this, the server hangs.

## 4. Dynamic Call (Warning)

- **Concept**: Reference Trees.
- **Why**: `Call(&Variable)` prevents GeneXus from knowing the call tree. The object won't appear in "References" and might not be deployed correctly.

## 5. New without When Duplicate (Info)

- **Concept**: Data Integrity.
- **Why**: Inserting a record without handling the `Source` (Example: Unique Index collision) forces a crash. `When duplicate` handles it gracefully.
