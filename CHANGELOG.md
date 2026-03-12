# Changelog

All notable changes to WinApp MCP will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.7.0] — 2026-03-12

### Added — 2 New Tools (53 → 55 total)

**Offscreen & Locked Session Support**
- `restore_window` — Restore a minimized window and bring it to the foreground
- `check_session_status` — Report whether the desktop session is locked and what operations are available

### Improved — Minimized Window Handling
- All mouse/keyboard operations now **auto-restore minimized windows** before interaction
- `click_element` falls back to **UIA InvokePattern/SelectionItemPattern/TogglePattern** when the window is minimized or session is locked
- `set_element_value` and `type_text` use **ValuePattern** before falling back to keyboard simulation
- `scroll_element` uses **ScrollPattern** before falling back to mouse wheel
- `drag_element`, `double_click_element`, `right_click_element`, `click_at_coordinates` auto-restore and give clear errors when session is locked

### Improved — Locked Session (Win+L) Support
- Session lock detection via `OpenInputDesktop` Win32 API
- When locked: UIA pattern operations (invoke, read, find, set value) **continue to work**
- When locked: Mouse/keyboard operations return clear error messages with recommended alternatives

### Improved — Screenshots
- `take_screenshot`, `take_screenshot_optimized`, `annotate_screenshot` now use **Win32 PrintWindow API** as fallback
- Screenshots work for **minimized windows** and **locked sessions** (uses `PW_RENDERFULLCONTENT` flag)
- Capture strategy: FlaUI screen capture → PrintWindow API → restore + FlaUI retry

### Added — Win32 Infrastructure
- `ShowWindow`, `SetForegroundWindow`, `IsIconic`, `PrintWindow`, `GetWindowRect`, `OpenInputDesktop`, `GetWindowPlacement` P/Invoke declarations
- `CaptureViaPrintWindow()` — Captures window bitmap even when minimized/offscreen/locked
- `IsDesktopLocked()` — Detects locked Windows session
- `EnsureInteractiveForInput()` — Auto-restores minimized windows, reports locked state
- `TryClickViaPattern()` — Pattern-based click fallback for non-interactive windows

---

## [1.6.0] — 2026-03-12

### Added — 14 New Tools (39 → 53 total)

**HWND Multi-Window Targeting**
- `click_element_hwnd` — Click elements in a specific window by native handle
- `set_value_hwnd` — Set text values in a specific window by handle
- `get_snapshot_hwnd` — UI snapshot of a specific window by handle

**Fuzzy Search**
- `find_elements_fuzzy` — Search with Levenshtein distance, tolerates typos and word reordering

**UIA Pattern Tools**
- `expand_collapse_element` — Expand, collapse, or toggle tree/menu items via ExpandCollapsePattern
- `scroll_into_view` — Scroll off-screen elements into visible area via ScrollItemPattern
- `realize_virtualized_item` — Force-load virtualized items in WinUI3 ListView/GridView
- `get_grid_item` — Direct cell access in DataGrid/Table by row/column via GridPattern
- `find_item_by_property` — Search containers via ItemContainerPattern (efficient for large lists)
- `wait_for_input_idle` — Wait for window to be ready for input via WindowPattern

**Event Monitoring**
- `start_event_monitor` — Monitor focus, structure, or property change events with session management
- `stop_event_monitor` — Stop monitoring sessions (individual or all)
- `get_event_log` — Read captured events from monitoring sessions

**Screenshot**
- `take_screenshot_optimized` — Screenshot with auto-resize for LLM token budgets + HWND targeting

### Added — Architecture
- Fuzzy matching engine with Levenshtein distance algorithm and substring optimization
- HWND resolution infrastructure via FlaUI and Win32 P/Invoke
- Event monitoring session management with 500-event ring buffer per session

## [1.5.0] — 2026-03-10

### Added
- `select_option` — Select ComboBox/dropdown option in one atomic call

### Improved
- **Descendant cache** (2s TTL) — 8x faster repeated element lookups on complex apps
- **Window cache** (30s TTL) — 300x faster window resolution
- **Smart cache invalidation** — Cache auto-clears after mutations

## [1.4.0] — 2026-03-08

### Added — 10 New Tools (29 → 39 total)
- `read_element_by_index` — Read properties by index from find_all_elements
- `get_element_bounds` — Get bounding rectangle in screen coordinates
- `element_exists` — Fast boolean existence check
- `wait_for_condition` — Wait for a property to reach a value
- `get_clipboard` — Read clipboard text content
- `drag_element` — Drag from one element to another
- `get_all_values` — Read all editable field values at once
- `get_tree_hash` — Hash the UI tree for change detection
- `screenshot_diff` — Pixel-diff two screenshots with visual highlight
- `annotate_screenshot` — Draw red bounding boxes around elements in a screenshot

## [1.3.0] — 2026-03-05

### Added
- `fill_form` — Batch fill multiple form fields in one call
- `find_elements` — Filtered element search by type, id, name
- `invoke_element` — Invoke via UIA patterns (InvokePattern/TogglePattern)
- `set_element_value` — Set text value with index support
- `scroll_element` — Scroll within scrollable containers
- `invalidate_cache` — Manual cache invalidation
- `find_all_elements` — List all matching elements with indices

## [1.2.0] — 2026-03-02

### Added
- `click_at_coordinates` — Click at absolute screen coordinates
- `release_all` — Emergency: release all stuck modifier keys

## [1.1.0] — 2026-02-28

### Added
- `double_click_element`
- `right_click_element`
- `press_key_combo`
- `take_screenshot`

## [1.0.0] — 2026-02-25

### Initial Release
- Core tools: `launch_app`, `attach_to_app`, `attach_to_pid`, `close_app`, `list_apps`
- Window discovery: `list_windows`, `list_desktop_windows`, `get_snapshot`, `get_focused_element`
- Interaction: `click_element`, `type_text`, `press_key`
- Waiting: `wait_for_element`
- Element reading: `read_element`
- VS Code extension with auto-registration (VSIX)
