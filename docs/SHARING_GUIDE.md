# 🚀 Guia de Compartilhamento: Nexus IDE para GeneXus

Este guia explica como instalar e configurar o **Nexus IDE**, transformando seu VS Code em um mini-editor GeneXus com suporte a IA (Copilot/Claude).

## 1. Instalação

1. Baixe o arquivo `nexus-ide.vsix`.
2. No VS Code, vá em **Extensions** (Ctrl+Shift+X).
3. Clique em `...` (Views and More Actions) -> **Install from VSIX...**.
4. Selecione o arquivo baixado.

## 2. Configuração Inicial (Zero-Config)

O Nexus IDE tentará localizar sua KB e o GeneXus 18 automaticamente:

- **KB**: Identificada por arquivos `.gxw` na sua pasta de trabalho aberta.
- **GeneXus**: Detectado automaticamente no caminho padrão.

Se tudo estiver correto, o ícone do **GX** aparecerá e o backend iniciará sozinho. Caso precise ajustar manualmente:

1. Abra as **Settings** (`Ctrl+,`) e procure por `GeneXus`.
2. Ajuste o **Kb Path** ou **Installation Path** se necessário.
3. **Reinicie o VS Code** após alterações manuais.

## 3. Uso do Explore (KB Explorer)

- Clique no ícone do **GX** na barra lateral esquerda.
- O backend iniciará automaticamente e fará o índice da sua KB.
- Você poderá navegar por arquivos `.gx` e editá-los diretamente.

## 4. Integração com Copilot / Claude

Para que o Copilot ou Claude consigam ler sua KB:

1. Pressione `Ctrl+Shift+P`.
2. Digite e selecione: **"GeneXus: Copy MCP Config for Copilot/Claude"**.
3. No **Claude Desktop**: Abra o arquivo de configuração e cole o trecho copiado em `mcpServers`.
4. No **VS Code Copilot**: Adicione o trecho nas configurações de MCP do GitHub Copilot.

---

_Dica: Use as abas no topo do editor para alternar entre Source, Rules, Events e Variables._
