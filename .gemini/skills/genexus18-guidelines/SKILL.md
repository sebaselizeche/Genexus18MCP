---
name: GeneXus 18 Guidelines
description: Core directives, architectural patterns, and development best practices for building robust applications with GeneXus 18.
---

# 📘 GeneXus 18 Development Guidelines

This skill provides the core directives and best practices for development in GeneXus 18. It serves as a static knowledge base for agents to ensure compliant and high-quality contributions to the Knowledge Base (KB).

## 🛡️ 1. Security First (The GAM Standard)

GeneXus Access Manager (GAM) is the mandatory standard for security in GeneXus 18.

- **Enable GAM**: Always ensure GAM is enabled for centralized Authentication and Authorization.
- **Centralized Identity**: Use Identity Providers for password centralization.
- **Standard Protocols**: Prefer OAuth 2.0 for third-party integrations.
- **Encryption**: Use built-in GeneXus functions for sensitive data encryption/decryption.
- **Audit**: Implement audit logs for critical data modifications (Business Component 'After Trn' events).

## ⚡ 2. Performance & Scalability

- **Base Table Navigation**: Always filter `For Each` loops using `Where` or `Defined By` to avoid Full Table Scans.
- **Caching**: Utilize Data Provider and Procedure caching for expensive, read-heavy operations.
- **Data Types**: Use efficient types:
  - `GUID`: For unique identifiers (better than autoincremental for distributed systems).
  - `Geography`: For spatial data.
- **Compilation**: Ensure the environment uses JDK 11+ and Gradle for parallelized Java compilation.
- **Lazy Loading**: For UI components, use paging and on-demand data loading for large grids.

## 🏗️ 3. Architectural Patterns & Maintainability

- **Modularization**: Organize the KB using **Modules**. Avoid keeping everything in the Root module.
- **Design System Object (DSO)**: Use DSOs to centralize styles. Avoid hardcoded CSS or inline layouts.
- **Business Components (BC)**: Always use BCs for data manipulation (Insert/Update/Delete) to ensure Transaction Rules and Referencial Integrity are respected.
- **Patterns**: Leverage standard patterns like **Work With Plus (WWP)** to maintain UI consistency and speed up development.

## ✍️ 4. Clean Code & Conventions

- **Naming**:
  - `Prc`: Procedures
  - `Trn`: Transactions
  - `Wbp`: Web Panels
  - `Dta`: Data Providers
  - `SDT`: Structured Data Types
- **Self-Documenting Code**: Use descriptive variable names (e.g., `&IsCustomerActive` instead of `&Flg`).
- **Rule Management**: Keep Transaction rules concise. Complex logic should be encapsulated in Procedures called from the rules.
- **Error Handling**: Use `Error_Handler` and `When Duplicate` clauses in `New` commands.

## ⚙️ 5. GeneXus Server & DevOps

- **Frequent Commits**: Commit changes to **GeneXus Server** frequently with meaningful comments.
- **CI/CD**: Integrate with GeneXus Server MSBuild tasks for automated testing (GXtest) and deployment.
- **Versioning**: Use branches in GeneXus Server for feature isolation before merging into the Trunk.

## 🚫 6. Core Anti-Patterns (The "Don'ts")

- **NO** `Commit` inside loops (Breaks LUW and locks DB).
- **NO** `Sleep` or `Wait` in Web Panels (Blocks IIS/Webserver threads).
- **NO** Hardcoded URLs (Use Location objects or Environment settings).
- **NO** Direct SQL commands if a `For Each` or `Business Component` can achieve the same (Breaks DB abstraction).
