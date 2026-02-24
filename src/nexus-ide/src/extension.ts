import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as cp from 'child_process';
import { GxFileSystemProvider } from './gxFileSystem';
import { GxDocumentSymbolProvider } from './symbolProvider';
import { GxTreeProvider, GxTreeItem } from './gxTreeProvider';
import { GxDefinitionProvider } from './definitionProvider';
import { GxHoverProvider } from './hoverProvider';
import { GxCompletionItemProvider } from './completionProvider';
import { GxInlineCompletionItemProvider } from './inlineCompletionProvider';
import { GxDiagnosticProvider } from './diagnosticProvider';
import { GxReferenceProvider } from './referenceProvider';
import { GxWorkspaceSymbolProvider } from './workspaceSymbolProvider';
import { GxCodeLensProvider } from './codeLensProvider';
import { GxSignatureHelpProvider } from './signatureHelpProvider';
import { GxCodeActionProvider } from './codeActionProvider';
import { GxRenameProvider } from './renameProvider';
import { GxFormatProvider } from './formatProvider';
import { GxActionsProvider } from './gxActionsProvider';
import { TYPE_SUFFIX } from './gxFileSystem';
import { GxShadowService } from './gxShadowService';

let backendProcess: cp.ChildProcess | undefined;

async function findBestKbPath(): Promise<string> {
    const config = vscode.workspace.getConfiguration();
    let kbPath = config.get<string>('genexus.kbPath', '');
    
    if (kbPath && fs.existsSync(kbPath)) {
        return kbPath;
    }

    // Auto-discovery: Look for .gxw files in workspace
    const files = await vscode.workspace.findFiles('**/*.gxw', '**/node_modules/**', 1);
    if (files.length > 0) {
        const foundPath = path.dirname(files[0].fsPath);
        console.log(`[Nexus IDE] Auto-discovered KB at: ${foundPath}`);
        return foundPath;
    }

    return '';
}

function findBestInstallationPath(): string {
    const config = vscode.workspace.getConfiguration();
    const currentPath = config.get<string>('genexus.installationPath', '');
    
    // If it's the default or empty, check if it actually exists
    if (!currentPath || currentPath === 'C:\\Program Files (x86)\\GeneXus\\GeneXus18') {
        const defaultPath = 'C:\\Program Files (x86)\\GeneXus\\GeneXus18';
        if (fs.existsSync(path.join(defaultPath, 'GeneXus.exe'))) {
            return defaultPath;
        }
    }

    return currentPath;
}

async function startBackend(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration();
    const autoStart = config.get('genexus.autoStartBackend');
    if (!autoStart) return;

    const backendDir = path.join(context.extensionPath, 'backend');
    const gatewayExe = path.join(backendDir, 'GxMcp.Gateway.exe');
    const configFile = path.join(backendDir, 'config.json');

    if (!fs.existsSync(gatewayExe)) {
        console.log("[Nexus IDE] Backend executable not found (Development Mode?). Skipping auto-start.");
        return;
    }

    // Zero-Config: Auto-discover paths
    const kbPath = await findBestKbPath();
    const installationPath = findBestInstallationPath();

    if (!kbPath || !installationPath) {
        console.log("[Nexus IDE] Missing KB Path or Installation Path. Auto-start aborted.");
        return;
    }

    // Sync found paths to backend config.json
    if (fs.existsSync(configFile)) {
        try {
            const currentConfig = JSON.parse(fs.readFileSync(configFile, 'utf8'));
            currentConfig.GeneXus.InstallationPath = installationPath;
            currentConfig.Environment.KBPath = kbPath;
            currentConfig.Server.HttpPort = config.get('genexus.mcpPort');
            fs.writeFileSync(configFile, JSON.stringify(currentConfig, null, 2));
        } catch (e) {
            console.error("[Nexus IDE] Failed to update config.json:", e);
        }
    }

    console.log("[Nexus IDE] Starting MCP Gateway...");
    backendProcess = cp.spawn(gatewayExe, [], { 
        cwd: backendDir,
        detached: false, 
        stdio: 'ignore' 
    });
    
    backendProcess.on('error', (err) => {
        vscode.window.showErrorMessage(`Failed to start MCP Gateway: ${err.message}`);
    });
}

