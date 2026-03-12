using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace WinAppMCP;

/// <summary>
/// MCP tools for Windows app UI automation.
/// Each method is exposed as an MCP tool callable by AI assistants.
/// </summary>
[McpServerToolType]
public sealed class WinAppTools
{
    private static readonly WinAppAutomation _auto = new();

    // ── App Lifecycle ────────────────────────────────────────────────

    [McpServerTool, Description("Launch a Windows application by its executable path. Returns an app ID for future commands.")]
    public static string launch_app(
        [Description("Full path to the .exe file")] string exePath,
        [Description("Optional command-line arguments")] string? arguments = null)
    {
        string id = _auto.LaunchApp(exePath, arguments);
        return $"Launched app: {id}\n\nUse this app ID in subsequent commands.";
    }

    [McpServerTool, Description("Attach to an already-running process by its name (e.g. 'notepad', 'Calculator'). Returns an app ID.")]
    public static string attach_to_app(
        [Description("Process name without .exe (e.g. 'notepad', 'Calculator')")] string processName)
    {
        return _auto.AttachToProcess(processName);
    }

    [McpServerTool, Description("Attach to an already-running process by its PID. Returns an app ID.")]
    public static string attach_to_pid(
        [Description("Process ID")] int pid)
    {
        return _auto.AttachToProcess(pid);
    }

    [McpServerTool, Description("Close a tracked application.")]
    public static string close_app(
        [Description("App ID returned by launch_app or attach_to_app")] string appId)
    {
        return _auto.CloseApp(appId);
    }

    [McpServerTool, Description("List all currently tracked applications and their PIDs.")]
    public static string list_apps()
    {
        return _auto.ListApps();
    }

    // ── Window & Element Discovery ───────────────────────────────────

    [McpServerTool, Description("List all top-level windows for a tracked application.")]
    public static string list_windows(
        [Description("App ID")] string appId)
    {
        return _auto.ListWindows(appId);
    }

    [McpServerTool, Description("List all visible top-level windows on the desktop. Useful for finding apps to attach to.")]
    public static string list_desktop_windows()
    {
        return _auto.ListDesktopWindows();
    }

    [McpServerTool, Description("Get a tree snapshot of the UI element hierarchy of the app's main window. Like browser DOM inspection but for native Windows apps.")]
    public static string get_snapshot(
        [Description("App ID")] string appId,
        [Description("Maximum depth to traverse (default 3, increase for deeper inspection)")] int maxDepth = 3)
    {
        return _auto.GetSnapshot(appId, maxDepth);
    }

    [McpServerTool, Description("Get the currently focused UI element's information.")]
    public static string get_focused_element()
    {
        return _auto.GetFocusedElement();
    }

    // ── Read / Inspect ───────────────────────────────────────────────

    [McpServerTool, Description("Read detailed properties of a specific UI element. Provide at least one of automationId or name to find the element.")]
    public static string read_element(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter: Button, Edit, Text, CheckBox, ComboBox, ListItem, DataGrid, etc.")] string? controlType = null)
    {
        return _auto.ReadElement(appId, automationId, name, controlType);
    }

    // ── Click Actions ────────────────────────────────────────────────

    [McpServerTool, Description("Find all matching UI elements and list them with their index. Use this when multiple elements share the same automationId or name, to discover which index to use with click_element.")]
    public static string find_all_elements(
        [Description("App ID")] string appId,
        [Description("AutomationId to search for")] string? automationId = null,
        [Description("Name/text to search for")] string? name = null,
        [Description("Control type filter (e.g. 'Button', 'Edit')")] string? controlType = null)
    {
        return _auto.FindAllElements(appId, automationId, name, controlType);
    }

    [McpServerTool, Description("Click a UI element. Provide at least one of automationId or name to find the element. Use 'index' when multiple elements match (use find_all_elements to discover indices).")]
    public static string click_element(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter (e.g. 'Button', 'ListItem')")] string? controlType = null,
        [Description("0-based index when multiple elements match. Use find_all_elements to see available indices. -1 means first match (default).")] int index = -1)
    {
        return _auto.ClickElement(appId, automationId, name, controlType, index);
    }

    [McpServerTool, Description("Double-click a UI element.")]
    public static string double_click_element(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("0-based index when multiple elements match (-1 = first match)")] int index = -1)
    {
        return _auto.DoubleClickElement(appId, automationId, name, controlType, index);
    }

