import * as assert from "assert";
import * as vscode from "vscode";

suite("Nexus IDE Extension Test Suite", () => {
  vscode.window.showInformationMessage("Start all tests.");

  test("Extension should be present", () => {
    assert.ok(vscode.extensions.getExtension("lennix1337.nexus-ide"));
  });

  test("Should register custom filesystem provider", async () => {
    const uri = vscode.Uri.parse("gxkb18:/Procedure/Test.gx");
    const stat = await vscode.workspace.fs.stat(uri);
    assert.ok(stat.type === vscode.FileType.Directory);
  });

  test("Should have Quality & Testing commands registered", async () => {
    // Wait for activation if needed
    const extension = vscode.extensions.getExtension("lennix1337.nexus-ide");
    if (extension && !extension.isActive) {
      await extension.activate();
    }

    const commands = await vscode.commands.getCommands(true);
    assert.ok(
      commands.includes("nexus-ide.runTest"),
      "Command runTest not found",
    );
    assert.ok(
      commands.includes("nexus-ide.runLinter"),
      "Command runLinter not found",
    );
    assert.ok(
      commands.includes("nexus-ide.extractProcedure"),
      "Command extractProcedure not found",
    );
  });
});