export async function activate(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration();
    const provider = new GxFileSystemProvider();

    // Set dynamic port from config
    const port = config.get('genexus.mcpPort', 5000);
    provider.baseUrl = `http://localhost:${port}/api/command`;

    // Initialize Shadow Service (Invisible physical files for Gemini CLI)
    const shadowService = new GxShadowService(provider.baseUrl);
    provider.setShadowService(shadowService);

    let isBulkIndexing = false;

    // CRITICAL: Ensure shadow directory exists for Gemini CLI indexing
    const shadowRoot = shadowService.shadowRoot;
    if (!fs.existsSync(shadowRoot)) {
        fs.mkdirSync(shadowRoot, { recursive: true });
        console.log(`[Nexus IDE] Created shadow root: ${shadowRoot}`);
    }

    // Watcher for disk -> KB synchronization (Transparent to user)
    const shadowWatcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(shadowService.shadowRoot, '**/*.gx'));
    shadowWatcher.onDidChange(async (uri) => {
        if (isBulkIndexing) return; // Silent during bulk operations
        if (shadowService.shouldIgnore(uri.fsPath)) return; // Ignore self-writes

        // When Gemini CLI or user edits a shadow file, sync back to KB
        await shadowService.syncToKB(uri.fsPath);
        
        // Refresh diagnostics after sync
        await diagnosticProvider.refreshAll();
    });

    shadowWatcher.onDidCreate(async (uri) => {
        if (isBulkIndexing) return;
        if (shadowService.shouldIgnore(uri.fsPath)) return;
        await shadowService.syncToKB(uri.fsPath);
    });

    context.subscriptions.push(shadowWatcher);

    // 0. Auto-start Backend if configured (Now with Auto-Discovery)
    startBackend(context);

    // Custom Tree Provider for the GeneXus Explorer view (icons + ordering)
    const treeProvider = new GxTreeProvider(
        (cmd) => provider.callGateway(cmd),
        context.extensionUri,
    );
    const treeView = vscode.window.createTreeView('genexusExplorer', {
        treeDataProvider: treeProvider,
        showCollapseAll: true,
    });
    context.subscriptions.push(treeView);

    // Action Center (Elite Edition)
    const actionsProvider = new GxActionsProvider();
    vscode.window.createTreeView('genexusActions', {
        treeDataProvider: actionsProvider,
        showCollapseAll: false
    });
    
    context.subscriptions.push(vscode.workspace.registerFileSystemProvider('genexus', provider, {
        isCaseSensitive: true,
        isReadonly: false
    }));

    context.subscriptions.push(vscode.languages.registerDocumentSymbolProvider('genexus', new GxDocumentSymbolProvider()));
    context.subscriptions.push(vscode.languages.registerDefinitionProvider('genexus', new GxDefinitionProvider((cmd) => provider.callGateway(cmd))));
    context.subscriptions.push(vscode.languages.registerHoverProvider('genexus', new GxHoverProvider((cmd) => provider.callGateway(cmd))));
    context.subscriptions.push(vscode.languages.registerCompletionItemProvider('genexus', new GxCompletionItemProvider((cmd) => provider.callGateway(cmd)), '.', '&'));
    context.subscriptions.push(vscode.languages.registerInlineCompletionItemProvider('genexus', new GxInlineCompletionItemProvider()));
    context.subscriptions.push(vscode.languages.registerSignatureHelpProvider('genexus', new GxSignatureHelpProvider((cmd) => provider.callGateway(cmd)), '(', ','));
    context.subscriptions.push(vscode.languages.registerCodeActionsProvider('genexus', new GxCodeActionProvider((cmd) => provider.callGateway(cmd)), {
        providedCodeActionKinds: [GxCodeActionProvider.kind]
    }));
    context.subscriptions.push(vscode.languages.registerRenameProvider('genexus', new GxRenameProvider((cmd) => provider.callGateway(cmd))));
    context.subscriptions.push(vscode.languages.registerDocumentFormattingEditProvider('genexus', new GxFormatProvider((cmd) => provider.callGateway(cmd))));

    const diagnosticProvider = new GxDiagnosticProvider((cmd) => provider.callGateway(cmd), provider);
    diagnosticProvider.subscribeToEvents(context);

    context.subscriptions.push(vscode.languages.registerWorkspaceSymbolProvider(new GxWorkspaceSymbolProvider((cmd) => provider.callGateway(cmd))));
    context.subscriptions.push(vscode.languages.registerCodeLensProvider('genexus', new GxCodeLensProvider((cmd) => provider.callGateway(cmd))));
    context.subscriptions.push(vscode.languages.registerReferenceProvider('genexus', new GxReferenceProvider((cmd) => provider.callGateway(cmd))));

    context.subscriptions.push(vscode.commands.registerCommand('gx.showReferences', async (objName: string) => {
        const activeEditor = vscode.window.activeTextEditor;
        if (!activeEditor) return;

        // Trigger native reference peek/view at current position (top of file where CodeLens is)
        await vscode.commands.executeCommand('editor.action.showReferences', 
            activeEditor.document.uri, 
            new vscode.Position(0, 0), 
            [] // We let the provider find them
        );
    }));

    // --- INSTANT ACTIVATION ---
    // 1. Add virtual folder IMMEDIATELY (No delay, no await)
    const hasGxFolder = vscode.workspace.workspaceFolders?.some(f => f.uri.scheme === 'genexus');
    if (!hasGxFolder) {
        vscode.workspace.updateWorkspaceFolders(
            vscode.workspace.workspaceFolders ? vscode.workspace.workspaceFolders.length : 0, 
            null, 
            { uri: vscode.Uri.parse('genexus:/'), name: "GeneXus KB" }
        );
    }

    // 2. Fire and forget KB initialization + Transparent Bulk Indexing
    provider.initKb().then(async () => {
        console.log("[Nexus IDE] KB background init complete.");
        treeProvider.refresh();

        // --- TRANSPARENT AUTO-INDEXING & SHADOWING ---
        // Se o shadowRoot estiver vazio ou for a primeira vez, dispara o indexamento em background
        const shadowPath = shadowService.shadowRoot;
        const shadowDirExists = fs.existsSync(shadowPath) && fs.readdirSync(shadowPath).length > 0;

        if (!shadowDirExists) {
            vscode.window.setStatusBarMessage("$(sync~spin) GeneXus: Preparando ambiente (Index & Shadow)...", 10000);
            
            try {
                isBulkIndexing = true;
                // Dispara o BulkIndex (que agora também faz o Shadow Sync via Worker)
                await provider.callGateway({
                    method: "execute_command",
                    params: { module: 'KB', action: 'BulkIndex' }
                });
                
                // Monitoramento silencioso na barra de status
                let isDone = false;
                while (!isDone) {
                    await new Promise(resolve => setTimeout(resolve, 2000));
                    const status = await provider.callGateway({
                        method: "execute_command",
                        params: { module: 'KB', action: 'GetIndexStatus' }
                    });
                    
                    if (status && status.status === "Complete") {
                        isDone = true;
                        vscode.window.setStatusBarMessage("$(check) GeneXus: Ambiente Pronto!", 5000);
                    } else if (status && status.isIndexing) {
                        vscode.window.setStatusBarMessage(`$(sync~spin) GeneXus: Indexando (${status.processed}/${status.total})...`, 2000);
                    } else {
                        isDone = true; // Parar se não estiver mais indexando por algum motivo
                    }
                }
            } catch (e) {
                console.error("[Nexus IDE] Auto-index failed:", e);
            } finally {
                isBulkIndexing = false;
            }
        }
    }).catch(e => {
        console.error("[Nexus IDE] Background init failed:", e);
    });

    const webviewPanels = new Map<string, vscode.WebviewPanel>();

    // Webview layout preview
    const showWebviewLayout = async (targetUri: vscode.Uri) => {
        const path = decodeURIComponent(targetUri.path.substring(1));
        const objName = path.split('/').pop()!.replace('.gx', '');
        const uriKey = targetUri.toString();

        if (webviewPanels.has(uriKey)) {
            webviewPanels.get(uriKey)!.reveal(vscode.ViewColumn.Beside);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'gxLayout',
            `${objName} Layout`,
            vscode.ViewColumn.Beside,
            { enableScripts: true, enableCommandUris: true }
        );

        webviewPanels.set(uriKey, panel);
        panel.onDidDispose(() => webviewPanels.delete(uriKey));

        panel.webview.html = "<h1>Carregando Layout...</h1>";
        
        try {
            const result = await provider.callGateway({
                method: "execute_command",
                params: { module: 'Read', action: 'ExtractSource', target: objName, part: 'Layout' }
            });
            if (result && result.source) {
                panel.webview.html = result.source;
            } else {
                panel.webview.html = "<h1>Erro ao carregar Layout</h1>";
            }
        } catch (e) {
            panel.webview.html = `<h1>Erro Crítico: ${e}</h1>`;
        }
    };

    // Switch Part Commands
    const switchPart = async (partName: string, uri?: vscode.Uri) => {
        let targetUri = uri;
        if (!targetUri) {
            const editor = vscode.window.activeTextEditor;
            if (editor && editor.document.uri.scheme === 'genexus') {
                targetUri = editor.document.uri;
            } else {
                // Try to find any visible genexus editor
                const gxEditor = vscode.window.visibleTextEditors.find(e => e.document.uri.scheme === 'genexus');
                if (gxEditor) targetUri = gxEditor.document.uri;
            }
        }
        
        if (!targetUri) return;

        // If it's Layout, open a Webview Beside the editor
        if (partName === 'Layout') {
            await showWebviewLayout(targetUri);
            return;
        }

        console.log(`[Nexus IDE] switchPart: Changing part to ${partName} for ${targetUri.fsPath}`);
        provider.setPart(targetUri, partName);
        
        // Force VS Code to re-open the same URI to pick up the new part content
        await vscode.commands.executeCommand('vscode.open', targetUri, {
            preview: false,
            preserveFocus: true
        });

        vscode.window.setStatusBarMessage(`Switched to ${partName}`, 2000);
    };

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Source', (u) => switchPart('Source', u)));
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Rules', (u) => switchPart('Rules', u)));
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Events', (u) => switchPart('Events', u)));
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Variables', (u) => switchPart('Variables', u)));
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Structure', (u) => switchPart('Structure', u)));
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Layout', (u) => switchPart('Layout', u)));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.forceSave', async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor || editor.document.uri.scheme !== 'genexus') return;

        const uri = editor.document.uri;
        const content = Buffer.from(editor.document.getText(), 'utf8');
        
        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `GeneXus: Salvando ${uri.fsPath}...`,
            cancellable: false
        }, async () => {
            try {
                await provider.triggerSave(uri, content);
                vscode.window.setStatusBarMessage(`$(check) Salvo: ${uri.fsPath}`, 5000);
            } catch (e) {
                vscode.window.showErrorMessage(`Erro ao salvar: ${e}`);
            }
        });
    }));

    // Add Workspace command
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.openKb', async () => {
        vscode.workspace.updateWorkspaceFolders(vscode.workspace.workspaceFolders ? vscode.workspace.workspaceFolders.length : 0, null, {
            uri: vscode.Uri.parse('genexus:/'),
            name: "GeneXus KB"
        });
        await provider.initKb();
        await vscode.commands.executeCommand('nexus-ide.indexKb');
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.indexKb', async () => {
        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: "GeneXus: Real-time Indexing KB...",
            cancellable: false
        }, async (progress) => {
            try {
                isBulkIndexing = true;
                // Trigger background indexing
                await provider.callGateway({
                    method: "execute_command",
                    params: { module: 'KB', action: 'BulkIndex' }
                });

                // Polling loop
                let isDone = false;
                let lastProcessed = 0;

                while (!isDone) {
                    await new Promise(resolve => setTimeout(resolve, 1000));
                    const status = await provider.callGateway({
                        method: "execute_command",
                        params: { module: 'KB', action: 'GetIndexStatus' }
                    });

                    if (status && status.isIndexing) {
                        const current = status.processed || 0;
                        const total = status.total || 1;
                        const increment = ((current - lastProcessed) / total) * 100;
                        lastProcessed = current;
                        
                        progress.report({ 
                            message: `${status.status} (${current}/${total})`,
                            increment: increment > 0 ? increment : undefined
                        });
                    } else if (status && (status.status === "Complete" || !status.isIndexing)) {
                        isDone = true;
                    }
                }
            } catch (e) {
                vscode.window.showErrorMessage(`Indexing failed: ${e}`);
            } finally {
                isBulkIndexing = false;
            }
        });
        treeProvider.refresh();
        vscode.window.showInformationMessage("GeneXus KB Indexed! Hierarchy and Search are now ready.");
    }));
    
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.refreshTree', () => {
        provider.clearDirCache();
        treeProvider.refresh();
        vscode.window.setStatusBarMessage('$(refresh) Nexus IDE: Tree refreshed', 3000);
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.refreshDiagnostics', async () => {
        await diagnosticProvider.refreshAll();
    }));

    // Intercept standard Save event for debug
    context.subscriptions.push(vscode.workspace.onDidSaveTextDocument((doc: vscode.TextDocument) => {
        if (doc.uri.scheme === 'genexus') {
            console.log(`[Nexus IDE] onDidSaveTextDocument: ${doc.uri.path}`);
        }
    }));

    // Monitor willSave
    context.subscriptions.push(vscode.workspace.onWillSaveTextDocument((e: vscode.TextDocumentWillSaveEvent) => {
        if (e.document.uri.scheme === 'genexus') {
            console.log(`[Nexus IDE] onWillSaveTextDocument: ${e.document.uri.path}`);
        }
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.runReorg', async () => {
        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: "Checking and Installing Database (Reorg)...",
            cancellable: false
        }, async () => {
            const result = await provider.callGateway({
                method: 'execute_command',
                params: { module: 'Build', action: 'Reorg' }
            });
            if (result && result.status === 'Success') {
                vscode.window.showInformationMessage("Reorganization successful.");
            } else {
                vscode.window.showErrorMessage("Reorganization failed: " + (result?.output || result?.error || 'Unknown error'));
            }
        });
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.buildObject', async (item?: GxTreeItem) => {
        let objName = '';
        if (item && item.gxName) {
            objName = item.gxName;
        } else {
            const editor = vscode.window.activeTextEditor;
            if (editor && editor.document.uri.scheme === 'genexus') {
                const path = decodeURIComponent(editor.document.uri.path.substring(1));
                objName = path.split('/').pop()!.replace('.gx', '');
            }
        }

        if (!objName) {
            vscode.window.showErrorMessage("Selecione um objeto para Build.");
            return;
        }

        const outputChannel = vscode.window.createOutputChannel("GeneXus Build");
        outputChannel.show();
        outputChannel.appendLine(`[Build] Iniciando 'Build with this only' para: ${objName}...`);

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `GeneXus: Building ${objName}...`,
            cancellable: false
        }, async (progress) => {
            try {
                const result = await provider.callGateway({
                    method: "execute_command",
                    params: { module: 'Build', action: 'Build', target: objName }
                }, 600000); // 10 minutes timeout for Build

                if (result && result.status === 'Success') {
                    outputChannel.appendLine(result.output || "Build finalizado com sucesso.");
                    vscode.window.showInformationMessage(`Build de ${objName} concluído!`);
                } else {
                    const errorMsg = result ? (result.error || result.output || JSON.stringify(result)) : "Resposta vazia do Gateway";
                    outputChannel.appendLine(`ERRO NO BUILD:\n${errorMsg}`);
                    vscode.window.showErrorMessage(`Falha no Build de ${objName}. Verifique o log de saída.`);
                }
            } catch (e) {
                outputChannel.appendLine(`ERRO CRÍTICO: ${e}`);
                vscode.window.showErrorMessage(`Erro ao chamar o Gateway para Build: ${e}`);
            }
        });
    }));

    // Optimized Search Provider for CTRL+P (Instant Response)
    if ((vscode.workspace as any).registerFileSearchProvider) {
        (vscode.workspace as any).registerFileSearchProvider('genexus', {
            provideFileSearchResults: async (query: any, _options: any, token: vscode.CancellationToken): Promise<vscode.Uri[]> => {
                try {
                    const pattern = query.pattern || "";
                    if (pattern.length < 2) return [];

                    // Directly call the gateway without artificial delay
                    const result = await provider.callGateway({
                        method: "execute_command",
                        params: { module: 'Search', target: pattern, limit: 1000 }
                    });

                    if (token.isCancellationRequested) return [];

                    if (result && result.results) {
                        return result.results.map((obj: any) => vscode.Uri.parse(`genexus:/${obj.type}/${obj.name}.gx`));
                    }
                } catch (e) {
                    console.error("[Nexus IDE] Global search error:", e);
                }
                return [];
            }
        });
    }

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.createVariable', async (uri: vscode.Uri, varName: string) => {
        const path = decodeURIComponent(uri.path.substring(1));
        const objName = path.split('/').pop()!.replace('.gx', '');

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `Creating Variable &${varName}...`,
            cancellable: false
        }, async () => {
            try {
                const result = await provider.callGateway({
                    method: 'execute_command',
                    params: { module: 'Write', action: 'AddVariable', target: objName, varName: varName }
                });
                if (result && result.status === 'Success') {
                    vscode.window.showInformationMessage(`Variable &${varName} created successfully.`);
                } else {
                    vscode.window.showErrorMessage(`Failed to create variable: ${result.error || JSON.stringify(result)}`);
                }
            } catch (e) {
                vscode.window.showErrorMessage(`Error: ${e}`);
            }
        });
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.copyMcpConfig', async () => {
        const port = vscode.workspace.getConfiguration().get('genexus.mcpPort', 5000);
        const snippet = JSON.stringify({
            "mcpServers": {
                "genexus": {
                    "command": "npx",
                    "args": ["-y", "@modelcontextprotocol/server-http", `http://localhost:${port}/api/command`]
                }
            }
        }, null, 2);
        
        await vscode.env.clipboard.writeText(snippet);
        vscode.window.showInformationMessage("MCP Configuration snippet for Claude/Copilot copied to clipboard!");
    }));

    // --- ELITE EDITION COMMANDS ---

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.newObject', async () => {
        const types = Object.keys(TYPE_SUFFIX);
        const selectedType = await vscode.window.showQuickPick(types, {
            placeHolder: 'Select object type to create'
        });

        if (!selectedType) return;

        const name = await vscode.window.showInputBox({
            prompt: `Enter name for the new ${selectedType}`,
            placeHolder: 'e.g. MyNewObject'
        });

        if (!name) return;

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `Creating ${selectedType}: ${name}...`,
            cancellable: false
        }, async () => {
            try {
                const result = await provider.callGateway({
                    method: 'execute_command',
                    params: { module: 'KB', action: 'CreateObject', type: selectedType, name: name }
                });

                if (result && result.status === 'Success') {
                    vscode.window.showInformationMessage(`${selectedType} '${name}' created!`);
                    
                    // Open the new object
                    const suffix = TYPE_SUFFIX[selectedType] ? `.${TYPE_SUFFIX[selectedType]}` : '';
                    const uri = vscode.Uri.parse(`genexus:/${name}${suffix}.gx`);
                    await vscode.commands.executeCommand('vscode.open', uri);
                    
                    // Refresh explorer
                    provider.clearDirCache();
                    treeProvider.refresh();
                } else {
                    vscode.window.showErrorMessage(`Failed to create object: ${result?.error || 'Unknown error'}`);
                }
            } catch (e) {
                vscode.window.showErrorMessage(`Error creating object: ${e}`);
            }
        });
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.rebuildAll', async () => {
        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: "Rebuilding All objects...",
            cancellable: false
        }, async () => {
            try {
                await provider.callGateway({
                    method: 'execute_command',
                    params: { module: 'Build', action: 'RebuildAll' }
                });
                vscode.window.showInformationMessage("Rebuild All completed!");
            } catch (e) {
                vscode.window.showErrorMessage(`Rebuild All failed: ${e}`);
            }
        });
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.renameAttribute', async () => {
        const oldName = await vscode.window.showInputBox({
            prompt: 'Enter current attribute name',
            placeHolder: 'e.g. CustomerName'
        });

        if (!oldName) return;

        const newName = await vscode.window.showInputBox({
            prompt: `Rename attribute '${oldName}' to:`,
            placeHolder: 'e.g. CustomerFullName'
        });

        if (!newName) return;

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: `Renaming Attribute ${oldName} -> ${newName}...`,
            cancellable: false
        }, async () => {
            try {
                const result = await provider.callGateway({
                    method: 'execute_command',
                    params: { module: 'Refactor', action: 'RenameAttribute', target: oldName, payload: newName }
                });

                if (result && result.status === 'Success') {
                    vscode.window.showInformationMessage(`Attribute renamed successfully!`);
                    provider.clearDirCache();
                    treeProvider.refresh();
                } else {
                    vscode.window.showErrorMessage(`Failed to rename: ${result?.error || 'Unknown error'}`);
                }
            } catch (e) {
                vscode.window.showErrorMessage(`Error renaming attribute: ${e}`);
            }
        });
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.generateDiagram', async (item?: GxTreeItem) => {
        let objName = '';
        if (item && item.gxName) {
            objName = item.gxName;
        } else {
            const editor = vscode.window.activeTextEditor;
            if (editor && editor.document.uri.scheme === 'genexus') {
                const path = decodeURIComponent(editor.document.uri.path.substring(1));
                objName = path.split('/').pop()!.replace('.gx', '');
            }
        }

        if (!objName) {
            vscode.window.showErrorMessage("Selecione um objeto para gerar o diagrama.");
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'gxDiagram',
            `${objName} Diagram`,
            vscode.ViewColumn.Beside,
            { enableScripts: true }
        );

        panel.webview.html = `<h1>Gerando Diagrama para ${objName}...</h1>`;

        try {
            const result = await provider.callGateway({
                method: 'execute_command',
                params: { module: 'Visualizer', action: 'GenerateGraph', target: objName }
            });

            if (result && result.mermaid) {
                panel.webview.html = `
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <script src="https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js"></script>
                        <script>mermaid.initialize({ startOnLoad: true });</script>
                    </head>
                    <body>
                        <pre class="mermaid">
                            ${result.mermaid}
                        </pre>
                    </body>
                    </html>
                `;
            } else {
                panel.webview.html = "<h1>Não foi possível gerar o diagrama para este objeto.</h1>";
            }
        } catch (e) {
            panel.webview.html = `<h1>Erro: ${e}</h1>`;
        }
    }));

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.autoFix', async () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor || editor.document.uri.scheme !== 'genexus') {
            vscode.window.showErrorMessage("Abra um objeto GeneXus para usar o Auto-Fix.");
            return;
        }

        const diagnostics = vscode.languages.getDiagnostics(editor.document.uri);
        const error = diagnostics.find(d => d.severity === vscode.DiagnosticSeverity.Error);

        if (!error) {
            vscode.window.showInformationMessage("Nenhum erro de build encontrado neste objeto.");
            return;
        }

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: "AI Analyzing error and proposing fix...",
            cancellable: false
        }, async () => {
            try {
                const result = await provider.callGateway({
                    method: 'execute_command',
                    params: { 
                        module: 'Analyze', 
                        action: 'ExplainCode', 
                        target: editor.document.uri.path.split('/').pop()?.replace('.gx', ''),
                        payload: JSON.stringify({
                            error: error.message,
                            line: error.range.start.line,
                            code: editor.document.getText()
                        })
                    }
                });

                if (result && result.fix) {
                    const choice = await vscode.window.showInformationMessage(
                        `AI Fix suggested: ${result.summary}\nApply fix?`,
                        "Apply Fix", "Cancel"
                    );

                    if (choice === "Apply Fix") {
                        const edit = new vscode.WorkspaceEdit();
                        const fullRange = new vscode.Range(
                            editor.document.positionAt(0),
                            editor.document.positionAt(editor.document.getText().length)
                        );
                        edit.replace(editor.document.uri, fullRange, result.fix);
                        await vscode.workspace.applyEdit(edit);
                        vscode.window.showInformationMessage("AI Fix applied! Save to verify.");
                    }
                } else {
                    vscode.window.showWarningMessage("AI não conseguiu encontrar uma solução automática para este erro.");
                }
            } catch (e) {
                vscode.window.showErrorMessage(`Erro no Auto-Fix: ${e}`);
            }
        });
    }));
}

export function deactivate() {
    if (backendProcess && !backendProcess.killed) {
        console.log("[Nexus IDE] Stopping MCP Gateway...");
        backendProcess.kill();
    }
}