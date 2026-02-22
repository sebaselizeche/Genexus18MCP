import * as vscode from 'vscode';

export class GxHoverProvider implements vscode.HoverProvider {
    constructor(private readonly callGateway: (cmd: any) => Promise<any>) {}

    async provideHover(
        document: vscode.TextDocument,
        position: vscode.Position,
        _token: vscode.CancellationToken
    ): Promise<vscode.Hover | undefined> {
        const range = document.getWordRangeAtPosition(position);
        if (!range) return undefined;
        const word = document.getText(range);

        // Filter out keywords or very small words to avoid noise
        if (word.length < 3) return undefined;

        try {
            // Search for the object in the index
            const result = await this.callGateway({
                method: 'execute_command',
                params: { module: 'Search', query: word, limit: 1 }
            });

            if (result && result.results && result.results.length > 0) {
                const obj = result.results[0];
                if (obj.name.toLowerCase() === word.toLowerCase()) {
                    const markdown = new vscode.MarkdownString();
                    markdown.appendMarkdown(`### [${obj.type}] **${obj.name}**

`);
                    if (obj.description) markdown.appendMarkdown(`*${obj.description}*

`);
                    if (obj.parm) {
                        markdown.appendCodeblock(obj.parm, 'genexus');
                    }
                    if (obj.snippet) {
                        markdown.appendMarkdown(`---
**Preview:**
`);
                        markdown.appendCodeblock(obj.snippet, 'genexus');
                    }
                    return new vscode.Hover(markdown);
                }
            }
        } catch (e) {
            console.error("[Nexus IDE] Hover error:", e);
        }

        return undefined;
    }
}
