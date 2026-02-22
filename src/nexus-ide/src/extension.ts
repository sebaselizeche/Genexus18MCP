import * as vscode from 'vscode';
import { GxFileSystemProvider } from './gxFileSystem';
import { GxDocumentSymbolProvider } from './symbolProvider';
import { GxTreeProvider, GxTreeItem } from './gxTreeProvider';
import { GxDefinitionProvider } from './definitionProvider';
import { GxHoverProvider } from './hoverProvider';

export function activate(context: vscode.ExtensionContext) {
    const provider = new GxFileSystemProvider();

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
    
    context.subscriptions.push(vscode.workspace.registerFileSystemProvider('genexus', provider, {
        isCaseSensitive: true,
        isReadonly: false
    }));

    context.subscriptions.push(vscode.languages.registerDocumentSymbolProvider('genexus', new GxDocumentSymbolProvider()));
    context.subscriptions.push(vscode.languages.registerDefinitionProvider('genexus', new GxDefinitionProvider()));
    context.subscriptions.push(vscode.languages.registerHoverProvider('genexus', new GxHoverProvider((cmd) => provider.callGateway(cmd))));

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

    // 2. Fire and forget KB initialization
    provider.initKb().then(() => {
        console.log("[Nexus IDE] KB background init complete.");
        treeProvider.refresh();
    }).catch(e => {
        console.error("[Nexus IDE] Background init failed:", e);
    });

    // Switch Part Commands
    const switchPart = async (partName: string) => {
        const editor = vscode.window.activeTextEditor;
        if (!editor || editor.document.uri.scheme !== 'genexus') return;

        console.log(`[Nexus IDE] switchPart: Changing part to ${partName} for ${editor.document.uri.fsPath}`);
        provider.setPart(editor.document.uri, partName);
        
        vscode.window.setStatusBarMessage(`Switched to ${partName}`, 2000);
    };

    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Source', () => switchPart('Source')));
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Rules', () => switchPart('Rules')));
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Events', () => switchPart('Events')));
    context.subscriptions.push(vscode.commands.registerCommand('nexus-ide.switchPart.Variables', () => switchPart('Variables')));

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
                progress.report({ message: "Running GeneXus SDK Indexing..." });
                await provider.callGateway({
                    method: "execute_command",
                    params: { module: 'KB', action: 'BulkIndex' }
                });
            } catch (e) {
                vscode.window.showErrorMessage(`Indexing failed: ${e}`);
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
}

export function deactivate() {}