# WinApp MCP — Windows App Automation

VS Code extension that provides an MCP server for automating native Windows desktop apps (WinUI3, WPF, WinForms, UWP) via UI Automation.

## Features

Once installed, VS Code automatically registers a **winapp** MCP server. Copilot (or any MCP client) can use these tools:

| Tool | Description |
|------|-------------|
| `launch_app` | Launch a Windows application by path |
| `attach_to_app` | Attach to a running app by process name |
| `attach_to_pid` | Attach to a running app by PID |
| `close_app` | Close an attached application |
| `get_snapshot` | Get the UI automation tree of the app |
| `find_elements` | Search for elements with filters (type, id, name) |
| `click_element` | Click an element by AutomationId or Name |
| `double_click_element` | Double-click an element |
| `right_click_element` | Right-click an element |
| `click_at_coordinates` | Click at absolute screen coordinates |
| `invoke_element` | Invoke (activate) an element |
| `set_element_value` | Set the value of an input element |
| `fill_form` | Fill multiple form fields in one call |
| `type_text` | Type text into the focused element |
| `press_key` | Press a keyboard key |
| `press_key_combo` | Press a key combination (e.g. Ctrl+S) |
| `scroll_element` | Scroll an element |
| `wait_for_element` | Wait for an element to appear |
| `take_screenshot` | Capture a screenshot of the app window |
| `get_focused_element` | Get the currently focused element |
| `read_element` | Read properties of a specific element |
| `find_all_elements` | Find all elements matching criteria |
| `list_apps` | List running applications |
| `list_windows` | List windows of an attached app |
| `list_desktop_windows` | List all desktop windows |
| `release_all` | Release all held keys/mouse buttons |
| `invalidate_cache` | Clear cached window references |

## Requirements

- Windows 10/11
- VS Code 1.99+ with GitHub Copilot
- .NET 8 Runtime (bundled server is self-contained, so typically not needed)

## Installation

### From .vsix file
```
code --install-extension winapp-mcp-1.0.0.vsix
```

### Manual
1. Copy the extension folder to `%USERPROFILE%\.vscode\extensions\winapp-mcp-1.0.0`
2. Restart VS Code

## How It Works

The extension bundles a .NET 8 MCP server that uses [FlaUI](https://github.com/FlaUI/FlaUI) (UI Automation) to interact with Windows applications. VS Code auto-starts it as a stdio MCP server when Copilot needs it.
