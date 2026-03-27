# Contributing to WinApp MCP

Thanks for your interest in contributing! Here's how to get started.

## Development Setup

```powershell
# Clone
git clone https://github.com/floatingbrij/desktop-pilot-mcp.git
cd desktop-pilot-mcp

# Build the MCP server
cd src
dotnet restore
dotnet build

# Run it (starts on stdio — you'll see no output, that's normal)
dotnet run
```

### Building the VS Code Extension

```powershell
cd vscode-winapp-mcp
npm install
npm run compile
npx @vscode/vsce package    # Creates .vsix file
```

## Adding a New Tool

1. Add the implementation method in `src/WinAppAutomation.cs`
2. Add the MCP wrapper in `src/WinAppTools.cs` with `[McpServerTool]` and `[Description]` attributes
3. Document the tool in `DOCUMENTATION.md`
4. Add a changelog entry in `CHANGELOG.md`

## Pull Request Process

1. Fork the repo and create a branch from `main`
2. Make your changes with clear commit messages
3. Test your changes against a real Windows application
4. Update documentation if you added/changed tools
5. Open a PR with a description of what you changed and why

## Code Style

- Follow existing C# conventions in the codebase
- All logging goes to `stderr` — `stdout` is reserved for MCP JSON-RPC
- Add `[Description]` attributes to all tool parameters
- Keep `WinAppTools.cs` as thin wrappers — business logic goes in `WinAppAutomation.cs`

## Reporting Bugs

Use [GitHub Issues](https://github.com/floatingbrij/desktop-pilot-mcp/issues) with:
- Steps to reproduce
- Expected vs actual behavior
- Target app framework (WinUI3, WPF, WinForms, etc.)
- Windows version and .NET version

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
