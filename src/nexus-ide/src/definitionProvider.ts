import * as vscode from 'vscode';

export class GxDefinitionProvider implements vscode.DefinitionProvider {
    async provideDefinition(
        document: vscode.TextDocument,
        position: vscode.Position,
        _token: vscode.CancellationToken
    ): Promise<vscode.Definition | undefined> {
        const line = document.lineAt(position.line).text;
        const range = document.getWordRangeAtPosition(position);
        if (!range) return undefined;
        
        const word = document.getText(range);

        // 1. Check if it's a Subroutine call: do 'SubName' or do SubName
        const doMatch = line.match(/\bdo\s+['"]?([a-zA-Z0-9_]+)['"]?/i);
        if (doMatch && line.includes(word) && word === doMatch[1]) {
            const subName = doMatch[1];
            // Search for the sub definition in the same file
            const text = document.getText();
            const subDefRegex = new RegExp(`\\b(sub)\\s+['"]?${subName}['"]?`, 'gi');
            let match;
            while ((match = subDefRegex.exec(text)) !== null) {
                const startPos = document.positionAt(match.index);
                return new vscode.Location(document.uri, startPos);
            }
        }

        // 2. Check if it's an external object call (Udp, call, etc)
        // We look for patterns like MyProc.Udp( or call(MyProc, ...)
        const udpMatch = line.match(/([a-zA-Z_][a-zA-Z0-9_]*)\.(Udp|Call|Execute)\(/i);
        if (udpMatch && word === udpMatch[1]) {
            const objName = udpMatch[1];
            // We assume Procedures for now as they are the most common for UDP/Call
            return new vscode.Location(
                vscode.Uri.parse(`genexus:/Procedure/${objName}.gx`),
                new vscode.Position(0, 0)
            );
        }

        return undefined;
    }
}
