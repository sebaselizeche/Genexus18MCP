import * as vscode from 'vscode';
import { nativeFunctions, keywords, typeMethods } from './gxNativeFunctions';

export class GxCompletionItemProvider implements vscode.CompletionItemProvider {
    private varCache = new Map<string, any[]>();

    constructor(private readonly callGateway: (cmd: any) => Promise<any>) {}

    async provideCompletionItems(
        document: vscode.TextDocument,
        position: vscode.Position,
        _token: vscode.CancellationToken,
        context: vscode.CompletionContext
    ): Promise<vscode.CompletionItem[] | vscode.CompletionList> {
        const items: vscode.CompletionItem[] = [];
        const lineText = document.lineAt(position).text;
        const lineUntilCursor = lineText.substring(0, position.character);

        // 1. Check for Member Access (e.g., &var. or &var.pa)
        // Regex improved: matches &varName. followed by optional partial method name
        const memberMatch = lineUntilCursor.match(/&([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]*)$/);
        if (memberMatch) {
            const varName = memberMatch[1];
            const partialMethod = memberMatch[2];
            const objName = this.getObjName(document);
            const variables = await this.getVariables(objName);
            const variable = variables.find(v => v.name.toLowerCase() === varName.toLowerCase());
            
            let type = variable ? variable.type : 'Character'; // Default to Character if not found (common for strings)
            if (type.endsWith('Collection')) type = 'Collection'; 

            // Map standard GeneXus type names to our keys
            if (type === 'VarChar' || type === 'LongVarChar' || type === 'Character') type = 'Character';
            if (type === 'Numeric' || type === 'Integer' || type === 'SmallInt') type = 'Numeric';

            const methods = typeMethods[type] || typeMethods['Character']; // Default to Character methods for best UX
            for (const m of methods) {
                if (partialMethod && !m.name.toLowerCase().startsWith(partialMethod.toLowerCase())) continue;
                
                const item = new vscode.CompletionItem(m.name, vscode.CompletionItemKind.Method);
                item.detail = `${m.name}${m.parameters}: ${m.returnType}`;
                item.documentation = new vscode.MarkdownString(m.description);
                item.insertText = new vscode.SnippetString(m.snippet || m.name);
                items.push(item);
            }
            return items;
        }

        // 2. Add Native Functions and Keywords (Always relevant)
        for (const func of nativeFunctions) {
            const item = new vscode.CompletionItem(func.name, vscode.CompletionItemKind.Function);
            item.detail = `(Native) ${func.name}${func.parameters}: ${func.returnType}`;
            item.documentation = new vscode.MarkdownString(func.description);
            item.insertText = new vscode.SnippetString(func.snippet || func.name);
            items.push(item);
        }

        for (const kw of keywords) {
            const item = new vscode.CompletionItem(kw.name, vscode.CompletionItemKind.Snippet);
            item.detail = `(Keyword) ${kw.name}`;
            item.insertText = new vscode.SnippetString(kw.snippet);
            items.push(item);
        }

        // 3. Add Local Variables (If typing & or just in general)
        const objName = this.getObjName(document);
        const variables = await this.getVariables(objName);
        for (const v of variables) {
            const item = new vscode.CompletionItem(`&${v.name}`, vscode.CompletionItemKind.Variable);
            item.detail = `${v.type}(${v.length})`;
            items.push(item);
        }

        // 4. Add Attributes (Experimental: Search if prefix > 2)
        const range = document.getWordRangeAtPosition(position);
        if (range) {
            const word = document.getText(range);
            if (word.length >= 2 && !word.startsWith('&')) {
                const attrResults = await this.callGateway({
                    method: 'execute_command',
                    params: { module: 'Search', query: `type:Attribute ${word}`, limit: 15 }
                });
                if (attrResults && attrResults.results) {
                    for (const attr of attrResults.results) {
                        const item = new vscode.CompletionItem(attr.name, vscode.CompletionItemKind.Property);
                        item.detail = `(Attribute) ${attr.description || ''}`;
                        if (attr.parm) item.documentation = new vscode.MarkdownString(attr.parm);
                        items.push(item);
                    }
                }
            }
        }

        return items;
    }

    private getObjName(document: vscode.TextDocument): string {
        const path = decodeURIComponent(document.uri.path.substring(1));
        return path.split('/').pop()!.replace('.gx', '');
    }

    private async getVariables(objName: string): Promise<any[]> {
        if (this.varCache.has(objName)) return this.varCache.get(objName)!;

        try {
            const result = await this.callGateway({
                method: "execute_command",
                params: { module: 'Read', action: 'GetVariables', target: objName }
            });
            if (result && Array.isArray(result)) {
                this.varCache.set(objName, result);
                return result;
            }
        } catch (e) {
            console.error("[Nexus IDE] Error fetching variables:", e);
        }
        return [];
    }
}
