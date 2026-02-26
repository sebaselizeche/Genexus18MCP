import * as vscode from "vscode";

export class GxCacheManager {
  public filePartState = new Map<string, string>();
  public contentCache = new Map<string, Uint8Array>();
  public mtimes = new Map<string, number>();
  public readCache = new Map<string, { data: Uint8Array; time: number }>();
  public pendingReadRequests = new Map<string, Promise<Uint8Array>>();
  public dirCache = new Map<string, { entries: [string, vscode.FileType][]; time: number }>();
  public partsCache = new Map<string, Map<string, Uint8Array>>();
  public metadataCache = new Map<string, string>();

  public clearDirectoryCache() {
    this.dirCache.clear();
  }

  public invalidatePartCache(uriStr: string, partName: string) {
    this.readCache.delete(uriStr + "?" + partName);
    this.contentCache.delete(uriStr);
  }

  /**
   * Invalida o cache após uma escrita com sucesso.
   * Além do arquivo, invalida o diretório pai para refletir mudanças estruturais.
   */
  public commitWrite(uri: vscode.Uri, partName: string) {
    const uriStr = uri.toString();
    
    // 1. Limpa caches de leitura do arquivo
    this.readCache.delete(uriStr + "?" + partName);
    this.contentCache.delete(uriStr);
    this.partsCache.delete(uriStr); // Força recarregar todas as partes se uma mudou

    // 2. Invalida cache do diretório pai
    const pathParts = uri.path.split("/");
    if (pathParts.length > 1) {
      pathParts.pop();
      const parentPath = pathParts.join("/");
      this.dirCache.delete(`dir:${parentPath}`);
      this.dirCache.delete(`dir:${parentPath.substring(1)}`); // Tenta ambas as variações de barra
    }

    // 3. Atualiza mtime para forçar o editor a recarregar se necessário
    this.mtimes.set(uriStr, Date.now());
  }

  public getCacheKey(uri: vscode.Uri, partName: string): string {
    return uri.toString() + "?" + partName;
  }
}
