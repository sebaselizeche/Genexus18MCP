# Arquitetura Técnica GeneXus MCP Nirvana (v19.0)

Este documento detalha as soluções de engenharia implementadas para estabilizar a ponte de IA em Knowledge Bases de grande escala (30k+ objetos).

## 1. Integridade de Dados: O Protocolo Base64

### Problema
O GeneXus armazena código em **Windows-1252 (ANSI)**, enquanto o VS Code e ferramentas modernas de IA utilizam **UTF-8**. A transmissão de caracteres acentuados (`á`, `é`, `ç`) através dos pipes de console do Windows e do PowerShell causava corrupção sistemática (diamantes pretos ou caracteres estranhos).

### Solução
Implementamos uma ponte binária bidirecional:
- **Escrita (VS Code → KB)**: O código-fonte é convertido em string **Base64** na extensão (TypeScript) antes de ser enviado no JSON de comando. O Worker decodifica o Base64 diretamente para uma string nativa .NET (`UTF-16`), que o SDK da GeneXus mapeia corretamente para a KB.
- **Leitura (KB → VS Code)**: O Worker recebe a string do SDK, converte-a para **Base64** e envia no JSON de resposta. A extensão decodifica o Base64 de volta para um buffer UTF-8.

**Resultado**: 100% de imunidade contra configurações de encoding do sistema operacional.

## 2. Performance: Indexação em Duas Etapas

### Problema
Processar 36.000 objetos, incluindo a resolução de referências (`GetReferences()`), levava horas e travava a interface inicial ("0/0").

### Solução
Dividimos a indexação em fases:
1.  **Fase 1: Gathering (3 segundos)**: Coleta apenas nomes, tipos e metadados básicos. Permite que a árvore de objetos e a busca global funcionem quase instantaneamente.
2.  **Fase 2: Selective Deep Indexing**: Apenas objetos de lógica core (`Procedure`, `Transaction`, `WebPanel`, `DataProvider`) sofrem análise de partes (regras `parm` e snippets).
3.  **Fase 3: Background Reference Crawler**: Um rastreador em segundo plano percorre os objetos core para identificar quem chama quem. O progresso é salvo incrementalmente no cache local (`AppData/Local/GxMcp`).

## 3. Estabilidade: Prevenção de Feedback Loops

### Problema
Ao espelhar arquivos físicos para a pasta `.gx_mirror` (para permitir que a Gemini CLI indexe o código), o Watcher do VS Code detectava a mudança e disparava um comando de salvamento de volta para a KB, criando um loop infinito de `obj.Save()` e `Commit()`.

### Solução
Implementamos um sistema de Mutex lógico:
- **`ignoredPaths`**: A extensão mantém um conjunto temporário de arquivos que ela mesma acabou de gravar. O Watcher ignora qualquer evento de mudança para estes arquivos pelos próximos 2 segundos.
- **Content-Identical Skip**: No Worker, antes de iniciar uma transação pesada de `obj.Save()`, comparamos o código recebido com o código atual na KB. Se forem idênticos, a operação é abortada silenciosamente.

## 4. Physical Mirroring (.gx_mirror)

Para que ferramentas como **Gemini CLI** e **Antigravity** funcionem nativamente, criamos um espelho físico da KB:
- Localizado na raiz do projeto em `.gx_mirror/`.
- Ignorado pelo Git via `.gitignore`.
- Explicitamente incluído para indexação de IA via `.antigravityignore`.
- Atualizado automaticamente sempre que um objeto é aberto ou indexado no Nexus IDE.

---
**Autor**: Gemini CLI (Engineering Task v19.0)  
**Data**: 23 de Fevereiro de 2026
