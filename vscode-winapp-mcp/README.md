# WinApp MCP — Windows App Automation

**Playwright for Windows Desktop Apps** — A VS Code extension that gives AI assistants (GitHub Copilot, Claude, Cursor) full control over native Windows applications through the Model Context Protocol.

## Features

- **55 UI automation tools** — launch, inspect, click, type, drag, screenshot, and test any Windows app
- **Auto-registers** — the MCP server starts working as soon as you install the extension
- **No .NET SDK needed** — bundles a self-contained server
- **Works with minimized/locked apps** — falls back to UIA patterns automatically

## Supported App Frameworks

WinUI3, WPF, WinForms, UWP, Win32, Electron

## Installation

### From VS Code Marketplace

Search **"WinApp MCP"** in the Extensions tab, or:

```
ext install BrijesharunG.winapp-mcp
```

### From VSIX

Download the latest `.vsix` from [GitHub Releases](https://github.com/floatingbrij/desktop-pilot-mcp/releases), then:

```
code --install-extension winapp-mcp-1.7.0.vsix
```

### From npm (for Claude Desktop, Cursor, Windsurf, etc.)

```bash
npx -y winapp-mcp
```

See the [main README](https://github.com/floatingbrij/desktop-pilot-mcp#client-configurations) for client-specific config snippets.

## Requirements

- Windows 10 (1903+) or Windows 11
- VS Code 1.99+

## Commands

| Command | Description |
|---|---|
| `WinApp MCP: Register Server` | Manually register the MCP server |
| `WinApp MCP: Unregister Server` | Remove the MCP server registration |

## Links

- [Full documentation (55 tools)](https://github.com/floatingbrij/desktop-pilot-mcp/blob/main/DOCUMENTATION.md)
- [Changelog](https://github.com/floatingbrij/desktop-pilot-mcp/blob/main/CHANGELOG.md)
- [npm package](https://www.npmjs.com/package/winapp-mcp)
- [GitHub](https://github.com/floatingbrij/desktop-pilot-mcp)

## License

MIT
