import * as vscode from 'vscode';
import * as http from 'http';

export class GxFileSystemProvider implements vscode.FileSystemProvider {
    private _emitter = new vscode.EventEmitter<vscode.FileChangeEvent[]>();
    readonly onDidChangeFile: vscode.Event<vscode.FileChangeEvent[]> = this._emitter.event;

    private readonly baseUrl = 'http://localhost:5000/api/command';
    private _filePartState = new Map<string, string>(); // Maps uri.path -> partName
    private _contentCache = new Map<string, Uint8Array>(); // Holds content AFTER a save or latest read from Worker
    private _mtimes = new Map<string, number>(); // Stores mtime per URI
    private _readCache = new Map<string, { data: Uint8Array, time: number }>(); // Read cache from Worker for performance
    private _pendingReadRequests = new Map<string, Promise<Uint8Array>>();
    private _dirCache = new Map<string, { entries: [string, vscode.FileType][], time: number }>();
    private readonly VALID_TYPES = new Set(['Procedure', 'Transaction', 'WebPanel', 'DataProvider', 'Attribute', 'Table', 'DataView', 'SDPanel']);

    public setPart(uri: vscode.Uri, partName: string) {
        console.log(`[Nexus IDE] setPart: Setting ${uri.path} to ${partName}`);
        this._filePartState.set(uri.path, partName);
        this._readCache.delete(this.getCacheKey(uri, partName)); // Invalidate specific part cache
        this._contentCache.delete(uri.toString()); // Invalidate shadowing cache for this URI

        // Note: We DON'T update mtime here anymore to avoid triggering VS Code's "file changed on disk" 
        // while the user is simply switching views. VS Code will call readFile and we will return
        // the new part content.
        this._emitter.fire([{ type: vscode.FileChangeType.Changed, uri }]); // Notify VS Code of change
    }

    public getPart(uri: vscode.Uri): string {
        const part = this._filePartState.get(uri.path) || 'Source';
        return part;
    }

    private getCacheKey(uri: vscode.Uri, partName: string): string {
        return uri.toString() + "?" + partName;
    }

    public async initKb() {
        console.log("[Nexus IDE] Warming up KB...");
        return this.callGateway({ method: "execute_command", params: { module: 'KB', action: 'Initialize' } });
    }

    watch(_uri: vscode.Uri, _options: { recursive: boolean; excludes: string[] }): vscode.Disposable {
        return new vscode.Disposable(() => {});
    }

    stat(uri: vscode.Uri): vscode.FileStat {
        const path = decodeURIComponent(uri.path.substring(1));
        
        if (path.startsWith('.') || path.includes('tasks.json') || path.includes('settings.json') || path.includes('launch.json') || path.includes('pom.xml')) {
            throw vscode.FileSystemError.FileNotFound(uri);
        }

        if (path === '' || !path.endsWith('.gx')) {
            return { type: vscode.FileType.Directory, ctime: Date.now(), mtime: Date.now(), size: 0 };
        }

        const cachedContent = this._contentCache.get(uri.toString());
        const size = cachedContent ? cachedContent.byteLength : 1024; 

        // Important: If mtime doesn't exist, we MUST set it once.
        // But we MUST NOT update it on every read/stat if the file hasn't actually been written to.
        if (!this._mtimes.has(uri.toString())) {
            this._mtimes.set(uri.toString(), Date.now()); 
        }

        return {
            type: vscode.FileType.File,
            ctime: Date.now(), 
            mtime: this._mtimes.get(uri.toString())!,
            size: size 
        };
    }

    async readDirectory(uri: vscode.Uri): Promise<[string, vscode.FileType][]> {
        const path = decodeURIComponent(uri.path.substring(1));
        const parentName = path === '' ? 'Root Module' : path.split('/').pop()!;
        const cacheKey = `dir:${parentName}`;
        
        const cached = this._dirCache.get(cacheKey);
        if (cached && (Date.now() - cached.time < 60000)) return cached.entries;

        try {
            const result = await this.callGateway({
                method: "execute_command",
                params: { module: 'Search', query: `parent:"${parentName}"`, limit: 5000 }
            });

            const objects = result.results || (Array.isArray(result) ? result : []);
            if (Array.isArray(objects)) {
                const mapped = objects.map((obj: any) => {
                    const isDir = obj.type === 'Folder' || obj.type === 'Module';
                    return [
                        isDir ? obj.name : `${obj.name}.gx`,
                        isDir ? vscode.FileType.Directory : vscode.FileType.File
                    ];
                }) as [string, vscode.FileType][];

                this._dirCache.set(cacheKey, { entries: mapped, time: Date.now() });
                return mapped;
            }
        } catch (e) {
            console.error(`[Nexus IDE] readDirectory error for ${parentName}:`, e);
        }
        return [];
    }

