import * as vscode from "vscode";
import * as fs from "fs";
import { GxFileSystemProvider } from "./gxFileSystem";
import { GxTreeProvider } from "./gxTreeProvider";
import { GxActionsProvider } from "./gxActionsProvider";
import { GxDiagnosticProvider } from "./diagnosticProvider";
import { GxShadowService } from "./gxShadowService";

import { BackendManager } from "./managers/BackendManager";
import { ShadowManager } from "./managers/ShadowManager";
import { ContextManager } from "./managers/ContextManager";
import { CommandManager } from "./managers/CommandManager";
import { ProviderManager } from "./managers/ProviderManager";
import { McpDiscoveryManager } from "./managers/McpDiscoveryManager";

let backendManager: BackendManager;
const SCHEME = "gxkb18";
const STATE_KEY_FOLDER_ADDED = "genexus.kbFolderAdded_V6";

export function activate(context: vscode.ExtensionContext) {
  console.log("[Nexus IDE] Extension activating...");

  const provider = new GxFileSystemProvider();

  // 1. REGISTER COMMANDS FIRST (Ensure they are always available)
  context.subscriptions.push(
    vscode.commands.registerCommand("nexus-ide.openKb", () => {
      console.log("[Nexus IDE] Command 'nexus-ide.openKb' triggered.");
      addKbFolder();
    }),
    vscode.commands.registerCommand("nexus-ide.addKbFolder", () => {
      console.log("[Nexus IDE] Manual 'nexus-ide.addKbFolder' triggered.");
      addKbFolder();
    }),
    vscode.commands.registerCommand("nexus-ide.refreshFilesystem", () => {
      console.log(
        "[Nexus IDE] Command 'nexus-ide.refreshFilesystem' triggered.",
      );
      provider.clearDirCache();
    }),
  );

  // 2. REGISTER FILESYSTEM PROVIDER
  try {
    context.subscriptions.push(
      vscode.workspace.registerFileSystemProvider(SCHEME, provider, {
        isCaseSensitive: false,
        isReadonly: false,
      }),
    );
    console.log(
      `[Nexus IDE] GxFileSystemProvider registered for scheme '${SCHEME}'.`,
    );

    // Warm up
    vscode.workspace.fs
      .stat(vscode.Uri.from({ scheme: SCHEME, path: "/" }))
      .then(
        () => console.log(`[Nexus IDE] Scheme '${SCHEME}' warm up success.`),
        () => {},
      );

    // Dummy search providers to satisfy workbench
    context.subscriptions.push(
      (vscode.workspace as any).registerFileSearchProvider(SCHEME, {
        provideFileSearchResults: () => Promise.resolve([]),
      }),
      (vscode.workspace as any).registerTextSearchProvider(SCHEME, {
        provideTextSearchResults: () => Promise.resolve({ limitHit: false }),
      }),
    );
  } catch (e) {
    console.error("[Nexus IDE] FS registration failed:", e);
  }

  // 3. DEFERRED INITIALIZATION
  setImmediate(() => {
    try {
      initializeExtension(context, provider);
    } catch (e) {
      console.error("[Nexus IDE] Init failed:", e);
    }
  });

  function addKbFolder() {
    const folders = vscode.workspace.workspaceFolders || [];
    const hasGxFolder = folders.some((f) => f.uri.scheme === SCHEME);
    if (!hasGxFolder) {
      console.log(`[Nexus IDE] Adding workspace folder: ${SCHEME}:/`);
      vscode.workspace.updateWorkspaceFolders(folders.length, 0, {
        uri: vscode.Uri.from({ scheme: SCHEME, path: "/" }),
        name: "GeneXus KB",
      });
      context.globalState.update(STATE_KEY_FOLDER_ADDED, true);
    }
  }

  // Auto-add folder if first time
  if (!context.globalState.get(STATE_KEY_FOLDER_ADDED, false)) {
    setTimeout(addKbFolder, 5000);
  }
}

