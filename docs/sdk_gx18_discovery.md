# GeneXus 18 SDK: Guia Definitivo de Engenharia Reversa e MCP (v18.7)

Este documento detalha as descobertas críticas feitas durante a estabilização do MCP para o GeneXus 18, cobrindo desde o bootstrapping do motor até otimizações de performance para KBs de grande porte (30k+ objetos).

---

## 1. Arquitetura de Inicialização (Bootstrapping)

O GeneXus 18 abandonou a inicialização descentralizada de serviços. O motor agora depende de um processo de "Boot" via `Connector.dll`.

### 1.1 Sequência Obrigatória de Chamadas
A ordem abaixo é vital. Inverter ou omitir qualquer passo resulta em `NullReferenceException` ou travamento da thread.

1.  **Desativação de UI**: `Artech.Architecture.UI.Framework.Services.UIServices.SetDisableUI(true)`. 
    - *Por que:* Evita que o SDK tente carregar componentes de vídeo ou diálogos de rede que não existem no contexto de console.
2.  **Connector Initialize**: `Artech.Core.Connector.Initialize()`.
    - *O que faz:* Prepara o carregador de pacotes e resolve as dependências das extensões instaladas.
3.  **Connector Start**: `Artech.Core.Connector.Start()`.
    - *O que faz:* Ativa o `GxServiceManager` central. Sem isso, propriedades como `.Current` de quase todos os serviços retornam `null`.
4.  **Linkagem de Factory**: Se `KnowledgeBase.KBFactory` for nulo, atribua:
    ```csharp
    KnowledgeBase.KBFactory = new Connector.KBFactory();
    ```
5.  **Model Initializer**: `Artech.Genexus.Common.KBModelObjectsInitializer.Initialize()`.
    - *O que faz:* Registra os tipos de objetos (`Procedure`, `Transaction`, etc.) no modelo de dados.

---

## 2. Manipulação de Objetos e Partes (Otimização de Performance)

### 2.1 O Problema do Lazy Loading
Iterar sobre `obj.Parts` via `foreach` é extremamente lento (2 a 3 minutos por objeto). O SDK tenta carregar metadados de editores, ícones e dependências visuais para cada parte.

### 2.2 Solução: Acesso Direto via GUID
A única forma de ler código de forma instantânea é usando o GUID do tipo da parte. No GeneXus 18, a propriedade de comparação é **`.Type`**.

| Parte | GUID (GeneXus 18) |
| :--- | :--- |
| **Procedure (Source)** | `528d1c06-a9c2-420d-bd35-21dca83f12ff` |
| **Rules** | `9b0a32a3-de6d-4be1-a4dd-1b85d3741534` |
| **Events** | `c44bd5ff-f918-415b-98e6-aca44fed84fa` |
| **Variables** | `e4c4ade7-53f0-4a56-bdfd-843735b66f47` |

---

## 3. Estabilidade do Ambiente e Comunicação

### 3.1 Arquitetura de Thread STA (Single-Threaded Apartment)
O SDK do GeneXus é baseado em COM e é **extremamente sensível** a trocas de threads.
- **Descoberta**: Tentar processar comandos via `ThreadPool` causava deadlocks aleatórios de 30s a 2min.
- **Solução**: O Worker agora opera em uma **Thread STA única**. Os comandos são lidos por uma thread de background e enfileirados para a thread principal (STA), que é a única que toca no SDK.

### 3.2 Protocolo de Linha Única (Minified JSON)
O Gateway utiliza `ReadLine()` para capturar as respostas do Worker. 
- **Regra de Ouro**: O Worker **NUNCA** deve enviar JSON formatado (com quebras de linha). 
- **Implementação**: Use sempre `JsonConvert.SerializeObject(obj, Formatting.None)`. Se o JSON tiver quebras de linha, o Gateway lerá apenas o primeiro fragmento, causará um erro de parse e o cliente receberá um timeout.

---

## 4. Estratégia de Busca (Discovery Engine)

Em KBs com 30.000+ objetos, a busca nativa do SDK (`GetByName`) pode ser lenta se o tipo não for especificado.
1.  **Índice Local**: Use o comando `genexus_bulk_index` para gerar o `SearchIndex.json`.
2.  **Busca Semântica**: O MCP consulta o JSON (milissegundos) para extrair o Nome e o Tipo.
3.  **Assinatura GX18**: O método `kb.DesignModel.Objects.GetByName` exige 3 argumentos: `(null, null, name)`. Inverter ou omitir trava a execução.

---
*Este documento consolida o conhecimento técnico para manter o motor estável e performático.*
