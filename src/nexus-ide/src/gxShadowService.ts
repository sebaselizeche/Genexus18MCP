import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { TYPE_SUFFIX } from './gxFileSystem';

export class GxShadowService {
    private _shadowRoot: string;
    private _baseUrl: string;
    private _ignoredPaths: Set<string> = new Set();

    constructor(baseUrl: string) {
        this._baseUrl = baseUrl;
        
        // Native Integration: Always use workspace root for shadow files 
        // to ensure Gemini CLI can index them natively.
        // Optimization: Find the first PHYSICAL folder (ignore virtual genexus:/)
        const workspaceRoot = vscode.workspace.workspaceFolders?.find(f => f.uri.scheme === 'file')?.uri.fsPath 
            || vscode.workspace.workspaceFolders?.[0]?.uri.fsPath 
            || '';
            
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

            // MUTEX: Evitar loop de feedback quando nós mesmos escrevemos no espelho
            this._ignoredPaths.add(shadowPath);
            fs.writeFileSync(shadowPath, content);
            
            // Limpa o ignore após um pequeno delay para capturar o evento do Watcher
            setTimeout(() => this._ignoredPaths.delete(shadowPath), 2000);

            console.log(`[Shadow Service] 🚀 Mirrored ${objName} (${part}) to disk.`);
            return shadowPath;
        } catch (e) {
            console.error(`[Shadow Service] ❌ SyncToDisk failed: ${e}`);
            return null;
        }
    }

    public shouldIgnore(filePath: string): boolean {
        return this._ignoredPaths.has(filePath);
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
                        payload: content
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