    async readFile(uri: vscode.Uri): Promise<Uint8Array> {
        const path = decodeURIComponent(uri.path.substring(1));
        const partName = this.getPart(uri);
        const cacheKey = this.getCacheKey(uri, partName);
        
        if (this._contentCache.has(uri.toString())) {
            return this._contentCache.get(uri.toString())!;
        }

        const cached = this._readCache.get(cacheKey);
        if (cached && (Date.now() - cached.time < 30000)) {
            return cached.data;
        }

        if (this._pendingReadRequests.has(cacheKey)) {
            return this._pendingReadRequests.get(cacheKey)!;
        }

        const request = (async () => {
            const parts = path.split('/');
            const fileName = parts[parts.length - 1];
            const objName = fileName.replace('.gx', '');
            
            try {
                // In hierarchical view, we don't always know the type from the path easily,
                // so we let the backend find it by name.
                const result = await this.callGateway({
                    method: "execute_command",
                    params: { module: 'Read', action: 'ExtractSource', target: objName, part: partName }
                });
                let data: Uint8Array;
                if (result && result.source) data = Buffer.from(result.source, 'utf8');
                else if (partName === 'Variables' && result.variables) data = Buffer.from(result.variables.map((v: any) => `&${v.name} : ${v.type}(${v.length})`).join('\n'), 'utf8');
                else data = Buffer.from("// Part not available", 'utf8');
                
                this._readCache.set(cacheKey, { data, time: Date.now() });
                this._contentCache.set(uri.toString(), data);
                return data;
            } catch (error) { 
                return Buffer.from(`// Error reading part: ${error}`, 'utf8'); 
            } finally { 
                this._pendingReadRequests.delete(cacheKey); 
            }
        })();
        this._pendingReadRequests.set(cacheKey, request);
        return request;
    }

    writeFile(uri: vscode.Uri, content: Uint8Array, options: { create: boolean; overwrite: boolean }): Promise<void> {
        return this._writeFile(uri, content, options);
    }

    async _writeFile(uri: vscode.Uri, content: Uint8Array, _options: { create: boolean; overwrite: boolean }): Promise<void> {
        const path = decodeURIComponent(uri.path.substring(1));
        const parts = path.split('/');
        const objName = parts[parts.length - 1].replace('.gx', '');
        const partName = this.getPart(uri);
        const source = Buffer.from(content).toString('utf8');

        this._contentCache.set(uri.toString(), content);
        this._mtimes.set(uri.toString(), Date.now());

        try {
            const result = await this.callGateway({
                method: "execute_command",
                params: { module: 'Write', target: objName, action: partName, payload: source }
            });
            if (result && (result.error || result.status === 'Error')) throw new Error(result.error || 'SDK Error');
            
            // On successful save, we keep the content in cache and update mtime
            this._contentCache.set(uri.toString(), content);
            this._mtimes.set(uri.toString(), Date.now());
            
            vscode.window.setStatusBarMessage(`$(check) Saved ${objName}`, 5000);
        } catch (err) {
            vscode.window.showErrorMessage(`Save Error: ${err}`);
            throw err;
        } finally {
            // We increase the timeout or remove it to ensure VS Code doesn't re-read stale data from gateway
            // before the gateway's own cache is fully flushed.
            setTimeout(() => this._contentCache.delete(uri.toString()), 5000);
        }
    }

    public async triggerSave(uri: vscode.Uri, content: Uint8Array): Promise<void> {
        return this._writeFile(uri, content, { create: false, overwrite: true });
    }

    public async callGateway(command: any): Promise<any> {
        return new Promise((resolve, reject) => {
            const data = JSON.stringify(command);
            const req = http.request(this.baseUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Content-Length': data.length },
                timeout: 30000
            }, (res) => {
                let body = '';
                res.on('data', (chunk) => body += chunk);
                res.on('end', () => { try { resolve(JSON.parse(body)); } catch { resolve(body); } });
            });
            req.on('timeout', () => { req.destroy(); reject(new Error("Timeout Gateway (15s)")); });
            req.on('error', reject);
            req.write(data);
            req.end();
        });
    }

    public clearDirCache(): void {
        this._dirCache.clear();
        this._emitter.fire([{ type: vscode.FileChangeType.Changed, uri: vscode.Uri.parse('genexus:/') }]);
    }

    createDirectory(): void { throw vscode.FileSystemError.NoPermissions('Nexus IDE: Creation of folders is not supported yet.'); }
    delete(): void { throw vscode.FileSystemError.NoPermissions('Nexus IDE: Deletion is not supported yet.'); }
    rename(): void { throw vscode.FileSystemError.NoPermissions('Nexus IDE: Rename is not supported yet.'); }
}
