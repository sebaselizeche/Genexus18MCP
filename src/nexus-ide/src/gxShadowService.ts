import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as crypto from 'crypto';
import { TYPE_SUFFIX } from './gxFileSystem';

export class GxShadowService {
    private _shadowRoot: string;
    private _baseUrl: string;
    private _fileHashes: Map<string, string> = new Map();
    private readonly MAX_HASHES = 500; // Proteção contra vazamento de memória

    constructor(baseUrl: string) {
        this._baseUrl = baseUrl;
        
        let workspaceRoot = vscode.workspace.workspaceFolders?.find(f => f.uri.scheme === 'file')?.uri.fsPath;
        
        // Seletion Fallback: Se estivermos em um workspace puramente virtual (genexus:/),
        // procuramos a raiz física real caminhando para cima a partir da extensão.
        if (!workspaceRoot || workspaceRoot.startsWith('genexus')) {
            let current = __dirname;
            while (current !== path.dirname(current)) {
                if (fs.existsSync(path.join(current, '.git')) || fs.existsSync(path.join(current, 'Genexus18MCP.sln'))) {
                    workspaceRoot = current;
                    break;
                }
                current = path.dirname(current);
            }
            if (!workspaceRoot) workspaceRoot = process.cwd();
        }
            
        this._shadowRoot = path.join(workspaceRoot, '.gx_mirror');
        
        if (!fs.existsSync(this._shadowRoot)) {
            fs.mkdirSync(this._shadowRoot, { recursive: true });
        }
    }

    /**
     * Sincroniza um objeto da KB para o disco (.gx_shadow/)
     */
    public async syncToDisk(uri: vscode.Uri, content: Uint8Array, part: string): Promise<string | null> {
        try {
            if (!this._shadowRoot) return null;

            const pathParts = decodeURIComponent(uri.path.substring(1)).split('/');
            if (pathParts.length < 2) return null;

            const type = pathParts[0];
            const fileName = pathParts[pathParts.length - 1];
            const objName = fileName.replace(/\.gx$/, '').split('.')[0]; // Only name, no suffix

            const typeDir = path.join(this._shadowRoot, type);
            if (!fs.existsSync(typeDir)) fs.mkdirSync(typeDir, { recursive: true });

            // File naming: Name.[Part].gx (or just Name.gx if Source)
            const cleanPart = part === 'Source' ? '' : `.${part}`;
            const shadowFileName = `${objName}${cleanPart}.gx`;
            const shadowPath = path.join(typeDir, shadowFileName);

            // MUTEX: Calcula o hash do conteúdo que estamos salvando para evitar loop de feedback
            const hash = crypto.createHash('sha256').update(content).digest('hex');
            
            // Gestão de Memória: Pruning do Map se exceder o limite
            if (this._fileHashes.size >= this.MAX_HASHES) {
                const firstKey = this._fileHashes.keys().next().value;
                if (firstKey) this._fileHashes.delete(firstKey);
            }
            this._fileHashes.set(shadowPath, hash);
            
            // ESCRITA ATÔMICA: Salva em arquivo temporário e renomeia
            const tmpPath = `${shadowPath}.tmp`;
            fs.writeFileSync(tmpPath, content);
            fs.renameSync(tmpPath, shadowPath);

            console.log(`[Shadow Service] 🚀 Mirrored ${objName} (${part}) to disk.`);
            return shadowPath;
        } catch (e) {
            console.error(`[Shadow Service] ❌ SyncToDisk failed: ${e}`);
            return null;
        }
    }

    public shouldIgnore(filePath: string): boolean {
        if (!fs.existsSync(filePath)) return true;
        
        try {
            const content = fs.readFileSync(filePath);
            const currentHash = crypto.createHash('sha256').update(content).digest('hex');
            const expectedHash = this._fileHashes.get(filePath);
            
            // Se o hash for idêntico ao último que NÓS salvamos, ignoramos o evento
            if (currentHash === expectedHash) {
                return true;
            }
            
            // Se o hash for diferente (modificado por IA ou usuário externamente), atualizamos o registro
            // e permitimos o sync para a KB
            this._fileHashes.set(filePath, currentHash);
            return false;
        } catch (e) {
            return false;
        }
    }

    /**
     * Sincroniza uma mudança no disco (.gx_shadow/) de volta para a KB
     */
    public async syncToKB(filePath: string): Promise<void> {
        try {
            const content = fs.readFileSync(filePath, 'utf8');
            const relativePath = path.relative(this._shadowRoot, filePath);
            const parts = relativePath.split(path.sep);

            if (parts.length < 2) return;

            const type = parts[0];
            const fileName = parts[1];
            
            // Parse name and part from filename (e.g. MyProc.Rules.gx)
            const fileNoExt = fileName.replace(/\.gx$/, '');
            const dotParts = fileNoExt.split('.');
            const objName = dotParts[0];
            const partName = dotParts.length > 1 ? dotParts[1] : 'Source';

            console.log(`[Shadow Service] 💾 Disk -> KB Sync: ${objName} (Part: ${partName})`);

            // ENCODING FIX: Convert content to Base64 to prevent Windows-1252 corruption in the Worker
            const base64Content = Buffer.from(content, 'utf8').toString('base64');

            // Use fetch to call Gateway Write command
            const response = await fetch(this._baseUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    method: 'execute_command',
                    params: {
                        module: 'Write',
                        target: objName,
                        action: partName,
                        payload: base64Content,
                        shadowPath: this._shadowRoot
                    }
                })
            });

            if (!response.ok) {
                const errorBody = await response.text();
                throw new Error(`Gateway returned ${response.status}: ${errorBody}`);
            }
        } catch (e) {
            console.error(`[Shadow Service] ❌ SyncToKB failed for ${filePath}: ${e}`);
            vscode.window.showErrorMessage(`Shadow Sync Error: ${e}`);
        }
    }

    public get shadowRoot(): string { return this._shadowRoot; }
}
