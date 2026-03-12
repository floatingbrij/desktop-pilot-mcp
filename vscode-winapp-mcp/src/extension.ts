import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

const MCP_SERVER_ID = 'winapp';

export function activate(context: vscode.ExtensionContext) {
    const serverExe = path.join(context.extensionPath, 'server', 'WinAppMCP.exe');
    registerMcpServer(serverExe);

    context.subscriptions.push(
        vscode.commands.registerCommand('winapp-mcp.register', () => {
            registerMcpServer(serverExe);
            vscode.window.showInformationMessage('WinApp MCP server registered. Reload window to activate.');
        }),
        vscode.commands.registerCommand('winapp-mcp.unregister', () => {
            unregisterMcpServer();
            vscode.window.showInformationMessage('WinApp MCP server unregistered.');
        })
    );
}

function getMcpConfigPath(): string {
    const appData = process.env.APPDATA || '';
    const isInsiders = vscode.env.appName.includes('Insiders');
    const folder = isInsiders ? 'Code - Insiders' : 'Code';
    return path.join(appData, folder, 'User', 'mcp.json');
}

function registerMcpServer(serverExe: string) {
    const configPath = getMcpConfigPath();
    let config: { servers?: Record<string, unknown>; inputs?: unknown[] } = { servers: {}, inputs: [] };

    if (fs.existsSync(configPath)) {
        try {
            config = JSON.parse(fs.readFileSync(configPath, 'utf-8'));
        } catch {
            // corrupted file, start fresh
        }
    }

    if (!config.servers) {
        config.servers = {};
    }

    config.servers[MCP_SERVER_ID] = {
        type: 'stdio',
        command: serverExe,
        args: [],
        version: '1.0.0'
    };

    const dir = path.dirname(configPath);
    if (!fs.existsSync(dir)) {
        fs.mkdirSync(dir, { recursive: true });
    }
    fs.writeFileSync(configPath, JSON.stringify(config, null, '\t'), 'utf-8');
}

function unregisterMcpServer() {
    const configPath = getMcpConfigPath();
    if (!fs.existsSync(configPath)) {
        return;
    }

    try {
        const config = JSON.parse(fs.readFileSync(configPath, 'utf-8'));
        if (config.servers && config.servers[MCP_SERVER_ID]) {
            delete config.servers[MCP_SERVER_ID];
            fs.writeFileSync(configPath, JSON.stringify(config, null, '\t'), 'utf-8');
        }
    } catch {
        // ignore read errors
    }
}

export function deactivate() {}
