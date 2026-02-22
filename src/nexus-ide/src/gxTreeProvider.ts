import * as vscode from 'vscode';
import * as path from 'path';

// Sort priority inside any folder: Module → Folder → everything else (alphabetical within groups)
const TYPE_ORDER: Record<string, number> = {
    'Module': 0,
    'Folder': 1,
};
const FILE_ORDER = 2;

// Maps GeneXus type names → icon file in resources/
const TYPE_ICON_FILE: Record<string, string> = {
    'Module':         'module',
    'Folder':         'folder',
    'Procedure':      'procedure',
    'WebPanel':       'webpanel',
    'Transaction':    'transaction',
    'SDT':            'sdt',
    'DataProvider':   'dataprovider',
    'DataView':       'dataview',
    'Attribute':      'attribute',
    'Table':          'table',
    'SDPanel':        'sdpanel',
};

export interface GxObject {
    name: string;
    type: string;
    description?: string;
    parent?: string;
    module?: string;
}

export class GxTreeItem extends vscode.TreeItem {
    constructor(
        public readonly gxName: string,
        public readonly gxType: string,
        public readonly gxParentPath: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        private readonly extensionUri: vscode.Uri,
    ) {
        super(gxName, collapsibleState);

        const isContainer = gxType === 'Module' || gxType === 'Folder';

        this.tooltip = `[${gxType}] ${gxName}`;
        this.contextValue = `gx_${gxType.toLowerCase()}`;

        // Icon: use SVG if available, else codicon fallback
        const iconFile = TYPE_ICON_FILE[gxType];
        if (iconFile) {
            const iconUri = vscode.Uri.joinPath(extensionUri, 'resources', `${iconFile}.svg`);
            this.iconPath = { light: iconUri, dark: iconUri };
        } else {
            this.iconPath = new vscode.ThemeIcon('symbol-misc');
        }

        if (!isContainer) {
            // File item: set resourceUri for the genexus:// virtual filesystem and open command
            this.resourceUri = vscode.Uri.parse(
                `genexus:/${gxParentPath ? gxParentPath + '/' : ''}${gxName}.gx`
            );
            this.command = {
                command: 'vscode.open',
                title: 'Open',
                arguments: [this.resourceUri],
            };
        }
    }
}

export class GxTreeProvider implements vscode.TreeDataProvider<GxTreeItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<GxTreeItem | undefined | null | void>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private _cache = new Map<string, { items: GxTreeItem[]; time: number }>();

    constructor(
        private readonly callGateway: (cmd: any) => Promise<any>,
        private readonly extensionUri: vscode.Uri,
    ) {}

    /** Clear the cache and re-render the whole tree */
    refresh(): void {
        this._cache.clear();
        this._onDidChangeTreeData.fire();
    }

    /** Clear cache for a single node (e.g., after save) */
    refreshNode(item: GxTreeItem): void {
        this._cache.delete(item.gxName);
        this._onDidChangeTreeData.fire(item);
    }

    getTreeItem(element: GxTreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: GxTreeItem): Promise<GxTreeItem[]> {
        const parentName  = element ? element.gxName    : 'Root Module';
        const parentPath  = element
            ? (element.gxParentPath ? element.gxParentPath + '/' : '') + element.gxName
            : '';

        const cached = this._cache.get(parentName);
        if (cached && Date.now() - cached.time < 60_000) return cached.items;

        try {
            const result = await this.callGateway({
                method: 'execute_command',
                params: { module: 'Search', query: `parent:"${parentName}"`, limit: 5000 },
            });

            const objects: GxObject[] = result.results || (Array.isArray(result) ? result : []);

            // Sort: Module (0) → Folder (1) → Files (2), alphabetical within each group
            objects.sort((a, b) => {
                const oa = TYPE_ORDER[a.type] ?? FILE_ORDER;
                const ob = TYPE_ORDER[b.type] ?? FILE_ORDER;
                if (oa !== ob) return oa - ob;
                return a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });
            });

            const items = objects.map(obj => {
                const isContainer = obj.type === 'Module' || obj.type === 'Folder';
                return new GxTreeItem(
                    obj.name,
                    obj.type,
                    parentPath,
                    isContainer
                        ? vscode.TreeItemCollapsibleState.Collapsed
                        : vscode.TreeItemCollapsibleState.None,
                    this.extensionUri,
                );
            });

            this._cache.set(parentName, { items, time: Date.now() });
            return items;

        } catch (e) {
            console.error(`[Nexus IDE] TreeProvider error for ${parentName}:`, e);
            return [];
        }
    }
}