function initializeExtension(
  context: vscode.ExtensionContext,
  provider: GxFileSystemProvider,
) {
  console.log("[Nexus IDE] Starting deferred initialization...");

  const config = vscode.workspace.getConfiguration();
  const port = config.get("genexus.mcpPort", 5000);
  provider.baseUrl = `http://localhost:${port}/api/command`;

  // Initialize Managers
  backendManager = new BackendManager(context);
  const contextManager = new ContextManager(context, provider);
  const shadowService = new GxShadowService(provider.baseUrl);
  provider.setShadowService(shadowService);

  const discoveryManager = new McpDiscoveryManager(context, provider);
  // Defer discovery registration until AFTER backend is potentially ready
  // discoveryManager.register(); // Moved below

  const diagnosticProvider = new GxDiagnosticProvider(
    (cmd) => provider.callGateway(cmd),
    provider,
  );
  provider.setDiagnosticProvider(diagnosticProvider);

  const treeProvider = new GxTreeProvider(
    (cmd) => provider.callGateway(cmd),
    context.extensionUri,
  );

  const shadowManager = new ShadowManager(
    context,
    provider,
    shadowService,
    diagnosticProvider,
  );
  shadowManager.register();

  // UI Components
  const treeView = vscode.window.createTreeView("genexusExplorer", {
    treeDataProvider: treeProvider,
    showCollapseAll: true,
  });
  context.subscriptions.push(treeView);

  treeView.onDidChangeSelection((e) => {
    if (e.selection.length > 0) {
      const item: any = e.selection[0];
      if (item && item.resourceUri) {
        provider.preWarm(item.resourceUri);
      }
    }
  });

  const actionsProvider = new GxActionsProvider();
  vscode.window.createTreeView("genexusActions", {
    treeDataProvider: actionsProvider,
    showCollapseAll: false,
  });

  const providerManager = new ProviderManager(context, provider);
  providerManager.register();

  const commandManager = new CommandManager(
    context,
    provider,
    treeProvider,
    diagnosticProvider,
    contextManager,
    providerManager.historyProvider,
  );
  commandManager.register();

  contextManager.register();
  diagnosticProvider.subscribeToEvents(context);

  // Start Backend and register discovery tools
  backendManager
    .start(provider)
    .then(() => {
      console.log(
        "[Nexus IDE] Backend started successfully. Registering discovery tools...",
      );
      discoveryManager.register();
    })
    .catch((e) => console.error("[Nexus IDE] Backend start failed:", e));

  // KB Background Initialization
  provider.initKb().then(async () => {
    console.log("[Nexus IDE] KB background init complete.");
    treeProvider.refresh();

    const shadowPath = shadowService.shadowRoot;
    const shadowDirExists =
      fs.existsSync(shadowPath) && fs.readdirSync(shadowPath).length > 0;

    if (!shadowDirExists) {
      vscode.window.setStatusBarMessage(
        "$(sync~spin) GeneXus: Preparando ambiente...",
        10000,
      );
      try {
        provider.isBulkIndexing = true;
        await provider.callGateway({
          method: "execute_command",
          params: { module: "KB", action: "BulkIndex" },
        });

        let isDone = false;
        while (!isDone) {
          await new Promise((resolve) => setTimeout(resolve, 2000));
          const status = await provider.callGateway({
            method: "execute_command",
            params: { module: "KB", action: "GetIndexStatus" },
          });

          if (status && status.status === "Complete") {
            isDone = true;
            vscode.window.setStatusBarMessage(
              "$(check) GeneXus: Ambiente Pronto!",
              5000,
            );
          } else if (status && status.isIndexing) {
            vscode.window.setStatusBarMessage(
              `$(sync~spin) GeneXus: Indexando (${status.processed}/${status.total})...`,
              2000,
            );
          } else {
            isDone = true;
          }
        }
      } catch (e) {
        console.error("[Nexus IDE] Auto-index failed:", e);
      } finally {
        provider.isBulkIndexing = false;
      }
    }
  });

  // Watch for Save event
  context.subscriptions.push(
    vscode.workspace.onDidSaveTextDocument((doc: vscode.TextDocument) => {
      if (doc.uri.scheme === SCHEME) {
        setTimeout(() => {
          diagnosticProvider.refreshAll();
        }, 1000);
      }
    }),
  );

  console.log("[Nexus IDE] Deferred initialization complete.");
}

export function deactivate() {
  if (backendManager) {
    backendManager.stop();
  }
}