    [McpServerTool, Description("Right-click a UI element to open context menu.")]
    public static string right_click_element(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("0-based index when multiple elements match (-1 = first match)")] int index = -1)
    {
        return _auto.RightClickElement(appId, automationId, name, controlType, index);
    }

    [McpServerTool, Description("Click at absolute screen coordinates within the app window. Use when elements can't be targeted by automationId/name.")]
    public static string click_at_coordinates(
        [Description("App ID")] string appId,
        [Description("Absolute X screen coordinate")] int x,
        [Description("Absolute Y screen coordinate")] int y)
    {
        return _auto.ClickAtPoint(appId, x, y);
    }

    [McpServerTool, Description("Invoke a UI element using UIA InvokePattern or TogglePattern. Use this for buttons in dialogs, ContentDialogs, or when click_element doesn't trigger the action.")]
    public static string invoke_element(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("0-based index when multiple elements match (-1 = first match)")] int index = -1)
    {
        return _auto.InvokeElement(appId, automationId, name, controlType, index);
    }

    [McpServerTool, Description("Set text value on a UI element by index. Unlike type_text which finds by name, this can target specific elements using find_all_elements indices. Works on Edit/TextBox controls.")]
    public static string set_element_value(
        [Description("App ID")] string appId,
        [Description("Text value to set")] string text,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter (e.g. 'Edit')")] string? controlType = null,
        [Description("0-based index when multiple elements match (-1 = first match). Use find_all_elements to see indices.")] int index = -1)
    {
        return _auto.SetElementValue(appId, text, automationId, name, controlType, index);
    }

    [McpServerTool, Description("Scroll within a UI element (e.g. ScrollViewer, ListView). Finds the element and scrolls it. Use for reaching off-screen content.")]
    public static string scroll_element(
        [Description("App ID")] string appId,
        [Description("AutomationId of the scrollable element")] string? automationId = null,
        [Description("Name of the scrollable element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("Number of scroll steps (default 3)")] int clicks = 3,
        [Description("Scroll direction: 'up', 'down', 'left', 'right' (default 'down')")] string direction = "down",
        [Description("0-based index when multiple elements match (-1 = first match)")] int index = -1)
    {
        return _auto.ScrollElement(appId, automationId, name, controlType, clicks, direction, index);
    }

    // ── Type / Input ─────────────────────────────────────────────────

    [McpServerTool, Description("Type text into a text field or editable element. Finds the element by automationId or name.")]
    public static string type_text(
        [Description("App ID")] string appId,
        [Description("Text to type or set")] string text,
        [Description("AutomationId of the text field")] string? automationId = null,
        [Description("Name/label of the text field")] string? name = null)
    {
        return _auto.TypeText(appId, text, automationId, name);
    }

    [McpServerTool, Description("Press a single keyboard key (e.g. RETURN, TAB, ESCAPE, DELETE, BACK, F5).")]
    public static string press_key(
        [Description("Key name: RETURN, TAB, ESCAPE, DELETE, BACK, SPACE, F1-F12, UP, DOWN, LEFT, RIGHT, HOME, END, etc.")] string key)
    {
        return _auto.PressKey(key);
    }

    [McpServerTool, Description("Press a keyboard shortcut combination (e.g. Ctrl+S, Alt+F4). Provide keys as array.")]
    public static string press_key_combo(
        [Description("Array of key names, e.g. ['CONTROL','KEY_S'] or ['ALT','F4']. Modifier keys: CONTROL, SHIFT, ALT, LWIN.")] string[] keys)
    {
        return _auto.PressKeyCombo(keys);
    }

    [McpServerTool, Description("EMERGENCY: Release ALL stuck modifier keys (Ctrl, Shift, Alt, Win) and mouse buttons. Call this if the system mouse/keyboard stops working correctly after automation.")]
    public static string release_all()
    {
        return _auto.ReleaseAll();
    }

    // ── Wait ─────────────────────────────────────────────────────────

    [McpServerTool, Description("Wait for a UI element to appear. Useful after navigation or dialog opens.")]
    public static string wait_for_element(
        [Description("App ID")] string appId,
        [Description("AutomationId to wait for")] string? automationId = null,
        [Description("Name to wait for")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("Timeout in milliseconds (default 10000)")] int timeoutMs = 10000)
    {
        return _auto.WaitForElement(appId, automationId, name, controlType, timeoutMs);
    }

    // ── Screenshot ───────────────────────────────────────────────────

    [McpServerTool, Description("Take a screenshot of the app's main window and save it to a file.")]
    public static string take_screenshot(
        [Description("App ID")] string appId,
        [Description("Full file path to save the screenshot (e.g. C:\\temp\\screenshot.png)")] string outputPath)
    {
        return _auto.TakeScreenshot(appId, outputPath);
    }

    // ── Batch & Filtered Operations ──────────────────────────────────

    [McpServerTool, Description("Fill multiple form fields at once in a single call. Much faster than calling type_text multiple times. Fields are matched by AutomationId or Name.")]
    public static string fill_form(
        [Description("App ID")] string appId,
        [Description("JSON object mapping field AutomationId/Name to value, e.g. {\"CustomerNameComboBox\":\"Acme\",\"OrderNumber\":\"123\"}")] string fieldsJson)
    {
        try
        {
            Dictionary<string, string>? fields = JsonSerializer.Deserialize<Dictionary<string, string>>(fieldsJson);
            if (fields is null || fields.Count == 0)
                return "ERROR: No fields provided. Pass a JSON object like {\"fieldId\":\"value\"}";
            return _auto.FillForm(appId, fields);
        }
        catch (JsonException ex)
        {
            return $"ERROR: Invalid JSON — {ex.Message}. Expected format: {{\"fieldId\":\"value\"}}";
        }
    }

    [McpServerTool, Description("Search for UI elements with filters. Faster and more targeted than get_snapshot. Use to find specific control types or elements containing a keyword.")]
    public static string find_elements(
        [Description("App ID")] string appId,
        [Description("Filter by control type (e.g. 'Button', 'Edit', 'ComboBox', 'ListItem', 'Text')")] string? controlType = null,
        [Description("Filter by AutomationId containing this text")] string? idContains = null,
        [Description("Filter by Name containing this text")] string? nameContains = null,
        [Description("Maximum results to return (default 50)")] int maxResults = 50)
    {
        return _auto.FindElementsFiltered(appId, controlType, idContains, nameContains, maxResults);
    }

    [McpServerTool, Description("Invalidate the cached window reference. Call this if the app opened a new window or the main window changed.")]
    public static string invalidate_cache(
        [Description("App ID")] string appId)
    {
        return _auto.InvalidateWindowCache(appId);
    }

    // ── New Tools ────────────────────────────────────────────────────

    [McpServerTool, Description("Read detailed properties of a specific UI element by index. Use find_all_elements first to discover indices. Completes the read/write symmetry with set_element_value.")]
    public static string read_element_by_index(
        [Description("App ID")] string appId,
        [Description("0-based index from find_all_elements")] int index,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null)
    {
        return _auto.ReadElementByIndex(appId, automationId, name, controlType, index);
    }

    [McpServerTool, Description("Get the bounding rectangle (screen coordinates) of a UI element.")]
    public static string get_element_bounds(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("0-based index when multiple elements match (-1 = first match)")] int index = -1)
    {
        return _auto.GetElementBounds(appId, automationId, name, controlType, index);
    }

    [McpServerTool, Description("Quick boolean check if a UI element exists. Returns 'true' or 'false'. Much faster than read_element when you only need existence.")]
    public static string element_exists(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null)
    {
        return _auto.ElementExists(appId, automationId, name, controlType);
    }

    [McpServerTool, Description("Wait until a UI element's property reaches a specific value. Properties: name, isEnabled, isOffscreen, text/value, isChecked, selectedItem.")]
    public static string wait_for_condition(
        [Description("App ID")] string appId,
        [Description("Property to monitor: name, isEnabled, isOffscreen, text, value, isChecked, selectedItem")] string property,
        [Description("Expected value (case-insensitive comparison)")] string expectedValue,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("Timeout in milliseconds (default 10000)")] int timeoutMs = 10000)
    {
        return _auto.WaitForCondition(appId, automationId, name, controlType, property, expectedValue, timeoutMs);
    }

    [McpServerTool, Description("Read the current clipboard text content. Useful for verifying copy operations.")]
    public static string get_clipboard()
    {
        return _auto.GetClipboard();
    }

    [McpServerTool, Description("Drag from one UI element to another. Performs a smooth mouse drag between the center points of source and target elements.")]
    public static string drag_element(
        [Description("App ID")] string appId,
        [Description("Source element AutomationId")] string? sourceAutomationId = null,
        [Description("Source element Name")] string? sourceName = null,
        [Description("Source control type filter")] string? sourceControlType = null,
        [Description("Source 0-based index (-1 = first match)")] int sourceIndex = -1,
        [Description("Target element AutomationId")] string? targetAutomationId = null,
        [Description("Target element Name")] string? targetName = null,
        [Description("Target control type filter")] string? targetControlType = null,
        [Description("Target 0-based index (-1 = first match)")] int targetIndex = -1)
    {
        return _auto.DragElement(appId, sourceAutomationId, sourceName, sourceControlType, sourceIndex,
            targetAutomationId, targetName, targetControlType, targetIndex);
    }

    [McpServerTool, Description("Read values of ALL editable fields (TextBox, ComboBox, CheckBox) in the app window at once. Great for verifying form state.")]
    public static string get_all_values(
        [Description("App ID")] string appId)
    {
        return _auto.GetAllValues(appId);
    }

    [McpServerTool, Description("Get a hash of the current UI tree structure. Compare hashes to detect if the UI changed (e.g. after navigation or data load).")]
    public static string get_tree_hash(
        [Description("App ID")] string appId)
    {
        return _auto.GetTreeHash(appId);
    }

    [McpServerTool, Description("Compare two screenshot images pixel-by-pixel. Returns diff percentage and saves a highlighted diff image showing changes in red.")]
    public static string screenshot_diff(
        [Description("Path to the first screenshot")] string imagePath1,
        [Description("Path to the second screenshot")] string imagePath2,
        [Description("Path to save the diff image (optional)")] string? outputPath = null)
    {
        return _auto.ScreenshotDiff(imagePath1, imagePath2, outputPath);
    }

    [McpServerTool, Description("Take a screenshot with red bounding boxes drawn around specified elements. Useful for visual verification of element locations.")]
    public static string annotate_screenshot(
        [Description("App ID")] string appId,
        [Description("Path to save the annotated screenshot")] string outputPath,
        [Description("Array of AutomationIds to highlight with bounding boxes")] string[] automationIds)
    {
        return _auto.AnnotateScreenshot(appId, outputPath, automationIds);
    }

    [McpServerTool, Description("Select an option in a ComboBox/dropdown by text. Handles expand → find item → select in one call. Much faster than click_element → wait_for_element → click_element.")]
    public static string select_option(
        [Description("App ID")] string appId,
        [Description("Text of the option to select (case-insensitive, partial match supported)")] string optionText,
        [Description("AutomationId of the ComboBox")] string? automationId = null,
        [Description("Name of the ComboBox")] string? name = null,
        [Description("0-based index when multiple ComboBoxes match (-1 = first match)")] int index = -1)
    {
        return _auto.SelectOption(appId, automationId, name, optionText, index);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── HWND-targeted tools ──────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Click a UI element in a specific window identified by HWND. Use list_desktop_windows to find HWNDs. Useful for multi-window apps.")]
    public static string click_element_hwnd(
        [Description("Native window handle (HWND) from list_desktop_windows")] long windowHandle,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("Enable fuzzy name matching (tolerates typos/partial names)")] bool fuzzyMatch = false)
    {
        return _auto.ClickElementHwnd(windowHandle, automationId, name, controlType, fuzzyMatch);
    }

    [McpServerTool, Description("Set text in a specific window identified by HWND. Use for multi-window apps when app ID targets the wrong window.")]
    public static string set_value_hwnd(
        [Description("Native window handle (HWND)")] long windowHandle,
        [Description("Value to set")] string value,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name/text of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("Enable fuzzy name matching")] bool fuzzyMatch = false)
    {
        return _auto.SetValueHwnd(windowHandle, value, automationId, name, controlType, fuzzyMatch);
    }

    [McpServerTool, Description("Get a UI snapshot of a specific window by HWND. Use for popups, dialogs, or secondary windows.")]
    public static string get_snapshot_hwnd(
        [Description("Native window handle (HWND)")] long windowHandle,
        [Description("Max tree depth (default 3)")] int maxDepth = 3)
    {
        return _auto.GetSnapshotHwnd(windowHandle, maxDepth);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Fuzzy search ─────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Search for UI elements using fuzzy matching. Tolerates typos, partial names, and word reordering. Use when exact names are unknown or contain dynamic parts.")]
    public static string find_elements_fuzzy(
        [Description("App ID")] string appId,
        [Description("Control type filter")] string? controlType = null,
        [Description("Fuzzy match AutomationId")] string? idContains = null,
        [Description("Fuzzy match Name")] string? nameContains = null,
        [Description("Max results (default 50)")] int maxResults = 50,
        [Description("Optional HWND to target a specific window (0 = main window)")] long windowHandle = 0)
    {
        return _auto.FindElementsFuzzy(appId, controlType, idContains, nameContains, maxResults, windowHandle);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── ExpandCollapsePattern ────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Expand, collapse, or toggle a tree item, menu item, or ComboBox using ExpandCollapsePattern. Better than click for programmatic expand/collapse.")]
    public static string expand_collapse_element(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element")] string? automationId = null,
        [Description("Name of the element")] string? name = null,
        [Description("Control type filter (TreeItem, MenuItem, ComboBox, etc.)")] string? controlType = null,
        [Description("Action: 'expand', 'collapse', or 'toggle' (default)")] string action = "toggle",
        [Description("0-based index when multiple match (-1 = first)")] int index = -1,
        [Description("Optional HWND for specific window targeting (0 = main window)")] long windowHandle = 0)
    {
        return _auto.ExpandCollapseElement(appId, automationId, name, controlType, action, index, windowHandle);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── ScrollItemPattern — Scroll into view ─────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Scroll a UI element into the visible area. Essential for off-screen items in long lists, tree views, or scrollable panels.")]
    public static string scroll_into_view(
        [Description("App ID")] string appId,
        [Description("AutomationId of the element to scroll into view")] string? automationId = null,
        [Description("Name of the element")] string? name = null,
        [Description("Control type filter")] string? controlType = null,
        [Description("0-based index when multiple match (-1 = first)")] int index = -1,
        [Description("Optional HWND (0 = main window)")] long windowHandle = 0)
    {
        return _auto.ScrollIntoView(appId, automationId, name, controlType, index, windowHandle);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── VirtualizedItemPattern ───────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Realize a virtualized item, forcing it to be fully loaded in the UI tree. Critical for WinUI3 ListView/GridView which virtualizes off-screen items.")]
    public static string realize_virtualized_item(
        [Description("App ID")] string appId,
        [Description("AutomationId of the virtualized element")] string? automationId = null,
        [Description("Name of the element")] string? name = null,
        [Description("Control type filter (ListItem, TreeItem, DataItem, etc.)")] string? controlType = null,
        [Description("0-based index when multiple match (-1 = first)")] int index = -1,
        [Description("Optional HWND (0 = main window)")] long windowHandle = 0)
    {
        return _auto.RealizeVirtualizedItem(appId, automationId, name, controlType, index, windowHandle);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Screenshot with maxTokens ────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Take a screenshot with optional auto-resize to fit within a token budget. Set maxTokens to automatically shrink large screenshots so they don't exceed LLM context limits.")]
    public static string take_screenshot_optimized(
        [Description("App ID")] string appId,
        [Description("Full file path to save the screenshot")] string outputPath,
        [Description("Max tokens for the image (0 = no resize, 1000 ≈ 768x768). Auto-resizes if the image would exceed this budget.")] int maxTokens = 0,
        [Description("Optional HWND to capture a specific window (0 = main window)")] long windowHandle = 0)
    {
        return _auto.TakeScreenshotOptimized(appId, outputPath, maxTokens, windowHandle);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Event Monitoring ─────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Start monitoring UI automation events (focus changes, structure changes, property changes). Returns a session ID. Great for debugging async UI updates.")]
    public static string start_event_monitor(
        [Description("App ID")] string appId,
        [Description("Event type: 'focus' (focus changes), 'structurechanged' (elements added/removed), 'propertychanged' (property value changes)")] string eventType,
        [Description("AutomationId of element to scope monitoring to (optional — null = entire window)")] string? automationId = null,
        [Description("Name of element to scope monitoring to")] string? name = null,
        [Description("Control type filter for scope element")] string? controlType = null,
        [Description("Optional HWND (0 = main window)")] long windowHandle = 0)
    {
        return _auto.StartEventMonitor(appId, eventType, automationId, name, controlType, windowHandle);
    }

    [McpServerTool, Description("Stop an event monitoring session. Pass sessionId to stop a specific session, or omit to stop all.")]
    public static string stop_event_monitor(
        [Description("Session ID from start_event_monitor (omit to stop all sessions)")] string? sessionId = null)
    {
        return _auto.StopEventMonitor(sessionId);
    }

    [McpServerTool, Description("Get the event log from a monitoring session. Returns captured events since monitoring started.")]
    public static string get_event_log(
        [Description("Session ID from start_event_monitor (omit to get all sessions)")] string? sessionId = null,
        [Description("Maximum number of recent events to return (default 100)")] int maxCount = 100)
    {
        return _auto.GetEventLog(sessionId, maxCount);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── GridPattern — Direct grid cell access ────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Get a specific cell from a DataGrid or Table by row and column index. Much faster than navigating the tree manually.")]
    public static string get_grid_item(
        [Description("App ID")] string appId,
        [Description("0-based row index")] int row,
        [Description("0-based column index")] int column,
        [Description("AutomationId of the grid/table")] string? automationId = null,
        [Description("Name of the grid/table")] string? name = null,
        [Description("Control type (DataGrid, Table, List — default DataGrid)")] string? controlType = null,
        [Description("0-based index when multiple grids match (-1 = first)")] int index = -1,
        [Description("Optional HWND (0 = main window)")] long windowHandle = 0)
    {
        return _auto.GetGridItem(appId, row, column, automationId, name, controlType, index, windowHandle);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── ItemContainerPattern — Find item by property ─────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Find an item inside a container (List, Tree, DataGrid) by property value. Uses ItemContainerPattern for efficient search in large/virtualized lists.")]
    public static string find_item_by_property(
        [Description("App ID")] string appId,
        [Description("Property to search by: 'Name' or 'AutomationId'")] string? propertyName = null,
        [Description("Value to match (case-insensitive)")] string? value = null,
        [Description("AutomationId of the container")] string? automationId = null,
        [Description("Name of the container")] string? name = null,
        [Description("Control type of the container (List, Tree, DataGrid)")] string? controlType = null,
        [Description("0-based index when multiple containers match (-1 = first)")] int index = -1,
        [Description("Optional HWND (0 = main window)")] long windowHandle = 0)
    {
        return _auto.FindItemByProperty(appId, propertyName, value, automationId, name, controlType, index, windowHandle);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── WaitForWindowInputIdle ───────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Wait for a window to become idle (ready for input). Use after launching apps, navigating, or triggering async operations.")]
    public static string wait_for_input_idle(
        [Description("App ID")] string appId,
        [Description("Timeout in milliseconds (default 10000)")] int timeoutMs = 10000,
        [Description("Optional HWND (0 = main window)")] long windowHandle = 0)
    {
        return _auto.WaitForInputIdle(appId, timeoutMs, windowHandle);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Offscreen & Locked Session Support ───────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [McpServerTool, Description("Restore a minimized window and bring it to the foreground. Call this if the target app was minimized and you need it visible for coordinate-based operations. Most operations auto-restore automatically.")]
    public static string restore_window(
        [Description("App ID")] string appId)
    {
        return _auto.RestoreWindow(appId);
    }

    [McpServerTool, Description("Check if the desktop session is locked and whether the app window is minimized. Reports what operations are available. Call this if operations are failing unexpectedly — the session may be locked (Win+L) or the window minimized.")]
    public static string check_session_status(
        [Description("App ID")] string appId)
    {
        return _auto.CheckSessionStatus(appId);
    }
}
