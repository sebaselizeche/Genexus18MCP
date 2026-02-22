import * as vscode from 'vscode';
import { GxFileSystemProvider } from './gxFileSystem';
import { GxDocumentSymbolProvider } from './symbolProvider';
import { GxTreeProvider } from './gxTreeProvider';

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

    // Auto-Open KB if folder is in workspace
    const hasGxFolder = vscode.workspace.workspaceFolders?.some(f => f.uri.scheme === 'genexus');
    if (hasGxFolder) {
        provider.initKb().catch(() => {});
    }

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

    // Register Search Provider for CTRL+P (Proposed API for Insiders)
    if ((vscode.workspace as any).registerFileSearchProvider) {
        (vscode.workspace as any).registerFileSearchProvider('genexus', {
            provideFileSearchResults: async (query: any, _options: any, _token: any): Promise<vscode.Uri[]> => {
                const types = ['Procedure', 'Transaction', 'WebPanel', 'DataProvider', 'Attribute', 'Table', 'SDPanel', 'DataView'];
                const results: vscode.Uri[] = [];
                const pattern = query.pattern.toLowerCase();
                
                for (const type of types) {
                    try {
                        const files = await vscode.workspace.fs.readDirectory(vscode.Uri.parse(`genexus:/${type}`));
                        for (const [name, _] of files) {
                            if (name.toLowerCase().includes(pattern)) {
                                results.push(vscode.Uri.parse(`genexus:/${type}/${name}`));
                            }
                        }
                    } catch { }
                }
                return results;
            }
        });
    }
}

export function deactivate() {}