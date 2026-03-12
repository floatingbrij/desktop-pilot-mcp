# WinApp MCP — Complete Tool Documentation

> **53 tools** for automating Windows desktop applications via the Model Context Protocol.
>
> Each tool is callable by any MCP-compatible AI assistant (GitHub Copilot, Claude, etc.) through JSON-RPC over stdio.

---

## Table of Contents

- [Conventions](#conventions)
- [App Lifecycle](#app-lifecycle)
  - [launch_app](#launch_app)
  - [attach_to_app](#attach_to_app)
  - [attach_to_pid](#attach_to_pid)
  - [close_app](#close_app)
  - [list_apps](#list_apps)
- [Window & Element Discovery](#window--element-discovery)
  - [list_windows](#list_windows)
  - [list_desktop_windows](#list_desktop_windows)
  - [get_snapshot](#get_snapshot)
  - [get_focused_element](#get_focused_element)
  - [find_elements](#find_elements)
  - [find_all_elements](#find_all_elements)
  - [find_elements_fuzzy](#find_elements_fuzzy)
- [Read & Inspect](#read--inspect)
  - [read_element](#read_element)
  - [read_element_by_index](#read_element_by_index)
  - [get_element_bounds](#get_element_bounds)
  - [element_exists](#element_exists)
  - [get_all_values](#get_all_values)
- [Click & Interaction](#click--interaction)
  - [click_element](#click_element)
  - [double_click_element](#double_click_element)
  - [right_click_element](#right_click_element)
  - [click_at_coordinates](#click_at_coordinates)
  - [invoke_element](#invoke_element)
  - [select_option](#select_option)
  - [expand_collapse_element](#expand_collapse_element)
  - [drag_element](#drag_element)
- [Input](#input)
  - [type_text](#type_text)
  - [press_key](#press_key)
  - [press_key_combo](#press_key_combo)
  - [fill_form](#fill_form)
- [Wait & Synchronization](#wait--synchronization)
  - [wait_for_element](#wait_for_element)
  - [wait_for_condition](#wait_for_condition)
  - [wait_for_input_idle](#wait_for_input_idle)
  - [release_all](#release_all)
- [Screenshots & Visual](#screenshots--visual)
  - [take_screenshot](#take_screenshot)
  - [take_screenshot_optimized](#take_screenshot_optimized)
  - [annotate_screenshot](#annotate_screenshot)
  - [screenshot_diff](#screenshot_diff)
  - [get_tree_hash](#get_tree_hash)
- [HWND Multi-Window](#hwnd-multi-window)
  - [click_element_hwnd](#click_element_hwnd)
  - [set_value_hwnd](#set_value_hwnd)
  - [get_snapshot_hwnd](#get_snapshot_hwnd)
- [Advanced Patterns](#advanced-patterns)
  - [get_grid_item](#get_grid_item)
  - [find_item_by_property](#find_item_by_property)
  - [scroll_into_view](#scroll_into_view)
  - [realize_virtualized_item](#realize_virtualized_item)
  - [scroll_element](#scroll_element)
  - [invalidate_cache](#invalidate_cache)
- [Event Monitoring](#event-monitoring)
  - [start_event_monitor](#start_event_monitor)
  - [stop_event_monitor](#stop_event_monitor)
  - [get_event_log](#get_event_log)
- [Set Value](#set-value)
  - [set_element_value](#set_element_value)
- [Workflows & Patterns](#workflows--patterns)
- [Tips & Best Practices](#tips--best-practices)

---

## Conventions

### Parameters

- **`appId`** — Required for most tools. A string like `"app_1234"` returned by `launch_app` or `attach_to_app`. Identifies which application to target.
- **`automationId`** — The UIA AutomationId property. Most reliable selector — use it when available.
- **`name`** — The UIA Name property (visible text/label). Can match partially depending on the tool.
- **`controlType`** — Filters by UIA control type: `Button`, `Edit`, `Text`, `CheckBox`, `ComboBox`, `ListItem`, `DataGrid`, `TreeItem`, `MenuItem`, `Window`, etc.
- **`index`** — 0-based index when multiple elements match. Default `-1` means "first match". Use `find_all_elements` to discover indices.
- **`windowHandle`** — Native HWND (window handle) as a `long`. Use `list_desktop_windows` to find handles. `0` means use the app's main window.

### Return Values

All tools return a `string`. Success returns a descriptive message with the result. Errors return a string prefixed with `"ERROR: "`.

### Element Resolution

Tools that accept `automationId`, `name`, and `controlType` use this resolution order:
1. If `automationId` is provided → find by AutomationId (exact match)
2. If `name` is provided → find by Name (contains match)
3. If `controlType` is provided → further filter by control type
4. If `index` is provided → pick the Nth match

### Caching

- **Descendant cache** (2s TTL): `get_snapshot`, `find_elements`, `read_element`, `find_all_elements`, and all click/read operations use cached descendants.
- **Window cache** (30s TTL): Window lookups are cached to avoid expensive re-resolution.
- **Auto-invalidation**: Any mutation (click, type, set value, invoke) automatically invalidates the descendant cache for that app.
- **Manual invalidation**: Call `invalidate_cache` if the UI changed without a tracked mutation (e.g., async data load).

---

## App Lifecycle

### `launch_app`

Launch a Windows application by its executable path.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `exePath` | string | ✅ | Full path to the `.exe` file |
| `arguments` | string | ❌ | Command-line arguments |

**Returns:** App ID string (e.g., `"app_1234"`)

**Example:**
```
launch_app(exePath: "C:\\Windows\\notepad.exe")
→ "Launched app: app_1234\n\nUse this app ID in subsequent commands."

launch_app(exePath: "C:\\Program Files\\MyApp\\app.exe", arguments: "--debug")
→ "Launched app: app_5678\n\nUse this app ID in subsequent commands."
```

**Notes:**
- Waits 2 seconds after launch for the main window to appear
- The process must be a GUI application (not a console-only app)
- Returns immediately — use `wait_for_element` or `wait_for_input_idle` to wait for the app to be ready

---

### `attach_to_app`

Attach to an already-running process by its name.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `processName` | string | ✅ | Process name without `.exe` (e.g., `"notepad"`, `"Calculator"`) |

**Returns:** App ID string

**Example:**
```
attach_to_app(processName: "notepad")
→ "app_1234"

attach_to_app(processName: "Calculator")
→ "app_5678"
```

**Notes:**
- If multiple processes share the same name, attaches to the first one found
- Use `attach_to_pid` for precise targeting when multiple instances exist

---

### `attach_to_pid`

Attach to an already-running process by its PID.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `pid` | int | ✅ | Process ID |

**Returns:** App ID string

**Example:**
```
attach_to_pid(pid: 12345)
→ "app_12345"
```

---

### `close_app`

Close a tracked application.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID from launch/attach |

**Returns:** Confirmation message

---

### `list_apps`

List all currently tracked applications and their PIDs.

**No parameters.**

**Returns:** Formatted list of tracked apps with process IDs and responding status.

---

## Window & Element Discovery

### `list_windows`

List all top-level windows for a tracked application.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |

**Returns:** List of windows with titles, handles, and bounds.

---

### `list_desktop_windows`

List all visible top-level windows on the desktop. Useful for finding apps to attach to and discovering HWND values for multi-window operations.

**No parameters.**

**Returns:** List of all visible desktop windows with process names, PIDs, titles, and **HWND handles**.

**Tip:** Use the returned HWND values with HWND-targeted tools (`click_element_hwnd`, `set_value_hwnd`, `get_snapshot_hwnd`).

---

### `get_snapshot`

Get a tree snapshot of the UI element hierarchy. This is the primary tool for understanding the app's structure — similar to browser DOM inspection.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `maxDepth` | int | ❌ | 3 | Maximum tree depth to traverse |

**Returns:** Indented text tree showing each element's control type, name, AutomationId, and key properties.

**Example:**
```
get_snapshot(appId: "app_1234", maxDepth: 4)
→ Window "Untitled - Notepad" [AutomationId: ]
    MenuBar "Application" [AutomationId: MenuBar]
        MenuItem "File" [AutomationId: File]
        MenuItem "Edit" [AutomationId: Edit]
    Edit "" [AutomationId: RichEditBox]
    StatusBar "" [AutomationId: StatusBar]
        Text "Ln 1, Col 1" [AutomationId: ]
```

**Tips:**
- Start with `maxDepth: 3` (default) — increase only if you need deeper elements
- Uses descendant cache (2s TTL) — repeated calls are fast
- Look for `AutomationId` values — they're the most reliable selectors

---

### `get_focused_element`

Get information about the currently focused UI element.

**No parameters.**

**Returns:** Detailed properties of the focused element including AutomationId, name, control type, bounds, and value.

---

### `find_elements`

Search for UI elements with filters. Faster and more targeted than `get_snapshot` when you know what you're looking for.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `controlType` | string | ❌ | — | Filter by control type (e.g., `"Button"`, `"Edit"`, `"ComboBox"`) |
| `idContains` | string | ❌ | — | Filter by AutomationId containing this text |
| `nameContains` | string | ❌ | — | Filter by Name containing this text |
| `maxResults` | int | ❌ | 50 | Maximum results to return |

**Returns:** List of matching elements with their properties.

**Example:**
```
find_elements(appId: "app_5678", controlType: "Button")
→ All buttons in the app

find_elements(appId: "app_5678", nameContains: "Invoice", controlType: "ListItem")
→ All list items containing "Invoice" in their name
```

---

### `find_all_elements`

Find all matching UI elements and list them with their index. Use this to discover which index to pass to `click_element`, `read_element_by_index`, or `set_element_value`.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `automationId` | string | ❌ | AutomationId to search for |
| `name` | string | ❌ | Name/text to search for |
| `controlType` | string | ❌ | Control type filter |

**Returns:** Numbered list of all matching elements.

**Example:**
```
find_all_elements(appId: "app_5678", controlType: "Button")
→ [0] Button "Save" [AutomationId: SaveButton]
  [1] Button "Cancel" [AutomationId: CancelButton]
  [2] Button "Delete" [AutomationId: DeleteButton]
```

---

### `find_elements_fuzzy`

Search for UI elements using fuzzy matching. Tolerates typos, partial names, and word reordering using Levenshtein distance.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `controlType` | string | ❌ | — | Control type filter |
| `idContains` | string | ❌ | — | Fuzzy match AutomationId |
| `nameContains` | string | ❌ | — | Fuzzy match Name |
| `maxResults` | int | ❌ | 50 | Maximum results |
| `windowHandle` | long | ❌ | 0 | HWND to target specific window (0 = main) |

**Returns:** Matching elements ranked by relevance.

**How fuzzy matching works:**
1. **All-words-present:** All words in the query must be present in the target (order doesn't matter)
2. **Levenshtein distance:** Allows up to 30% character differences based on query length
3. **Substring optimization:** If query is shorter than target, checks all substrings

**Example:**
```
find_elements_fuzzy(appId: "app_5678", nameContains: "Custmer Name")  # typo in "Customer"
→ Still finds elements with Name "Customer Name"

find_elements_fuzzy(appId: "app_5678", nameContains: "name customer")  # reversed words
→ Finds "Customer Name" elements
```

---

## Read & Inspect

### `read_element`

Read detailed properties of a specific UI element.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `automationId` | string | ❌ | AutomationId of the element |
| `name` | string | ❌ | Name/text of the element |
| `controlType` | string | ❌ | Control type filter |

**Returns:** Full property dump including AutomationId, Name, ControlType, BoundingRectangle, IsEnabled, IsOffscreen, Value, Toggle state, Selection state, and supported patterns.

---

### `read_element_by_index`

Read properties of an element by its index from `find_all_elements`.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `index` | int | ✅ | 0-based index from `find_all_elements` |
| `automationId` | string | ❌ | AutomationId filter |
| `name` | string | ❌ | Name filter |
| `controlType` | string | ❌ | Control type filter |

**Returns:** Same as `read_element` but for the indexed element.

---

### `get_element_bounds`

Get the bounding rectangle (screen coordinates) of a UI element.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type filter |
| `index` | int | ❌ | -1 | Index when multiple match |

**Returns:** X, Y, Width, Height in screen coordinates.

---

### `element_exists`

Quick boolean check if a UI element exists. Much faster than `read_element` when you only need to know presence.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `automationId` | string | ❌ | AutomationId |
| `name` | string | ❌ | Name |
| `controlType` | string | ❌ | Control type filter |

**Returns:** `"true"` or `"false"`

**Example:**
```
element_exists(appId: "app_5678", name: "Delete", controlType: "Button")
→ "true"
```

---

### `get_all_values`

Read values of ALL editable fields in the app window at once. Great for verifying form state after filling.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |

**Returns:** All TextBox, ComboBox, CheckBox, ToggleSwitch values in the current window.

---

## Click & Interaction

### `click_element`

Click a UI element. The most commonly used interaction tool.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId of the element |
| `name` | string | ❌ | — | Name/text of the element |
| `controlType` | string | ❌ | — | Control type filter |
| `index` | int | ❌ | -1 | Index when multiple match (use `find_all_elements` to discover) |

**Returns:** Confirmation of click with element details.

**Example:**
```
click_element(appId: "app_5678", name: "Save")
→ "Clicked Button 'Save'"

click_element(appId: "app_5678", automationId: "InvoiceListItem", index: 2)
→ "Clicked ListItem [index 2]"
```

**Notes:**
- Invalidates the descendant cache after click
- Use `invoke_element` if click doesn't trigger the action (common with ContentDialogs)
- Use `index` when multiple elements share the same name (e.g., "Edit" buttons in a list)

---

### `double_click_element`

Double-click a UI element. Same parameters as `click_element`.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type filter |
| `index` | int | ❌ | -1 | Index when multiple match |

---

### `right_click_element`

Right-click a UI element to open a context menu. Same parameters as `click_element`.

---

### `click_at_coordinates`

Click at absolute screen coordinates. Use as a fallback when elements can't be targeted by properties.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `x` | int | ✅ | Absolute X screen coordinate |
| `y` | int | ✅ | Absolute Y screen coordinate |

**Tip:** Use `get_element_bounds` to find coordinates of elements, or `annotate_screenshot` for visual reference.

---

### `invoke_element`

Invoke a UI element using UIA InvokePattern or TogglePattern. More reliable than `click_element` for:
- Buttons inside ContentDialogs
- Menu items
- CheckBoxes (toggle state)
- Elements that don't respond to simulated mouse clicks

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type filter |
| `index` | int | ❌ | -1 | Index when multiple match |

---

### `select_option`

Select an option in a ComboBox/dropdown. Handles the entire expand → find → select flow atomically.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `optionText` | string | ✅ | — | Text of option to select (case-insensitive, partial match) |
| `automationId` | string | ❌ | — | AutomationId of the ComboBox |
| `name` | string | ❌ | — | Name of the ComboBox |
| `index` | int | ❌ | -1 | Index when multiple ComboBoxes match |

**Returns:** Confirmation of selection.

**Example:**
```
select_option(appId: "app_5678", automationId: "StatusComboBox", optionText: "Active")
→ "Selected 'Active' in ComboBox"
```

**Why use this instead of click:**
- `click_element` requires three calls: click ComboBox → wait → click option
- `select_option` does it in one call, handles expand/collapse, and is more reliable

---

### `expand_collapse_element`

Expand, collapse, or toggle a tree item, menu item, or ComboBox using the ExpandCollapsePattern.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type (TreeItem, MenuItem, ComboBox) |
| `action` | string | ❌ | `"toggle"` | `"expand"`, `"collapse"`, or `"toggle"` |
| `index` | int | ❌ | -1 | Index when multiple match |
| `windowHandle` | long | ❌ | 0 | HWND for specific window |

**Returns:** Previous state and new state of the element.

---

### `drag_element`

Drag from one UI element to another. Performs a smooth mouse drag between center points.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `sourceAutomationId` | string | ❌ | — | Source element AutomationId |
| `sourceName` | string | ❌ | — | Source element Name |
| `sourceControlType` | string | ❌ | — | Source control type filter |
| `sourceIndex` | int | ❌ | -1 | Source index |
| `targetAutomationId` | string | ❌ | — | Target element AutomationId |
| `targetName` | string | ❌ | — | Target element Name |
| `targetControlType` | string | ❌ | — | Target control type filter |
| `targetIndex` | int | ❌ | -1 | Target index |

---

## Input

### `type_text`

Type text into a text field. Finds the element by AutomationId or name and sets its value.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `text` | string | ✅ | Text to type or set |
| `automationId` | string | ❌ | AutomationId of the text field |
| `name` | string | ❌ | Name/label of the text field |

---

### `press_key`

Press a single keyboard key.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `key` | string | ✅ | Key name: `RETURN`, `TAB`, `ESCAPE`, `DELETE`, `BACK`, `SPACE`, `F1`-`F12`, `UP`, `DOWN`, `LEFT`, `RIGHT`, `HOME`, `END`, `PAGEUP`, `PAGEDOWN` |

---

### `press_key_combo`

Press a keyboard shortcut combination.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `keys` | string[] | ✅ | Array of key names, e.g., `["CONTROL", "KEY_S"]` or `["ALT", "F4"]`. Modifier keys: `CONTROL`, `SHIFT`, `ALT`, `LWIN` |

**Example:**
```
press_key_combo(keys: ["CONTROL", "KEY_S"])    → Ctrl+S (Save)
press_key_combo(keys: ["CONTROL", "KEY_Z"])    → Ctrl+Z (Undo)
press_key_combo(keys: ["ALT", "F4"])           → Alt+F4 (Close)
press_key_combo(keys: ["CONTROL", "SHIFT", "KEY_N"])  → Ctrl+Shift+N
```

---

### `fill_form`

Fill multiple form fields in a single call. Dramatically faster than sequential `type_text` calls.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `fieldsJson` | string | ✅ | JSON object mapping field AutomationId/Name to value |

**Example:**
```
fill_form(appId: "app_5678", fieldsJson: "{
  \"CustomerNameComboBox\": \"Acme Corp\",
  \"InvoiceNumber\": \"INV-001\",
  \"Amount\": \"1500.00\",
  \"DueDate\": \"2026-03-15\"
}")
→ "Filled 4/4 fields successfully"
```

**Notes:**
- Each field is matched by AutomationId first, then by Name
- Failed fields are reported individually — successful fields are still set
- Invalidates cache once after all fields are filled

---

## Wait & Synchronization

### `wait_for_element`

Wait for a UI element to appear. Essential after navigation, dialog opens, or async data loads.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId to wait for |
| `name` | string | ❌ | — | Name to wait for |
| `controlType` | string | ❌ | — | Control type filter |
| `timeoutMs` | int | ❌ | 10000 | Timeout in milliseconds |

**Returns:** Element details once found, or error on timeout.

---

### `wait_for_condition`

Wait until a UI element's property reaches a specific value. More precise than `wait_for_element`.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `property` | string | ✅ | — | Property to monitor: `name`, `isEnabled`, `isOffscreen`, `text`, `value`, `isChecked`, `selectedItem` |
| `expectedValue` | string | ✅ | — | Expected value (case-insensitive) |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type filter |
| `timeoutMs` | int | ❌ | 10000 | Timeout in milliseconds |

**Example:**
```
# Wait for a button to become enabled
wait_for_condition(appId: "app_5678", automationId: "SaveButton",
  property: "isEnabled", expectedValue: "true")

# Wait for status text to change
wait_for_condition(appId: "app_5678", automationId: "StatusLabel",
  property: "name", expectedValue: "Sent")
```

---

### `wait_for_input_idle`

Wait for a window to become idle and ready for input. Use after launching apps, navigating pages, or triggering async operations.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `timeoutMs` | int | ❌ | 10000 | Timeout in milliseconds |
| `windowHandle` | long | ❌ | 0 | HWND for specific window (0 = main) |

**Notes:**
- Uses WindowPattern.WaitForInputIdle first
- Falls back to Process.WaitForInputIdle
- Final fallback: a short delay

---

### `release_all`

**EMERGENCY:** Release ALL stuck modifier keys (Ctrl, Shift, Alt, Win) and mouse buttons. Call this if automation stopped unexpectedly and your keyboard/mouse feels stuck.

**No parameters.**

**Returns:** Confirmation message.

**When to use:**
- Automation crashed mid-key-press and Ctrl/Shift/Alt is stuck
- Mouse button is stuck in "held" state
- Keyboard shortcuts aren't working normally after automation

---

## Screenshots & Visual

### `take_screenshot`

Capture the app's main window as a PNG file.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `outputPath` | string | ✅ | Full file path to save (e.g., `"C:\\temp\\screenshot.png"`) |

**Returns:** Confirmation with file path and image dimensions.

---

### `take_screenshot_optimized`

Screenshot with optional auto-resize to fit within an LLM token budget. Prevents large screenshots from consuming excessive context.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `outputPath` | string | ✅ | — | File path to save |
| `maxTokens` | int | ❌ | 0 | Token budget (0 = no resize). ~1000 tokens ≈ 768×768 pixels |
| `windowHandle` | long | ❌ | 0 | HWND for specific window (0 = main) |

**How it works:**
- Calculates max pixels as `maxTokens × 750`
- If the image exceeds that pixel count, resizes using bicubic interpolation while preserving aspect ratio
- Quality is maintained through high-quality bicubic downscaling

**Example:**
```
# Normal screenshot — full resolution
take_screenshot_optimized(appId: "app_5678", outputPath: "C:\\temp\\full.png")

# Token-limited screenshot — auto-shrinks if needed
take_screenshot_optimized(appId: "app_5678", outputPath: "C:\\temp\\small.png", maxTokens: 1000)
```

---

### `annotate_screenshot`

Take a screenshot with red bounding boxes drawn around specified elements. Useful for visual verification of element locations.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |
| `outputPath` | string | ✅ | File path to save |
| `automationIds` | string[] | ✅ | Array of AutomationIds to highlight |

**Example:**
```
annotate_screenshot(appId: "app_5678", outputPath: "C:\\temp\\annotated.png",
  automationIds: ["SaveButton", "CustomerComboBox", "TotalAmount"])
→ Screenshot with red boxes around Save button, Customer dropdown, and Total amount
```

---

### `screenshot_diff`

Compare two screenshot images pixel-by-pixel. Returns the percentage of changed pixels and saves a highlighted diff image.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `imagePath1` | string | ✅ | Path to first screenshot |
| `imagePath2` | string | ✅ | Path to second screenshot |
| `outputPath` | string | ❌ | Path to save the diff image (changes shown in red) |

**Returns:** Diff percentage and file path of the diff image.

**Use case:** Visual regression testing — take a baseline screenshot, make changes, compare.

---

### `get_tree_hash`

Get a hash of the current UI tree structure. Compare hashes to quickly detect if the UI changed.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |

**Returns:** Hash string of the UI tree.

**Use case:** After an action, compare tree hashes to verify the UI actually changed (navigation succeeded, data loaded, etc.)

---

## HWND Multi-Window

These tools target specific windows by their native handle (HWND). Essential for multi-window apps, popups, dialogs, and system-level windows.

**How to get HWND values:** Call `list_desktop_windows` — each window entry includes its HWND.

### `click_element_hwnd`

Click a UI element in a specific window identified by HWND.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `windowHandle` | long | ✅ | — | Native window handle from `list_desktop_windows` |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type filter |
| `fuzzyMatch` | bool | ❌ | false | Enable fuzzy name matching (Levenshtein) |

**Example:**
```
# Click "OK" in a system dialog window
click_element_hwnd(windowHandle: 12345678, name: "OK", controlType: "Button")
```

---

### `set_value_hwnd`

Set a text value in a specific window by HWND.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `windowHandle` | long | ✅ | — | Native window handle |
| `value` | string | ✅ | — | Value to set |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type filter |
| `fuzzyMatch` | bool | ❌ | false | Enable fuzzy matching |

---

### `get_snapshot_hwnd`

Get a UI snapshot of a specific window by HWND.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `windowHandle` | long | ✅ | — | Native window handle |
| `maxDepth` | int | ❌ | 3 | Maximum tree depth |

---

## Advanced Patterns

### `get_grid_item`

Access a specific cell in a DataGrid or Table by row and column index using the GridPattern.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `row` | int | ✅ | — | 0-based row index |
| `column` | int | ✅ | — | 0-based column index |
| `automationId` | string | ❌ | — | AutomationId of the grid |
| `name` | string | ❌ | — | Name of the grid |
| `controlType` | string | ❌ | — | Control type (default: DataGrid) |
| `index` | int | ❌ | -1 | Index when multiple grids match |
| `windowHandle` | long | ❌ | 0 | HWND for specific window |

**Returns:** Cell element details including value, bounds, and properties.

**Example:**
```
get_grid_item(appId: "app_5678", row: 0, column: 2, automationId: "InvoiceGrid")
→ Cell at [0,2]: "INV-001" (Text)
```

---

### `find_item_by_property`

Find an item inside a container (List, Tree, DataGrid) by property value. Uses the ItemContainerPattern for efficient search in large and virtualized lists.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `propertyName` | string | ❌ | — | Property to search: `"Name"` or `"AutomationId"` |
| `value` | string | ❌ | — | Value to match (case-insensitive) |
| `automationId` | string | ❌ | — | AutomationId of the container |
| `name` | string | ❌ | — | Name of the container |
| `controlType` | string | ❌ | — | Container type (List, Tree, DataGrid) |
| `index` | int | ❌ | -1 | Index when multiple containers match |
| `windowHandle` | long | ❌ | 0 | HWND for specific window |

**Notes:**
- Best for virtualized lists where items may not be in the UI tree
- Falls back to manual descendant search if ItemContainerPattern isn't supported

---

### `scroll_into_view`

Scroll a UI element into the visible area. Essential for off-screen items in long lists, tree views, or scrollable panels.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type filter |
| `index` | int | ❌ | -1 | Index when multiple match |
| `windowHandle` | long | ❌ | 0 | HWND for specific window |

**How it works:**
1. Tries `ScrollItemPattern.ScrollIntoView()` on the element itself
2. Falls back to finding the parent scroll container and adjusting scroll position

---

### `realize_virtualized_item`

Force a virtualized item to be fully loaded in the UI tree. Critical for WinUI3 applications that use `ListView`, `GridView`, or `ItemsRepeater` — these controls virtualize off-screen items to save memory, which means they don't exist in the UIA tree until realized.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type (ListItem, TreeItem, DataItem) |
| `index` | int | ❌ | -1 | Index when multiple match |
| `windowHandle` | long | ❌ | 0 | HWND for specific window |

**When to use:**
- When `find_elements` can't find an item you know exists in the list
- Before reading properties of items that are scrolled out of view
- When working with WinUI3 `ListView` or `GridView` controls

---

### `scroll_element`

Scroll within a scrollable container (ScrollViewer, ListView, TreeView).

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `automationId` | string | ❌ | — | AutomationId of the scrollable element |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type filter |
| `clicks` | int | ❌ | 3 | Number of scroll steps |
| `direction` | string | ❌ | `"down"` | `"up"`, `"down"`, `"left"`, `"right"` |
| `index` | int | ❌ | -1 | Index when multiple match |

---

### `invalidate_cache`

Force-refresh the cached window reference for an app. Call this when:
- The app opened a new window/dialog
- The main window changed or was replaced
- You suspect the cached reference is stale

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `appId` | string | ✅ | App ID |

---

## Event Monitoring

Monitor UI Automation events in real-time. Useful for debugging async UI updates, animations, and background data loads.

### `start_event_monitor`

Start monitoring UI automation events. Returns a session ID for tracking.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `eventType` | string | ✅ | — | `"focus"` (focus changes), `"structurechanged"` (elements added/removed), `"propertychanged"` (property changes) |
| `automationId` | string | ❌ | — | Scope to this element (null = entire window) |
| `name` | string | ❌ | — | Scope to this element |
| `controlType` | string | ❌ | — | Scope control type filter |
| `windowHandle` | long | ❌ | 0 | HWND for specific window |

**Returns:** Session ID string.

**Example:**
```
# Monitor all focus changes in the app
start_event_monitor(appId: "app_5678", eventType: "focus")
→ "Started focus monitor: session_abc123"

# Monitor structure changes in a specific list
start_event_monitor(appId: "app_5678", eventType: "structurechanged",
  automationId: "InvoiceListView")
→ "Started structurechanged monitor: session_def456"
```

**Notes:**
- Each session has a 500-event ring buffer (oldest events are dropped when full)
- Multiple sessions can run simultaneously
- Events are timestamped

---

### `stop_event_monitor`

Stop one or all event monitoring sessions.

| Parameter | Type | Required | Description |
|:---|:---|:---|:---|
| `sessionId` | string | ❌ | Session ID to stop (omit to stop all) |

---

### `get_event_log`

Read captured events from a monitoring session.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `sessionId` | string | ❌ | — | Session ID (omit for all sessions) |
| `maxCount` | int | ❌ | 100 | Maximum recent events to return |

**Returns:** Formatted event log with timestamps, event types, and element details.

---

## Set Value

### `set_element_value`

Set text value on a UI element by index. Unlike `type_text` which finds by name, this can target specific elements using indices from `find_all_elements`.

| Parameter | Type | Required | Default | Description |
|:---|:---|:---|:---|:---|
| `appId` | string | ✅ | — | App ID |
| `text` | string | ✅ | — | Text value to set |
| `automationId` | string | ❌ | — | AutomationId |
| `name` | string | ❌ | — | Name |
| `controlType` | string | ❌ | — | Control type (e.g., `"Edit"`) |
| `index` | int | ❌ | -1 | Index when multiple match |

---

## Workflows & Patterns

### Standard CRUD Test Flow

```
1. attach_to_app("MyApp")                           → get appId
2. click_element(appId, name: "Invoices")            → navigate
3. wait_for_element(appId, name: "New")              → page loaded
4. click_element(appId, name: "New")                 → open form
5. wait_for_element(appId, automationId: "InvoiceForm")
6. fill_form(appId, { "Customer": "Acme", ... })     → fill
7. click_element(appId, name: "Save")                → save
8. wait_for_element(appId, name: "Invoice Details")   → detail loaded
9. take_screenshot(appId, "C:\\evidence\\created.png") → evidence
10. click_element(appId, name: "Edit")                → open edit
11. set_element_value(appId, "Updated", automationId: "CustomerName")
12. click_element(appId, name: "Save")                → save edit
13. click_element(appId, name: "Delete")              → delete
14. wait_for_element(appId, name: "Confirm")          → dialog
15. invoke_element(appId, name: "Yes")                → confirm
```

### Multi-Window Dialog Flow

```
1. list_desktop_windows()                            → find HWND of dialog
2. get_snapshot_hwnd(windowHandle: 12345)             → see dialog tree
3. set_value_hwnd(windowHandle: 12345, value: "test", name: "File name")
4. click_element_hwnd(windowHandle: 12345, name: "Save")
```

### Visual Regression Test

```
1. take_screenshot(appId, "C:\\baseline\\before.png")
2. # ... make changes ...
3. take_screenshot(appId, "C:\\baseline\\after.png")
4. screenshot_diff("before.png", "after.png", "diff.png")
→ "Diff: 2.4% pixels changed. See diff.png"
```

### Event-Driven Debugging

```
1. start_event_monitor(appId, eventType: "structurechanged")  → session123
2. click_element(appId, name: "Load Data")
3. wait_for_condition(appId, property: "name", expectedValue: "Loaded",
     automationId: "StatusText", timeoutMs: 15000)
4. get_event_log(sessionId: "session123")
→ Shows exactly what elements were added/removed during data load
5. stop_event_monitor(sessionId: "session123")
```

---

## Tips & Best Practices

### Element Selection
1. **Always prefer AutomationId** — it's stable across app updates. Names can change with localization.
2. **Use `get_snapshot` first** — understand the UI tree before trying to interact.
3. **Use `find_all_elements` when clicking fails** — there might be multiple matches; specify the index.
4. **Use `find_elements_fuzzy` for dynamic content** — when element names contain dates, IDs, or other variable parts.

### Performance
1. **Don't call `get_snapshot` repeatedly** — use `find_elements` or `element_exists` for targeted queries.
2. **Use `fill_form` instead of multiple `type_text` calls** — one call fills everything.
3. **Use `select_option` instead of click→wait→click for dropdowns** — more reliable and faster.
4. **Trust the cache** — repeated reads within 2 seconds are served from cache automatically.

### Reliability
1. **Always `wait_for_element` after navigation** — don't assume the page loaded instantly.
2. **Use `wait_for_input_idle` after app launch** — the window may not be ready for input immediately.
3. **Use `invoke_element` for ContentDialogs** — `click_element` may fail on overlay dialogs.
4. **Call `release_all` if things feel stuck** — resets all modifier keys and mouse buttons.
5. **Use `invalidate_cache` after async updates** — if the UI changed without a tracked action.

### WinUI3 Specific
1. **Use `realize_virtualized_item` before reading list items** — WinUI3 virtualizes off-screen items.
2. **Check for `AutomationProperties.AutomationId`** — WinUI3 controls may not expose AutomationId unless explicitly set in XAML.
3. **Use HWND tools for ContentDialogs** — they often appear as separate top-level windows.

### Debugging
1. **Use `get_tree_hash` to detect UI changes** — compare before/after hashes.
2. **Use `start_event_monitor` to trace async behavior** — see exactly what changed and when.
3. **Use `annotate_screenshot` for visual confirmation** — red boxes show where elements are on screen.
4. **Use `screenshot_diff` for regression testing** — highlights pixel-level changes between runs.

---

*For questions, issues, or feature requests, please open an issue on GitHub.*
