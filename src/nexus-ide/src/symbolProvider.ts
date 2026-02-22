import * as vscode from 'vscode';

export class GxDocumentSymbolProvider implements vscode.DocumentSymbolProvider {
    public provideDocumentSymbols(document: vscode.TextDocument, _token: vscode.CancellationToken): vscode.SymbolInformation[] {
        const symbols: vscode.SymbolInformation[] = [];
        const text = document.getText();
        
        // Regex for Subroutines: Sub 'Name' or Sub Name
        const subRegex = /^\s*Sub\s+['"]?([a-zA-Z0-9_]+)['"]?/gmi;
        let match;
        while ((match = subRegex.exec(text)) !== null) {
            const line = document.lineAt(document.positionAt(match.index).line);
            symbols.push(new vscode.SymbolInformation(
                match[1],
                vscode.SymbolKind.Function,
                '',
                new vscode.Location(document.uri, line.range)
            ));
        }

        // Regex for Events: Event 'Name' or Event Name
        const eventRegex = /^\s*Event\s+['"]?([a-zA-Z0-9_.]+)['"]?/gmi;
        while ((match = eventRegex.exec(text)) !== null) {
            const line = document.lineAt(document.positionAt(match.index).line);
            symbols.push(new vscode.SymbolInformation(
                match[1],
                vscode.SymbolKind.Event,
                '',
                new vscode.Location(document.uri, line.range)
            ));
        }

        // Regex for Rules: parm(...)
        const ruleRegex = /^\s*(parm|order|where)\s*\(.*\)/gmi;
        while ((match = ruleRegex.exec(text)) !== null) {
            const line = document.lineAt(document.positionAt(match.index).line);
            symbols.push(new vscode.SymbolInformation(
                match[0].trim(),
                vscode.SymbolKind.Property,
                '',
                new vscode.Location(document.uri, line.range)
            ));
        }

        return symbols;
    }
}
