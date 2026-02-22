import * as vscode from 'vscode';
import { nativeFunctions } from './gxNativeFunctions';

export class GxSignatureHelpProvider implements vscode.SignatureHelpProvider {
    provideSignatureHelp(
        document: vscode.TextDocument,
        position: vscode.Position,
        _token: vscode.CancellationToken,
        _context: vscode.SignatureHelpContext
    ): vscode.ProviderResult<vscode.SignatureHelp> {
        const lineText = document.lineAt(position).text;
        const lineUntilCursor = lineText.substring(0, position.character);

        // Find the function name and the current parameter index
        // This is a simple regex-based approach for native functions
        const match = lineUntilCursor.match(/([a-zA-Z0-9_]+)\s*\(([^)]*)$/);
        if (!match) return undefined;

        const funcName = match[1];
        const paramsText = match[2];
        const paramIndex = paramsText.split(',').length - 1;

        const func = nativeFunctions.find(f => f.name.toLowerCase() === funcName.toLowerCase());
        if (!func || !func.paramDetails || func.paramDetails.length === 0) return undefined;

        const signatureHelp = new vscode.SignatureHelp();
        const signatureInfo = new vscode.SignatureInformation(`${func.name}${func.parameters}`, func.description);
        
        signatureInfo.parameters = func.paramDetails.map(d => new vscode.ParameterInformation(d.split(':')[0], d.split(':')[1] || ''));
        
        signatureHelp.signatures = [signatureInfo];
        signatureHelp.activeSignature = 0;
        signatureHelp.activeParameter = Math.min(paramIndex, signatureInfo.parameters.length - 1);

        return signatureHelp;
    }
}
