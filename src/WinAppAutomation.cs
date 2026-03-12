using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Application = FlaUI.Core.Application;

namespace WinAppMCP;

/// <summary>
/// Manages running application instances and provides UI automation via FlaUI.
/// </summary>
public sealed class WinAppAutomation : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly Dictionary<string, Application> _apps = new();
    private readonly Dictionary<string, Window> _windowCache = new();
    private readonly Dictionary<string, DateTime> _windowCacheTime = new();
    private AutomationElement? _focusedElement;
    private static readonly TimeSpan WindowCacheTTL = TimeSpan.FromSeconds(30);

    // ── Descendant cache (avoids repeated FindAllDescendants calls) ──
    private readonly Dictionary<string, AutomationElement[]> _descendantCache = new();
    private readonly Dictionary<string, DateTime> _descendantCacheTime = new();
    private static readonly TimeSpan DescendantCacheTTL = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Returns cached descendants if still valid, otherwise fetches fresh.
    /// This is the SINGLE biggest perf win — FindAllDescendants takes 200-800ms
    /// on complex WinUI3 apps, and most agent workflows call it 3-5 times in
    /// quick succession for the same UI state.
    /// </summary>
    private AutomationElement[] GetCachedDescendants(string appId, Window win)
    {
        if (_descendantCache.TryGetValue(appId, out AutomationElement[]? cached) &&
            _descendantCacheTime.TryGetValue(appId, out DateTime cacheTime) &&
            DateTime.UtcNow - cacheTime < DescendantCacheTTL)
        {
            return cached;
        }

        AutomationElement[] descendants = win.FindAllDescendants();
        _descendantCache[appId] = descendants;
        _descendantCacheTime[appId] = DateTime.UtcNow;
        return descendants;
    }

    /// Invalidate descendant cache (called after mutations like click, type, navigate)
    private void InvalidateDescendantCache(string appId)
    {
        _descendantCache.Remove(appId);
        _descendantCacheTime.Remove(appId);
    }

    public void Dispose()
    {
        foreach (Application app in _apps.Values)
        {
            try { app.Dispose(); } catch { }
        }
        _apps.Clear();
        _automation.Dispose();
    }

    // ── Launch / Attach / Close ──────────────────────────────────────

    public string LaunchApp(string exePath, string? arguments = null)
    {
        ProcessStartInfo psi = new(exePath) { UseShellExecute = false };
        if (!string.IsNullOrEmpty(arguments))
            psi.Arguments = arguments;

        Application app = Application.Launch(psi);
        string id = $"app_{app.ProcessId}";
        _apps[id] = app;
        // Wait a moment for the main window to appear
        Thread.Sleep(2000);
        return id;
    }

    public string AttachToProcess(int processId)
    {
        Application app = Application.Attach(processId);
        string id = $"app_{processId}";
        _apps[id] = app;
        return id;
    }

    public string AttachToProcess(string processName)
    {
        Process? proc = Process.GetProcessesByName(processName).FirstOrDefault();
        if (proc is null)
            return $"ERROR: No process found with name '{processName}'";

        return AttachToProcess(proc.Id);
    }

    public string CloseApp(string appId)
    {
        if (!_apps.TryGetValue(appId, out Application? app))
            return $"ERROR: App '{appId}' not found";

        app.Close();
        _apps.Remove(appId);
        _windowCache.Remove(appId);
        _windowCacheTime.Remove(appId);
        return "OK";
    }

    public string ListApps()
    {
        if (_apps.Count == 0)
            return "No apps currently tracked.";

        StringBuilder sb = new();
        foreach (KeyValuePair<string, Application> kvp in _apps)
        {
            try
            {
                string name = kvp.Value.Name ?? "Unknown";
                sb.AppendLine($"  {kvp.Key}: {name} (PID {kvp.Value.ProcessId})");
            }
            catch
            {
                sb.AppendLine($"  {kvp.Key}: (process may have exited)");
            }
        }
        return sb.ToString();
    }

    // ── Window helpers ───────────────────────────────────────────────

    private Window? GetMainWindow(string appId)
    {
        if (!_apps.TryGetValue(appId, out Application? app))
            return null;

        // Return cached window if still valid
        if (_windowCache.TryGetValue(appId, out Window? cached) &&
            _windowCacheTime.TryGetValue(appId, out DateTime cacheTime) &&
            DateTime.Now - cacheTime < WindowCacheTTL)
        {
            try
            {
                // Quick check: is the window still alive?
                _ = cached.Title;
                return cached;
            }
            catch
            {
                _windowCache.Remove(appId);
                _windowCacheTime.Remove(appId);
            }
        }

        try
        {
            Window? win = app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));
            if (win is not null)
            {
                _windowCache[appId] = win;
                _windowCacheTime[appId] = DateTime.Now;
            }
            return win;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GetMainWindow error: {ex.Message}");
            try
            {
                Window[] windows = app.GetAllTopLevelWindows(_automation);
                if (windows.Length > 0)
                {
                    _windowCache[appId] = windows[0];
                    _windowCacheTime[appId] = DateTime.Now;
                    return windows[0];
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// Invalidate cached window (call after navigation that opens new windows)
    public string InvalidateWindowCache(string appId)
    {
        _windowCache.Remove(appId);
        _windowCacheTime.Remove(appId);
        return "Window cache invalidated.";
    }

    public string ListWindows(string appId)
    {
        if (!_apps.TryGetValue(appId, out Application? app))
            return $"ERROR: App '{appId}' not found";

        try
        {
            Window[] windows = app.GetAllTopLevelWindows(_automation);
            if (windows.Length == 0)
                return "No windows found.";

            StringBuilder sb = new();
            for (int i = 0; i < windows.Length; i++)
            {
                sb.AppendLine($"  [{i}] \"{windows[i].Title}\" ({windows[i].AutomationId})");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Snapshot / Tree ──────────────────────────────────────────────

    public string GetSnapshot(string appId, int maxDepth = 3)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        try
        {
            StringBuilder sb = new();
            sb.AppendLine($"Window: \"{win.Title}\"");

            // Try flat descendant search first (more reliable for WinUI3)
            AutomationElement[] descendants;
            try
            {
                descendants = GetCachedDescendants(appId, win);
            }
            catch
            {
                // Fallback to tree walk
                _elementCount = 0;
                BuildTree(win, sb, depth: 0, maxDepth: maxDepth, elementLimit: 500);
                return sb.ToString();
            }

            int count = 0;
            foreach (AutomationElement el in descendants)
            {
                if (count >= 500) { sb.AppendLine("... (truncated at 500 elements)"); break; }
                count++;

                string controlType = "", automationId = "", name = "", className = "";
                try { controlType = el.Properties.ControlType.ValueOrDefault.ToString(); } catch { }
                try { automationId = el.Properties.AutomationId.ValueOrDefault ?? ""; } catch { }
                try { name = el.Properties.Name.ValueOrDefault ?? ""; } catch { }
                try { className = el.Properties.ClassName.ValueOrDefault ?? ""; } catch { }

                // Skip elements with no useful info (anonymous layout containers)
                if (string.IsNullOrEmpty(automationId) && string.IsNullOrEmpty(name))
                    continue;

                sb.Append($"  [{controlType}]");
                if (!string.IsNullOrEmpty(automationId))
                    sb.Append($" id=\"{automationId}\"");
                if (!string.IsNullOrEmpty(name))
                    sb.Append($" name=\"{name}\"");
                if (!string.IsNullOrEmpty(className))
                    sb.Append($" class=\"{className}\"");
                sb.AppendLine();
            }

            if (count == 0)
                sb.AppendLine("  (no accessible elements found — app may use a non-standard UI framework)");

            if (sb.Length > 60000)
                sb.Length = 60000;
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: Snapshot failed — {ex.Message}";
        }
    }

    private static int _elementCount;

    private static void BuildTree(AutomationElement element, StringBuilder sb, int depth, int maxDepth, int elementLimit = 500)
    {
        if (depth > maxDepth || _elementCount >= elementLimit)
            return;

        AutomationElement[] children;
        try
        {
            children = element.FindAllChildren();
        }
        catch
        {
            return;
        }

        foreach (AutomationElement child in children)
        {
            if (_elementCount >= elementLimit)
            {
                sb.AppendLine($"{new string(' ', (depth + 1) * 2)}... (truncated, {elementLimit} elements reached)");
                return;
            }
            _elementCount++;

            string indent = new(' ', (depth + 1) * 2);
            string controlType;
            string automationId;
            string name;
            string className;
            try
            {
                controlType = child.ControlType.ToString();
                automationId = child.AutomationId ?? "";
                name = child.Name ?? "";
                className = child.ClassName ?? "";
            }
            catch
            {
                sb.AppendLine($"{indent}[Unknown] (access error)");
                continue;
            }

            sb.Append($"{indent}[{controlType}]");
            if (!string.IsNullOrEmpty(automationId))
                sb.Append($" AutomationId=\"{automationId}\"");
            if (!string.IsNullOrEmpty(name))
                sb.Append($" Name=\"{name}\"");
            if (!string.IsNullOrEmpty(className))
                sb.Append($" Class=\"{className}\"");

            // Show value for editable controls
            try
            {
                if (child.ControlType == ControlType.Edit || child.ControlType == ControlType.Document)
                {
                    FlaUI.Core.AutomationElements.TextBox? textBox = child.AsTextBox();
                    if (textBox is not null)
                        sb.Append($" Value=\"{textBox.Text}\"");
                }
                else if (child.ControlType == ControlType.CheckBox)
                {
                    CheckBox? cb = child.AsCheckBox();
                    if (cb is not null)
                        sb.Append($" Checked={cb.IsChecked}");
                }
                else if (child.ControlType == ControlType.ComboBox)
                {
                    ComboBox? combo = child.AsComboBox();
                    if (combo is not null)
                        sb.Append($" Selected=\"{combo.SelectedItem?.Text}\"");
                }
            }
            catch { }

            sb.AppendLine();
            BuildTree(child, sb, depth + 1, maxDepth, elementLimit);
        }
    }

    // ── Find Element ─────────────────────────────────────────────────

    private AutomationElement? FindElement(string appId, string? automationId, string? name, string? controlType)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return null;

        try
        {
            ConditionFactory cf = _automation.ConditionFactory;
            List<ConditionBase> conditions = new();

            if (!string.IsNullOrEmpty(automationId))
                conditions.Add(cf.ByAutomationId(automationId));
            if (!string.IsNullOrEmpty(name))
                conditions.Add(cf.ByName(name));
            if (!string.IsNullOrEmpty(controlType) && Enum.TryParse<ControlType>(controlType, true, out ControlType ct))
                conditions.Add(cf.ByControlType(ct));

            if (conditions.Count == 0)
                return null;

            ConditionBase condition = conditions.Count == 1
                ? conditions[0]
                : new AndCondition(conditions.ToArray());

            AutomationElement? found = win.FindFirstDescendant(condition);
            if (found is not null)
            {
                _focusedElement = found;
                return found;
            }

            // Fallback: brute-force search through cached descendants (helps with WinUI3)
            AutomationElement[] allElements = GetCachedDescendants(appId, win);
            foreach (AutomationElement el in allElements)
            {
                try
                {
                    bool match = true;
                    if (!string.IsNullOrEmpty(automationId))
                        match &= (el.Properties.AutomationId.ValueOrDefault ?? "") == automationId;
                    if (!string.IsNullOrEmpty(name))
                    {
                        string elName = el.Properties.Name.ValueOrDefault ?? "";
                        match &= elName.Equals(name, StringComparison.OrdinalIgnoreCase) || elName.Contains(name, StringComparison.OrdinalIgnoreCase);
                    }
                    if (match)
                    {
                        _focusedElement = el;
                        return el;
                    }
                }
                catch { }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // ── Find All Elements (with index support) ─────────────────────

    private List<AutomationElement> FindAllMatchingElements(string appId, string? automationId, string? name, string? controlType)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return new();

        try
        {
            List<AutomationElement> results = new();
            AutomationElement[] allElements = GetCachedDescendants(appId, win);
            foreach (AutomationElement el in allElements)
            {
                try
                {
                    bool match = true;
                    if (!string.IsNullOrEmpty(automationId))
                        match &= (el.Properties.AutomationId.ValueOrDefault ?? "") == automationId;
                    if (!string.IsNullOrEmpty(name))
                    {
                        string elName = el.Properties.Name.ValueOrDefault ?? "";
                        match &= elName.Equals(name, StringComparison.OrdinalIgnoreCase) || elName.Contains(name, StringComparison.OrdinalIgnoreCase);
                    }
                    if (!string.IsNullOrEmpty(controlType) && Enum.TryParse<ControlType>(controlType, true, out ControlType ct))
                        match &= el.Properties.ControlType.ValueOrDefault == ct;
                    if (match)
                        results.Add(el);
                }
                catch { }
            }
            return results;
        }
        catch
        {
            return new();
        }
    }

    private AutomationElement? FindElementByIndex(string appId, string? automationId, string? name, string? controlType, int index)
    {
        List<AutomationElement> matches = FindAllMatchingElements(appId, automationId, name, controlType);
        if (index < 0 || index >= matches.Count)
            return null;
        _focusedElement = matches[index];
        return matches[index];
    }

    public string FindAllElements(string appId, string? automationId = null, string? name = null, string? controlType = null)
    {
        List<AutomationElement> matches = FindAllMatchingElements(appId, automationId, name, controlType);
        if (matches.Count == 0)
            return "No matching elements found.";

        StringBuilder sb = new();
        sb.AppendLine($"Found {matches.Count} matching element(s):");
        for (int i = 0; i < matches.Count; i++)
        {
            AutomationElement el = matches[i];
            string elName = "", elAutoId = "", elClass = "", elType = "";
            bool isOffscreen = false;
            try { elType = el.Properties.ControlType.ValueOrDefault.ToString(); } catch { }
            try { elAutoId = el.Properties.AutomationId.ValueOrDefault ?? ""; } catch { }
            try { elName = el.Properties.Name.ValueOrDefault ?? ""; } catch { }
            try { elClass = el.Properties.ClassName.ValueOrDefault ?? ""; } catch { }
            try { isOffscreen = el.Properties.IsOffscreen.ValueOrDefault; } catch { }
            sb.Append($"  [{i}] [{elType}]");
            if (!string.IsNullOrEmpty(elAutoId)) sb.Append($" id=\"{elAutoId}\"");
            if (!string.IsNullOrEmpty(elName)) sb.Append($" name=\"{elName}\"");
            if (!string.IsNullOrEmpty(elClass)) sb.Append($" class=\"{elClass}\"");
            sb.Append($" offscreen={isOffscreen}");
            try { sb.Append($" bounds={el.BoundingRectangle}"); } catch { }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ── Click ────────────────────────────────────────────────────────

    public string ClickElement(string appId, string? automationId = null, string? name = null, string? controlType = null, int index = -1)
    {
        AutomationElement? el = index >= 0
            ? FindElementByIndex(appId, automationId, name, controlType, index)
            : FindElement(appId, automationId, name, controlType);
        if (el is null)
            return $"ERROR: Element not found (automationId={automationId}, name={name}, controlType={controlType}, index={index})";

        // When window is minimized or session is locked, try UIA patterns first
        Window? win = GetMainWindow(appId);
        IntPtr hwnd = win is not null ? GetNativeHandle(win) : IntPtr.Zero;
        bool isMinimized = hwnd != IntPtr.Zero && IsIconic(hwnd);
        bool isLocked = IsDesktopLocked();

        if (isMinimized || isLocked)
        {
            string? patternResult = TryClickViaPattern(el, appId);
            if (patternResult is not null)
                return patternResult;

            if (isLocked)
                return "ERROR: Session is locked and element has no InvokePattern. Use invoke_element or unlock the session.";

            RestoreAndForeground(hwnd);
        }

        el.Click();
        InvalidateDescendantCache(appId);
        try { return $"Clicked [{el.ControlType}] \"{el.Name}\""; } catch { return "Clicked element"; }
    }

    public string DoubleClickElement(string appId, string? automationId = null, string? name = null, string? controlType = null, int index = -1)
    {
        AutomationElement? el = index >= 0
            ? FindElementByIndex(appId, automationId, name, controlType, index)
            : FindElement(appId, automationId, name, controlType);
        if (el is null)
            return $"ERROR: Element not found";

        if (!EnsureInteractiveForInput(appId))
            return "ERROR: Session is locked. Double-click requires mouse input — unlock the session.";

        el.DoubleClick();
        InvalidateDescendantCache(appId);
        try { return $"Double-clicked [{el.ControlType}] \"{el.Name}\""; } catch { return "Double-clicked element"; }
    }

    public string RightClickElement(string appId, string? automationId = null, string? name = null, string? controlType = null, int index = -1)
    {
        AutomationElement? el = index >= 0
            ? FindElementByIndex(appId, automationId, name, controlType, index)
            : FindElement(appId, automationId, name, controlType);
        if (el is null)
            return $"ERROR: Element not found";

        if (!EnsureInteractiveForInput(appId))
            return "ERROR: Session is locked. Right-click requires mouse input — unlock the session.";

        el.RightClick();
        InvalidateDescendantCache(appId);
        try { return $"Right-clicked [{el.ControlType}] \"{el.Name}\""; } catch { return "Right-clicked element"; }
    }

    // ── Invoke (for buttons/dialogs that don't respond to Click) ────

    public string InvokeElement(string appId, string? automationId = null, string? name = null, string? controlType = null, int index = -1)
    {
        AutomationElement? el = index >= 0
            ? FindElementByIndex(appId, automationId, name, controlType, index)
            : FindElement(appId, automationId, name, controlType);
        if (el is null)
            return $"ERROR: Element not found (automationId={automationId}, name={name}, controlType={controlType}, index={index})";

        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }

        InvalidateDescendantCache(appId);

        // Try InvokePattern
        try
        {
            if (el.Patterns.Invoke.TryGetPattern(out FlaUI.Core.Patterns.IInvokePattern? invokePattern))
            {
                invokePattern.Invoke();
                return $"Invoked {elDesc}";
            }
        }
        catch { }

        // Try SelectionItemPattern (for NavigationViewItems, ListItems, TabItems)
        try
        {
            if (el.Patterns.SelectionItem.TryGetPattern(out FlaUI.Core.Patterns.ISelectionItemPattern? selPattern))
            {
                selPattern.Select();
                return $"Selected {elDesc}";
            }
        }
        catch { }

        // Try TogglePattern (for toggle buttons/checkboxes)
        try
        {
            if (el.Patterns.Toggle.TryGetPattern(out FlaUI.Core.Patterns.ITogglePattern? togglePattern))
            {
                togglePattern.Toggle();
                return $"Toggled {elDesc}";
            }
        }
        catch { }

        // Fallback to Click
        el.Click();
        return $"Click-invoked {elDesc} (no Invoke/Toggle pattern available)";
    }

    // ── Set Value on Element by Index ────────────────────────────────

    public string SetElementValue(string appId, string text, string? automationId = null, string? name = null, string? controlType = null, int index = -1)
    {
        AutomationElement? el = index >= 0
            ? FindElementByIndex(appId, automationId, name, controlType, index)
            : FindElement(appId, automationId, name, controlType);
        if (el is null)
            return $"ERROR: Element not found";

        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }

        InvalidateDescendantCache(appId);

        // Try ValuePattern
        try
        {
            if (el.Patterns.Value.TryGetPattern(out FlaUI.Core.Patterns.IValuePattern? valuePattern))
            {
                valuePattern.SetValue(text);
                return $"Set value \"{text}\" on {elDesc} via ValuePattern";
            }
        }
        catch { }

        // Try as TextBox
        FlaUI.Core.AutomationElements.TextBox? textBox = el.AsTextBox();
        if (textBox is not null)
        {
            textBox.Text = text;
            return $"Set text \"{text}\" on {elDesc}";
        }

        // Fallback: focus and type (requires interactive window)
        if (!EnsureInteractiveForInput(appId))
            return $"ERROR: Cannot type via keyboard — session is locked. Element doesn't support ValuePattern.";

        el.Focus();
        Thread.Sleep(100);
        Keyboard.TypeSimultaneously(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL, FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
        Thread.Sleep(50);
        Keyboard.Type(text);
        return $"Typed \"{text}\" into {elDesc} (focus+type fallback)";
    }

    // ── Click at coordinates ─────────────────────────────────────────

    public string ClickAtPoint(string appId, int x, int y)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        if (!EnsureInteractiveForInput(appId))
            return "ERROR: Session is locked. Coordinate-based click requires mouse input — unlock the session.";

        InvalidateDescendantCache(appId);

        try
        {
            Mouse.Click(new Point(x, y));
            return $"Clicked at ({x}, {y})";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Scroll ───────────────────────────────────────────────────────

    public string ScrollElement(string appId, string? automationId = null, string? name = null, string? controlType = null, int clicks = 3, string direction = "down", int index = -1)
    {
        AutomationElement? el = index >= 0
            ? FindElementByIndex(appId, automationId, name, controlType, index)
            : FindElement(appId, automationId, name, controlType);
        if (el is null)
            return $"ERROR: Element not found";

        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }

        try
        {
            // Try ScrollPattern first
            if (el.Patterns.Scroll.TryGetPattern(out FlaUI.Core.Patterns.IScrollPattern? scrollPattern))
            {
                try
                {
                    for (int i = 0; i < Math.Abs(clicks); i++)
                    {
                        switch (direction.ToLowerInvariant())
                        {
                            case "down":
                                scrollPattern.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallIncrement);
                                break;
                            case "up":
                                scrollPattern.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallDecrement);
                                break;
                            case "left":
                                scrollPattern.Scroll(ScrollAmount.SmallDecrement, ScrollAmount.NoAmount);
                                break;
                            case "right":
                                scrollPattern.Scroll(ScrollAmount.SmallIncrement, ScrollAmount.NoAmount);
                                break;
                        }
                        Thread.Sleep(50);
                    }
                    return $"Scrolled {direction} {clicks} times on {elDesc}";
                }
                catch
                {
                    // ScrollPattern failed, fall through to mouse wheel
                }
            }
        }
        catch { /* TryGetPattern failed, fall through */ }

        // Fallback: mouse wheel (requires interactive window)
        if (!EnsureInteractiveForInput(appId))
            return $"ERROR: Cannot scroll via mouse — session is locked and element doesn't support ScrollPattern.";

        try
        {
            // Fallback: move mouse to center of element and use mouse wheel
            Rectangle rect = el.BoundingRectangle;
            if (rect.Width > 0 && rect.Height > 0)
            {
                Point center = new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                Mouse.MoveTo(center);
                Thread.Sleep(100);
                int wheelAmount = direction.ToLowerInvariant() switch
                {
                    "up" => clicks * 120,
                    "down" => -(clicks * 120),
                    _ => 0
                };
                if (wheelAmount != 0)
                    Mouse.Scroll(wheelAmount);
                return $"Mouse-wheel scrolled {direction} {clicks} ticks on {elDesc}";
            }

            return $"ERROR: Element has no visible bounds for scroll";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Type / Fill ──────────────────────────────────────────────────

    public string TypeText(string appId, string text, string? automationId = null, string? name = null)
    {
        AutomationElement? el = FindElement(appId, automationId, name, "Edit");
        if (el is null)
            el = FindElement(appId, automationId, name, null);
        if (el is null)
            return $"ERROR: Element not found";

        InvalidateDescendantCache(appId);

        FlaUI.Core.AutomationElements.TextBox? textBox = el.AsTextBox();
        if (textBox is not null)
        {
            textBox.Text = text;
            return $"Set text to \"{text}\" in [{el.ControlType}] \"{el.Name}\"";
        }

        // Fallback: focus and use keyboard (requires interactive window)
        if (!EnsureInteractiveForInput(appId))
            return $"ERROR: Cannot type via keyboard — session is locked and element doesn't support ValuePattern.";

        el.Focus();
        Keyboard.Type(text);
        return $"Typed \"{text}\" into [{el.ControlType}] \"{el.Name}\"";
    }

    // ── Read ─────────────────────────────────────────────────────────

    public string ReadElement(string appId, string? automationId = null, string? name = null, string? controlType = null)
    {
        AutomationElement? el = FindElement(appId, automationId, name, controlType);
        if (el is null)
            return $"ERROR: Element not found";

        StringBuilder sb = new();
        sb.AppendLine($"ControlType: {el.ControlType}");
        sb.AppendLine($"AutomationId: {el.AutomationId}");
        sb.AppendLine($"Name: {el.Name}");
        sb.AppendLine($"ClassName: {el.ClassName}");
        sb.AppendLine($"IsEnabled: {el.IsEnabled}");
        sb.AppendLine($"IsOffscreen: {el.IsOffscreen}");
        sb.AppendLine($"BoundingRectangle: {el.BoundingRectangle}");

        try
        {
            if (el.ControlType == ControlType.Edit || el.ControlType == ControlType.Document)
            {
                FlaUI.Core.AutomationElements.TextBox? textBox = el.AsTextBox();
                if (textBox is not null)
                    sb.AppendLine($"Text: {textBox.Text}");
            }
            else if (el.ControlType == ControlType.CheckBox)
            {
                CheckBox? cb = el.AsCheckBox();
                if (cb is not null)
                    sb.AppendLine($"IsChecked: {cb.IsChecked}");
            }
            else if (el.ControlType == ControlType.ComboBox)
            {
                ComboBox? combo = el.AsComboBox();
                if (combo is not null)
                    sb.AppendLine($"SelectedItem: {combo.SelectedItem?.Text}");
            }
            else if (el.ControlType == ControlType.DataGrid || el.ControlType == ControlType.Table)
            {
                Grid? grid = el.AsGrid();
                if (grid is not null)
                {
                    sb.AppendLine($"RowCount: {grid.RowCount}");
                    sb.AppendLine($"ColumnCount: {grid.ColumnCount}");
                }
            }
        }
        catch { }

        return sb.ToString();
    }

    // ── Keyboard ─────────────────────────────────────────────────────

    public string ReleaseAll()
    {
        // Release all modifier keys
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.SHIFT); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.ALT); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.LWIN); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.RWIN); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.LCONTROL); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.RCONTROL); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.LSHIFT); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.RSHIFT); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.LMENU); } catch { }
        try { Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.RMENU); } catch { }

        // Release mouse buttons
        try { Mouse.Up(FlaUI.Core.Input.MouseButton.Left); } catch { }
        try { Mouse.Up(FlaUI.Core.Input.MouseButton.Right); } catch { }
        try { Mouse.Up(FlaUI.Core.Input.MouseButton.Middle); } catch { }

        return "Released all modifier keys and mouse buttons.";
    }

    public string PressKey(string key)
    {
        // Support common named keys
        if (Enum.TryParse<FlaUI.Core.WindowsAPI.VirtualKeyShort>(key, true, out FlaUI.Core.WindowsAPI.VirtualKeyShort vk))
        {
            Keyboard.Press(vk);
            return $"Pressed key: {key}";
        }

        // Try typing as text
        Keyboard.Type(key);
        return $"Typed: {key}";
    }

    public string PressKeyCombo(string[] keys)
    {
        List<FlaUI.Core.WindowsAPI.VirtualKeyShort> vkeys = new();
        foreach (string k in keys)
        {
            if (Enum.TryParse<FlaUI.Core.WindowsAPI.VirtualKeyShort>(k, true, out FlaUI.Core.WindowsAPI.VirtualKeyShort vk))
                vkeys.Add(vk);
        }

        if (vkeys.Count != keys.Length)
            return $"ERROR: Could not parse all keys. Valid keys: {string.Join(", ", Enum.GetNames<FlaUI.Core.WindowsAPI.VirtualKeyShort>())}";

        // Press all modifier keys, press last key, release in reverse
        for (int i = 0; i < vkeys.Count - 1; i++)
            Keyboard.Press(vkeys[i]);

        Keyboard.Press(vkeys[^1]);
        Keyboard.Release(vkeys[^1]);

        for (int i = vkeys.Count - 2; i >= 0; i--)
            Keyboard.Release(vkeys[i]);

        return $"Pressed combo: {string.Join("+", keys)}";
    }

    // ── Screenshot ───────────────────────────────────────────────────

    public string TakeScreenshot(string appId, string outputPath)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        try
        {
            Bitmap? bmp = CaptureWindowBitmap(win);
            if (bmp is null)
                return "ERROR: Failed to capture window. Window may not be renderable.";

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            bmp.Save(outputPath, ImageFormat.Png);
            string result = $"Screenshot saved to {outputPath} ({bmp.Width}x{bmp.Height})";
            bmp.Dispose();
            return result;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Wait ─────────────────────────────────────────────────────────

    public string WaitForElement(string appId, string? automationId, string? name, string? controlType, int timeoutMs = 10000)
    {
        DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            AutomationElement? el = FindElement(appId, automationId, name, controlType);
            if (el is not null)
                return $"Found [{el.ControlType}] \"{el.Name}\" (AutomationId={el.AutomationId})";
            Thread.Sleep(300);
        }
        return $"ERROR: Timed out after {timeoutMs}ms waiting for element";
    }

    // ── Fill Form (batch set multiple fields) ────────────────────────

    public string FillForm(string appId, Dictionary<string, string> fields)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        StringBuilder sb = new();
        int success = 0;
        int failed = 0;

        foreach (KeyValuePair<string, string> field in fields)
        {
            try
            {
                // Try by AutomationId first (fastest path)
                AutomationElement? el = null;
                try
                {
                    el = win.FindFirstDescendant(_automation.ConditionFactory.ByAutomationId(field.Key));
                }
                catch { }

                // Fallback to name search
                if (el is null)
                {
                    try
                    {
                        el = win.FindFirstDescendant(_automation.ConditionFactory.ByName(field.Key));
                    }
                    catch { }
                }

                // Fallback brute force (using cached descendants)
                if (el is null)
                {
                    AutomationElement[] allElements = GetCachedDescendants(appId, win);
                    foreach (AutomationElement candidate in allElements)
                    {
                        try
                        {
                            string aid = candidate.Properties.AutomationId.ValueOrDefault ?? "";
                            string nm = candidate.Properties.Name.ValueOrDefault ?? "";
                            if (aid == field.Key || nm.Equals(field.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                el = candidate;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (el is null)
                {
                    sb.AppendLine($"  FAIL: \"{field.Key}\" — element not found");
                    failed++;
                    continue;
                }

                // Set value using ValuePattern → TextBox → Focus+Type
                bool set = false;
                try
                {
                    if (el.Patterns.Value.TryGetPattern(out FlaUI.Core.Patterns.IValuePattern? vp))
                    {
                        vp.SetValue(field.Value);
                        set = true;
                    }
                }
                catch { }

                if (!set)
                {
                    FlaUI.Core.AutomationElements.TextBox? tb = el.AsTextBox();
                    if (tb is not null)
                    {
                        tb.Text = field.Value;
                        set = true;
                    }
                }

                if (!set)
                {
                    el.Focus();
                    Thread.Sleep(50);
                    Keyboard.TypeSimultaneously(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL, FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
                    Thread.Sleep(30);
                    Keyboard.Type(field.Value);
                    set = true;
                }

                sb.AppendLine($"  OK: \"{field.Key}\" = \"{field.Value}\"");
                success++;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  FAIL: \"{field.Key}\" — {ex.Message}");
                failed++;
            }
        }

        sb.Insert(0, $"Fill form: {success} succeeded, {failed} failed\n");
        return sb.ToString();
    }

    // ── Find Elements with Filters ───────────────────────────────────

    public string FindElementsFiltered(string appId, string? controlType = null, string? idContains = null, string? nameContains = null, int maxResults = 50)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        try
        {
            AutomationElement[] allElements = GetCachedDescendants(appId, win);
            StringBuilder sb = new();
            int count = 0;

            foreach (AutomationElement el in allElements)
            {
                if (count >= maxResults) break;

                string elType = "", elId = "", elName = "", elClass = "";
                try { elType = el.Properties.ControlType.ValueOrDefault.ToString(); } catch { }
                try { elId = el.Properties.AutomationId.ValueOrDefault ?? ""; } catch { }
                try { elName = el.Properties.Name.ValueOrDefault ?? ""; } catch { }
                try { elClass = el.Properties.ClassName.ValueOrDefault ?? ""; } catch { }

                // Apply filters
                if (!string.IsNullOrEmpty(controlType) && !elType.Contains(controlType, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(idContains) && !elId.Contains(idContains, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(nameContains) && !elName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip anonymous noise
                if (string.IsNullOrEmpty(elId) && string.IsNullOrEmpty(elName))
                    continue;

                count++;
                sb.Append($"  [{elType}]");
                if (!string.IsNullOrEmpty(elId)) sb.Append($" id=\"{elId}\"");
                if (!string.IsNullOrEmpty(elName)) sb.Append($" name=\"{elName}\"");
                if (!string.IsNullOrEmpty(elClass)) sb.Append($" class=\"{elClass}\"");

                bool isEnabled = true;
                try { isEnabled = el.Properties.IsEnabled.ValueOrDefault; } catch { }
                if (!isEnabled) sb.Append(" [DISABLED]");

                sb.AppendLine();
            }

            if (count == 0)
                return "No elements matched the filters.";

            sb.Insert(0, $"Found {count} element(s):\n");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Inspect focused / hovered ────────────────────────────────────

    public string GetFocusedElement()
    {
        try
        {
            AutomationElement? focused = _automation.FocusedElement();
            if (focused is null)
                return "No element is currently focused.";

            string ct = "", aid = "", nm = "", cls = "";
            try { ct = focused.ControlType.ToString(); } catch { ct = "?"; }
            try { aid = focused.AutomationId ?? ""; } catch { }
            try { nm = focused.Name ?? ""; } catch { }
            try { cls = focused.ClassName ?? ""; } catch { }
            _focusedElement = focused;
            return $"[{ct}] AutomationId=\"{aid}\" Name=\"{nm}\" Class=\"{cls}\"";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Read Element by Index ────────────────────────────────────────

    public string ReadElementByIndex(string appId, string? automationId, string? name, string? controlType, int index)
    {
        List<AutomationElement> matches = FindAllMatchingElements(appId, automationId, name, controlType);
        if (matches.Count == 0)
            return $"ERROR: No matching elements found (automationId={automationId}, name={name}, controlType={controlType})";
        if (index < 0 || index >= matches.Count)
            return $"ERROR: Index {index} out of range. Found {matches.Count} element(s) (valid: 0-{matches.Count - 1})";

        AutomationElement el = matches[index];
        StringBuilder sb = new();
        try { sb.AppendLine($"ControlType: {el.ControlType}"); } catch { }
        try { sb.AppendLine($"AutomationId: {el.AutomationId}"); } catch { }
        try { sb.AppendLine($"Name: {el.Name}"); } catch { }
        try { sb.AppendLine($"ClassName: {el.ClassName}"); } catch { }
        try { sb.AppendLine($"IsEnabled: {el.IsEnabled}"); } catch { }
        try { sb.AppendLine($"IsOffscreen: {el.IsOffscreen}"); } catch { }
        try { sb.AppendLine($"BoundingRectangle: {el.BoundingRectangle}"); } catch { }

        try
        {
            if (el.ControlType == ControlType.Edit || el.ControlType == ControlType.Document)
            {
                FlaUI.Core.AutomationElements.TextBox? textBox = el.AsTextBox();
                if (textBox is not null)
                    sb.AppendLine($"Text: {textBox.Text}");
            }
            else if (el.ControlType == ControlType.CheckBox)
            {
                CheckBox? cb = el.AsCheckBox();
                if (cb is not null)
                    sb.AppendLine($"IsChecked: {cb.IsChecked}");
            }
            else if (el.ControlType == ControlType.ComboBox)
            {
                ComboBox? combo = el.AsComboBox();
                if (combo is not null)
                    sb.AppendLine($"SelectedItem: {combo.SelectedItem?.Text}");
            }
            else if (el.ControlType == ControlType.DataGrid || el.ControlType == ControlType.Table)
            {
                Grid? grid = el.AsGrid();
                if (grid is not null)
                {
                    sb.AppendLine($"RowCount: {grid.RowCount}");
                    sb.AppendLine($"ColumnCount: {grid.ColumnCount}");
                }
            }
        }
        catch { }

        return sb.ToString();
    }

    // ── Get Element Bounds ───────────────────────────────────────────

    public string GetElementBounds(string appId, string? automationId, string? name, string? controlType, int index = -1)
    {
        AutomationElement? el = index >= 0
            ? FindElementByIndex(appId, automationId, name, controlType, index)
            : FindElement(appId, automationId, name, controlType);
        if (el is null)
            return $"ERROR: Element not found";

        try
        {
            Rectangle rect = el.BoundingRectangle;
            return $"X={rect.X}, Y={rect.Y}, Width={rect.Width}, Height={rect.Height}, Right={rect.Right}, Bottom={rect.Bottom}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Element Exists ───────────────────────────────────────────────

    public string ElementExists(string appId, string? automationId, string? name, string? controlType)
    {
        AutomationElement? el = FindElement(appId, automationId, name, controlType);
        if (el is null)
            return "false";

        try
        {
            return $"true | [{el.ControlType}] id=\"{el.AutomationId}\" name=\"{el.Name}\"";
        }
        catch
        {
            return "true";
        }
    }

    // ── Wait for Condition ───────────────────────────────────────────

    public string WaitForCondition(string appId, string? automationId, string? name, string? controlType, string property, string expectedValue, int timeoutMs = 10000)
    {
        DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            AutomationElement? el = FindElement(appId, automationId, name, controlType);
            if (el is not null)
            {
                try
                {
                    string? actual = property.ToLowerInvariant() switch
                    {
                        "name" => el.Properties.Name.ValueOrDefault,
                        "isenabled" => el.Properties.IsEnabled.ValueOrDefault.ToString(),
                        "isoffscreen" => el.Properties.IsOffscreen.ValueOrDefault.ToString(),
                        "text" or "value" => GetElementTextValue(el),
                        "ischecked" => el.AsCheckBox()?.IsChecked?.ToString(),
                        "selecteditem" => el.AsComboBox()?.SelectedItem?.Text,
                        "automationid" => el.Properties.AutomationId.ValueOrDefault,
                        _ => null
                    };

                    if (actual is not null && actual.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
                        return $"Condition met: {property} = \"{actual}\"";
                }
                catch { }
            }
            Thread.Sleep(300);
        }
        return $"ERROR: Timed out after {timeoutMs}ms waiting for {property} = \"{expectedValue}\"";
    }

    private static string? GetElementTextValue(AutomationElement el)
    {
        try
        {
            if (el.Patterns.Value.TryGetPattern(out FlaUI.Core.Patterns.IValuePattern? vp))
                return vp.Value.Value;
        }
        catch { }
        try
        {
            FlaUI.Core.AutomationElements.TextBox? tb = el.AsTextBox();
            if (tb is not null)
                return tb.Text;
        }
        catch { }
        try { return el.Properties.Name.ValueOrDefault; } catch { return null; }
    }

    // ── Get Clipboard (via Win32 P/Invoke) ─────────────────────────

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;

    public string GetClipboard()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return "(clipboard is locked or unavailable)";

            try
            {
                IntPtr hData = GetClipboardData(CF_UNICODETEXT);
                if (hData == IntPtr.Zero)
                    return "(clipboard is empty or does not contain text)";

                IntPtr pData = GlobalLock(hData);
                if (pData == IntPtr.Zero)
                    return "(clipboard lock failed)";

                try
                {
                    string? text = Marshal.PtrToStringUni(pData);
                    return text ?? "(clipboard is empty)";
                }
                finally
                {
                    GlobalUnlock(hData);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Drag Element ─────────────────────────────────────────────────

    public string DragElement(string appId, string? sourceAutomationId, string? sourceName, string? sourceControlType, int sourceIndex,
        string? targetAutomationId, string? targetName, string? targetControlType, int targetIndex)
    {
        AutomationElement? source = sourceIndex >= 0
            ? FindElementByIndex(appId, sourceAutomationId, sourceName, sourceControlType, sourceIndex)
            : FindElement(appId, sourceAutomationId, sourceName, sourceControlType);
        if (source is null)
            return "ERROR: Source element not found";

        AutomationElement? target = targetIndex >= 0
            ? FindElementByIndex(appId, targetAutomationId, targetName, targetControlType, targetIndex)
            : FindElement(appId, targetAutomationId, targetName, targetControlType);
        if (target is null)
            return "ERROR: Target element not found";

        if (!EnsureInteractiveForInput(appId))
            return "ERROR: Session is locked. Drag requires mouse input — unlock the session.";

        try
        {
            Rectangle srcRect = source.BoundingRectangle;
            Rectangle tgtRect = target.BoundingRectangle;
            Point srcCenter = new(srcRect.X + srcRect.Width / 2, srcRect.Y + srcRect.Height / 2);
            Point tgtCenter = new(tgtRect.X + tgtRect.Width / 2, tgtRect.Y + tgtRect.Height / 2);

            Mouse.MoveTo(srcCenter);
            Thread.Sleep(100);
            Mouse.Down(FlaUI.Core.Input.MouseButton.Left);
            Thread.Sleep(100);

            // Move in steps for smooth drag
            int steps = 10;
            for (int i = 1; i <= steps; i++)
            {
                int x = srcCenter.X + (tgtCenter.X - srcCenter.X) * i / steps;
                int y = srcCenter.Y + (tgtCenter.Y - srcCenter.Y) * i / steps;
                Mouse.MoveTo(new Point(x, y));
                Thread.Sleep(30);
            }

            Thread.Sleep(100);
            Mouse.Up(FlaUI.Core.Input.MouseButton.Left);

            string srcDesc, tgtDesc;
            try { srcDesc = $"[{source.ControlType}] \"{source.Name}\""; } catch { srcDesc = "[source]"; }
            try { tgtDesc = $"[{target.ControlType}] \"{target.Name}\""; } catch { tgtDesc = "[target]"; }
            return $"Dragged {srcDesc} → {tgtDesc}";
        }
        catch (Exception ex)
        {
            try { Mouse.Up(FlaUI.Core.Input.MouseButton.Left); } catch { }
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Get All Values (batch read all editable fields) ──────────────

    public string GetAllValues(string appId)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        try
        {
            AutomationElement[] allElements = GetCachedDescendants(appId, win);
            StringBuilder sb = new();
            int count = 0;

            foreach (AutomationElement el in allElements)
            {
                try
                {
                    ControlType ct = el.Properties.ControlType.ValueOrDefault;
                    if (ct != ControlType.Edit && ct != ControlType.Document && ct != ControlType.ComboBox && ct != ControlType.CheckBox)
                        continue;

                    string elId = el.Properties.AutomationId.ValueOrDefault ?? "";
                    string elName = el.Properties.Name.ValueOrDefault ?? "";
                    if (string.IsNullOrEmpty(elId) && string.IsNullOrEmpty(elName))
                        continue;

                    string label = !string.IsNullOrEmpty(elId) ? elId : elName;
                    string? value = null;

                    if (ct == ControlType.Edit || ct == ControlType.Document)
                    {
                        FlaUI.Core.AutomationElements.TextBox? tb = el.AsTextBox();
                        value = tb?.Text ?? "";
                    }
                    else if (ct == ControlType.ComboBox)
                    {
                        ComboBox? combo = el.AsComboBox();
                        value = combo?.SelectedItem?.Text ?? "";
                    }
                    else if (ct == ControlType.CheckBox)
                    {
                        CheckBox? cb = el.AsCheckBox();
                        value = cb?.IsChecked?.ToString() ?? "";
                    }

                    sb.AppendLine($"  [{ct}] {label} = \"{value}\"");
                    count++;
                }
                catch { }
            }

            if (count == 0)
                return "No editable fields found.";

            sb.Insert(0, $"Found {count} editable field(s):\n");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Get Tree Hash (for change detection) ─────────────────────────

    public string GetTreeHash(string appId)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        try
        {
            AutomationElement[] descendants = GetCachedDescendants(appId, win);
            StringBuilder fingerprint = new();
            int count = 0;

            foreach (AutomationElement el in descendants)
            {
                if (count >= 500) break;
                count++;
                try
                {
                    string ct = el.Properties.ControlType.ValueOrDefault.ToString();
                    string aid = el.Properties.AutomationId.ValueOrDefault ?? "";
                    string nm = el.Properties.Name.ValueOrDefault ?? "";
                    fingerprint.Append($"{ct}|{aid}|{nm};");
                }
                catch { }
            }

            byte[] hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(fingerprint.ToString()));
            string hashStr = Convert.ToHexString(hash)[..16];
            return $"hash={hashStr} elements={count}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Screenshot Diff ──────────────────────────────────────────────

    public string ScreenshotDiff(string imagePath1, string imagePath2, string? outputPath)
    {
        if (!System.IO.File.Exists(imagePath1))
            return $"ERROR: File not found: {imagePath1}";
        if (!System.IO.File.Exists(imagePath2))
            return $"ERROR: File not found: {imagePath2}";

        try
        {
            using Bitmap img1 = new(imagePath1);
            using Bitmap img2 = new(imagePath2);

            int width = Math.Min(img1.Width, img2.Width);
            int height = Math.Min(img1.Height, img2.Height);

            using Bitmap diff = new(width, height);
            int diffPixels = 0;
            int totalPixels = width * height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c1 = img1.GetPixel(x, y);
                    Color c2 = img2.GetPixel(x, y);
                    if (c1 != c2)
                    {
                        diffPixels++;
                        diff.SetPixel(x, y, Color.FromArgb(255, 255, 0, 0)); // Red highlight
                    }
                    else
                    {
                        // Dim the matching pixels
                        diff.SetPixel(x, y, Color.FromArgb(255, c1.R / 3, c1.G / 3, c1.B / 3));
                    }
                }
            }

            double pct = totalPixels > 0 ? (double)diffPixels / totalPixels * 100.0 : 0;

            if (!string.IsNullOrEmpty(outputPath))
            {
                string? dir = System.IO.Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);
                diff.Save(outputPath, ImageFormat.Png);
            }

            StringBuilder sb = new();
            sb.AppendLine($"DiffPercent: {pct:F2}%");
            sb.AppendLine($"DiffPixels: {diffPixels} / {totalPixels}");
            sb.AppendLine($"Image1: {img1.Width}x{img1.Height}");
            sb.AppendLine($"Image2: {img2.Width}x{img2.Height}");
            if (img1.Width != img2.Width || img1.Height != img2.Height)
                sb.AppendLine($"WARNING: Images differ in size, compared only overlapping region ({width}x{height})");
            if (!string.IsNullOrEmpty(outputPath))
                sb.AppendLine($"DiffImage: {outputPath}");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Annotate Screenshot ──────────────────────────────────────────

    public string AnnotateScreenshot(string appId, string outputPath, string[] automationIds)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        try
        {
            Bitmap? bmp = CaptureWindowBitmap(win);
            if (bmp is null)
                return "ERROR: Failed to capture window for annotation.";

            Rectangle winRect = win.BoundingRectangle;
            using Graphics g = Graphics.FromImage(bmp);
            using Pen pen = new(Color.Red, 3);
            using System.Drawing.Font font = new("Arial", 12, FontStyle.Bold);
            using SolidBrush labelBrush = new(Color.Red);
            using SolidBrush bgBrush = new(Color.FromArgb(200, 255, 255, 255));

            int annotated = 0;
            StringBuilder sb = new();

            foreach (string aid in automationIds)
            {
                AutomationElement? el = FindElement(appId, aid, null, null);
                if (el is null)
                {
                    sb.AppendLine($"  NOT FOUND: {aid}");
                    continue;
                }

                try
                {
                    Rectangle elRect = el.BoundingRectangle;
                    // Convert to image-relative coordinates
                    int x = elRect.X - winRect.X;
                    int y = elRect.Y - winRect.Y;
                    g.DrawRectangle(pen, x, y, elRect.Width, elRect.Height);

                    // Draw label
                    string label = aid;
                    SizeF labelSize = g.MeasureString(label, font);
                    float labelY = Math.Max(0, y - labelSize.Height - 2);
                    g.FillRectangle(bgBrush, x, labelY, labelSize.Width + 4, labelSize.Height);
                    g.DrawString(label, font, labelBrush, x + 2, labelY);
                    annotated++;
                    sb.AppendLine($"  OK: {aid} at ({x},{y} {elRect.Width}x{elRect.Height})");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  ERROR: {aid} — {ex.Message}");
                }
            }

            string? dir = System.IO.Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);
            bmp.Save(outputPath, ImageFormat.Png);

            sb.Insert(0, $"Annotated {annotated}/{automationIds.Length} elements, saved to {outputPath}\n");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Select ComboBox Option (eliminates 3-step click→wait→click) ─

    public string SelectOption(string appId, string? automationId, string? name, string optionText, int index = -1)
    {
        AutomationElement? el = index >= 0
            ? FindElementByIndex(appId, automationId, name, "ComboBox", index)
            : FindElement(appId, automationId, name, "ComboBox");
        if (el is null)
            return $"ERROR: ComboBox not found (automationId={automationId}, name={name})";

        InvalidateDescendantCache(appId);
        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[ComboBox]"; }

        try
        {
            // Try ExpandCollapsePattern to open the dropdown
            if (el.Patterns.ExpandCollapse.TryGetPattern(out FlaUI.Core.Patterns.IExpandCollapsePattern? expandPattern))
            {
                expandPattern.Expand();
                Thread.Sleep(300); // Wait for dropdown items to render
            }
            else
            {
                el.Click();
                Thread.Sleep(300);
            }

            // Now find the option as a child ListItem
            AutomationElement[] items;
            try
            {
                items = el.FindAllDescendants();
            }
            catch
            {
                // Some ComboBoxes render items in a popup window, search globally
                Window? win = GetMainWindow(appId);
                items = win?.FindAllDescendants() ?? Array.Empty<AutomationElement>();
            }

            foreach (AutomationElement item in items)
            {
                try
                {
                    string itemName = item.Properties.Name.ValueOrDefault ?? "";
                    if (itemName.Equals(optionText, StringComparison.OrdinalIgnoreCase) ||
                        itemName.Contains(optionText, StringComparison.OrdinalIgnoreCase))
                    {
                        // Try SelectionItemPattern first
                        if (item.Patterns.SelectionItem.TryGetPattern(out FlaUI.Core.Patterns.ISelectionItemPattern? selPattern))
                        {
                            selPattern.Select();
                            return $"Selected \"{itemName}\" in {elDesc}";
                        }
                        // Fallback to click
                        item.Click();
                        return $"Clicked option \"{itemName}\" in {elDesc}";
                    }
                }
                catch { }
            }

            // Collapse if we didn't find the option
            try
            {
                if (el.Patterns.ExpandCollapse.TryGetPattern(out FlaUI.Core.Patterns.IExpandCollapsePattern? collapsePattern))
                    collapsePattern.Collapse();
            }
            catch { }

            return $"ERROR: Option \"{optionText}\" not found in {elDesc}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ── Desktop element listing (for finding windows to attach) ─────

    public string ListDesktopWindows()
    {
        AutomationElement desktop = _automation.GetDesktop();
        AutomationElement[] windows = desktop.FindAllChildren(
            _automation.ConditionFactory.ByControlType(ControlType.Window));

        StringBuilder sb = new();
        foreach (AutomationElement win in windows)
        {
            try
            {
                int pid = win.Properties.ProcessId.ValueOrDefault;
                sb.AppendLine($"  \"{win.Name}\" | PID={pid} | AutomationId=\"{win.AutomationId}\" | Class=\"{win.ClassName}\"");
            }
            catch { }
        }

        return sb.Length == 0 ? "No windows found on desktop." : sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════
    // ── HWND-based window resolution ─────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    /// <summary>
    /// Gets a UIA element from a raw HWND. Enables per-window targeting
    /// when an app has multiple top-level windows.
    /// </summary>
    private AutomationElement? GetElementFromHwnd(long windowHandle)
    {
        IntPtr hwnd = new(windowHandle);
        if (!IsWindow(hwnd))
            return null;

        try
        {
            return _automation.FromHandle(hwnd);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Find element within a specific HWND-targeted window, with optional fuzzy name matching.
    /// </summary>
    private AutomationElement? FindElementInHwnd(long windowHandle, string? automationId, string? name, string? controlType, bool fuzzyMatch = false)
    {
        AutomationElement? root = GetElementFromHwnd(windowHandle);
        if (root is null)
            return null;

        try
        {
            AutomationElement[] descendants = root.FindAllDescendants();
            foreach (AutomationElement el in descendants)
            {
                try
                {
                    bool match = true;
                    if (!string.IsNullOrEmpty(automationId))
                        match &= (el.Properties.AutomationId.ValueOrDefault ?? "") == automationId;
                    if (!string.IsNullOrEmpty(name))
                    {
                        string elName = el.Properties.Name.ValueOrDefault ?? "";
                        if (fuzzyMatch)
                            match &= FuzzyContains(elName, name);
                        else
                            match &= elName.Equals(name, StringComparison.OrdinalIgnoreCase) || elName.Contains(name, StringComparison.OrdinalIgnoreCase);
                    }
                    if (!string.IsNullOrEmpty(controlType) && Enum.TryParse<ControlType>(controlType, true, out ControlType ct))
                        match &= el.Properties.ControlType.ValueOrDefault == ct;
                    if (match)
                        return el;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Fuzzy matching ───────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Simple fuzzy match: checks if all words in the query appear in the target
    /// (in any order), case-insensitive. Falls back to Levenshtein distance for
    /// single-word queries.
    /// </summary>
    private static bool FuzzyContains(string target, string query)
    {
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(query))
            return false;

        // Exact or substring match (fast path)
        if (target.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        // Multi-word: all words must appear
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            return words.All(w => target.Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        // Single word: Levenshtein distance <= 30% of query length
        int maxDist = Math.Max(1, query.Length / 3);
        return LevenshteinDistance(target.ToLowerInvariant(), query.ToLowerInvariant()) <= maxDist;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        // Optimization: if checking substring match is cheaper
        if (n > m * 2)
        {
            for (int i = 0; i <= n - m; i++)
            {
                int d = LevenshteinDistanceCore(s.Substring(i, m), t);
                if (d <= Math.Max(1, m / 3))
                    return d;
            }
        }

        return LevenshteinDistanceCore(s, t);
    }

    private static int LevenshteinDistanceCore(string s, string t)
    {
        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Fuzzy Find Elements ──────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    public string FindElementsFuzzy(string appId, string? controlType = null, string? idContains = null, string? nameContains = null, int maxResults = 50, long windowHandle = 0)
    {
        AutomationElement? root;
        if (windowHandle != 0)
        {
            root = GetElementFromHwnd(windowHandle);
            if (root is null)
                return $"ERROR: Invalid window handle: {windowHandle}";
        }
        else
        {
            root = GetMainWindow(appId);
        }
        if (root is null)
            return $"ERROR: Cannot get window for '{appId}'";

        try
        {
            AutomationElement[] allElements;
            if (windowHandle != 0)
                allElements = root.FindAllDescendants();
            else
                allElements = GetCachedDescendants(appId, (Window)root);

            StringBuilder sb = new();
            int count = 0;

            foreach (AutomationElement el in allElements)
            {
                if (count >= maxResults) break;

                string elType = "", elId = "", elName = "", elClass = "";
                try { elType = el.Properties.ControlType.ValueOrDefault.ToString(); } catch { }
                try { elId = el.Properties.AutomationId.ValueOrDefault ?? ""; } catch { }
                try { elName = el.Properties.Name.ValueOrDefault ?? ""; } catch { }
                try { elClass = el.Properties.ClassName.ValueOrDefault ?? ""; } catch { }

                if (!string.IsNullOrEmpty(controlType) && !elType.Contains(controlType, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(idContains) && !FuzzyContains(elId, idContains))
                    continue;
                if (!string.IsNullOrEmpty(nameContains) && !FuzzyContains(elName, nameContains))
                    continue;
                if (string.IsNullOrEmpty(elId) && string.IsNullOrEmpty(elName))
                    continue;

                count++;
                sb.Append($"  [{elType}]");
                if (!string.IsNullOrEmpty(elId)) sb.Append($" id=\"{elId}\"");
                if (!string.IsNullOrEmpty(elName)) sb.Append($" name=\"{elName}\"");
                if (!string.IsNullOrEmpty(elClass)) sb.Append($" class=\"{elClass}\"");
                sb.AppendLine();
            }

            if (count == 0)
                return "No elements matched the fuzzy search.";

            sb.Insert(0, $"Found {count} element(s) (fuzzy match):\n");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── ExpandCollapsePattern ────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    public string ExpandCollapseElement(string appId, string? automationId, string? name, string? controlType, string action = "toggle", int index = -1, long windowHandle = 0)
    {
        AutomationElement? el;
        if (windowHandle != 0)
            el = FindElementInHwnd(windowHandle, automationId, name, controlType);
        else
            el = index >= 0 ? FindElementByIndex(appId, automationId, name, controlType, index) : FindElement(appId, automationId, name, controlType);

        if (el is null)
            return $"ERROR: Element not found (automationId={automationId}, name={name})";

        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }

        try
        {
            if (!el.Patterns.ExpandCollapse.TryGetPattern(out FlaUI.Core.Patterns.IExpandCollapsePattern? pattern))
                return $"ERROR: {elDesc} does not support ExpandCollapsePattern";

            ExpandCollapseState currentState = pattern.ExpandCollapseState.Value;

            switch (action.ToLowerInvariant())
            {
                case "expand":
                    pattern.Expand();
                    InvalidateDescendantCache(appId);
                    return $"Expanded {elDesc} (was {currentState})";
                case "collapse":
                    pattern.Collapse();
                    InvalidateDescendantCache(appId);
                    return $"Collapsed {elDesc} (was {currentState})";
                case "toggle":
                    if (currentState == ExpandCollapseState.Expanded || currentState == ExpandCollapseState.PartiallyExpanded)
                        pattern.Collapse();
                    else
                        pattern.Expand();
                    InvalidateDescendantCache(appId);
                    return $"Toggled {elDesc} (was {currentState})";
                default:
                    return $"ERROR: Invalid action '{action}'. Use 'expand', 'collapse', or 'toggle'.";
            }
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── ScrollItemPattern — Scroll element into view ─────────────────
    // ══════════════════════════════════════════════════════════════════

    public string ScrollIntoView(string appId, string? automationId, string? name, string? controlType, int index = -1, long windowHandle = 0)
    {
        AutomationElement? el;
        if (windowHandle != 0)
            el = FindElementInHwnd(windowHandle, automationId, name, controlType);
        else
            el = index >= 0 ? FindElementByIndex(appId, automationId, name, controlType, index) : FindElement(appId, automationId, name, controlType);

        if (el is null)
            return $"ERROR: Element not found (automationId={automationId}, name={name})";

        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }

        try
        {
            if (el.Patterns.ScrollItem.TryGetPattern(out FlaUI.Core.Patterns.IScrollItemPattern? scrollItemPattern))
            {
                scrollItemPattern.ScrollIntoView();
                return $"Scrolled {elDesc} into view via ScrollItemPattern";
            }

            // Fallback: find parent with ScrollPattern and scroll to bring element visible
            try
            {
                AutomationElement? parent = el.Parent;
                while (parent is not null)
                {
                    if (parent.Patterns.Scroll.TryGetPattern(out FlaUI.Core.Patterns.IScrollPattern? scrollPattern))
                    {
                        Rectangle elRect = el.BoundingRectangle;
                        Rectangle parentRect = parent.BoundingRectangle;

                        if (elRect.Bottom > parentRect.Bottom)
                        {
                            // Element is below visible area, scroll down
                            double targetPercent = Math.Min(100, scrollPattern.VerticalScrollPercent.Value + 20);
                            scrollPattern.SetScrollPercent(scrollPattern.HorizontalScrollPercent.Value, targetPercent);
                        }
                        else if (elRect.Top < parentRect.Top)
                        {
                            // Element is above visible area, scroll up
                            double targetPercent = Math.Max(0, scrollPattern.VerticalScrollPercent.Value - 20);
                            scrollPattern.SetScrollPercent(scrollPattern.HorizontalScrollPercent.Value, targetPercent);
                        }
                        return $"Scrolled parent to bring {elDesc} into view";
                    }
                    parent = parent.Parent;
                }
            }
            catch { }

            return $"ERROR: {elDesc} does not support ScrollItemPattern and no scrollable parent found";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── VirtualizedItemPattern — Realize virtualized items ───────────
    // ══════════════════════════════════════════════════════════════════

    public string RealizeVirtualizedItem(string appId, string? automationId, string? name, string? controlType, int index = -1, long windowHandle = 0)
    {
        AutomationElement? el;
        if (windowHandle != 0)
            el = FindElementInHwnd(windowHandle, automationId, name, controlType);
        else
            el = index >= 0 ? FindElementByIndex(appId, automationId, name, controlType, index) : FindElement(appId, automationId, name, controlType);

        if (el is null)
            return $"ERROR: Element not found (automationId={automationId}, name={name})";

        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }

        try
        {
            if (el.Patterns.VirtualizedItem.TryGetPattern(out FlaUI.Core.Patterns.IVirtualizedItemPattern? virtualizedPattern))
            {
                virtualizedPattern.Realize();
                InvalidateDescendantCache(appId);
                return $"Realized virtualized item {elDesc}";
            }
            return $"ERROR: {elDesc} does not support VirtualizedItemPattern (may already be realized)";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Screenshot with maxTokens auto-resize ────────────────────────
    // ══════════════════════════════════════════════════════════════════

    public string TakeScreenshotOptimized(string appId, string outputPath, int maxTokens = 0, long windowHandle = 0)
    {
        AutomationElement? root;
        if (windowHandle != 0)
            root = GetElementFromHwnd(windowHandle);
        else
            root = GetMainWindow(appId);

        if (root is null)
            return $"ERROR: Cannot get window for '{appId}'";

        try
        {
            Bitmap? bmp = CaptureWindowBitmap(root);
            if (bmp is null)
                return "ERROR: Failed to capture window. Window may not be renderable.";

            // If maxTokens is set, auto-resize to fit within token budget
            // Rough estimate: 1 token ≈ 0.75 pixels at JPEG quality, so maxTokens * 0.75 = max pixels
            // For a more practical estimate: 1000 tokens ≈ 768x768 image  
            if (maxTokens > 0)
            {
                // Approximate: tokens ≈ (width * height) / 750
                long currentPixels = (long)bmp.Width * bmp.Height;
                long maxPixels = (long)maxTokens * 750;

                if (currentPixels > maxPixels)
                {
                    double scale = Math.Sqrt((double)maxPixels / currentPixels);
                    int newWidth = Math.Max(100, (int)(bmp.Width * scale));
                    int newHeight = Math.Max(100, (int)(bmp.Height * scale));

                    Bitmap resized = new(newWidth, newHeight);
                    using (Graphics g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bmp, 0, 0, newWidth, newHeight);
                    }
                    bmp.Dispose();
                    bmp = resized;
                }
            }

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            bmp.Save(outputPath, ImageFormat.Png);
            string result = $"Screenshot saved to {outputPath} ({bmp.Width}x{bmp.Height})";
            bmp.Dispose();
            return result;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Event Monitoring ─────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    private readonly Dictionary<string, EventMonitorSession> _eventSessions = new();

    private sealed class EventMonitorSession
    {
        public string SessionId { get; set; } = "";
        public string EventType { get; set; } = "";
        public List<string> EventLog { get; } = new();
        public DateTime StartTime { get; set; }
        public bool IsActive { get; set; } = true;
        public FlaUI.Core.EventHandlers.AutomationEventHandlerBase? Handler { get; set; }
    }

    public string StartEventMonitor(string appId, string eventType, string? automationId = null, string? name = null, string? controlType = null, long windowHandle = 0)
    {
        AutomationElement? scope;
        if (windowHandle != 0)
            scope = GetElementFromHwnd(windowHandle);
        else if (!string.IsNullOrEmpty(automationId) || !string.IsNullOrEmpty(name))
            scope = FindElement(appId, automationId, name, controlType);
        else
            scope = GetMainWindow(appId);

        if (scope is null)
            return $"ERROR: Cannot find element to monitor";

        string sessionId = $"evt_{DateTime.UtcNow.Ticks}";
        EventMonitorSession session = new()
        {
            SessionId = sessionId,
            EventType = eventType,
            StartTime = DateTime.UtcNow,
            IsActive = true
        };

        try
        {
            if (eventType.Equals("focus", StringComparison.OrdinalIgnoreCase))
            {
                _automation.RegisterFocusChangedEvent(element =>
                {
                    if (!session.IsActive) return;
                    try
                    {
                        string desc = $"[{DateTime.UtcNow:HH:mm:ss.fff}] Focus → [{element.ControlType}] id=\"{element.AutomationId}\" name=\"{element.Name}\"";
                        lock (session.EventLog)
                        {
                            session.EventLog.Add(desc);
                            if (session.EventLog.Count > 500)
                                session.EventLog.RemoveAt(0);
                        }
                    }
                    catch { }
                });
            }
            else if (eventType.Equals("structurechanged", StringComparison.OrdinalIgnoreCase))
            {
                scope.RegisterStructureChangedEvent(FlaUI.Core.Definitions.TreeScope.Subtree, (element, changeType, runtimeIds) =>
                {
                    if (!session.IsActive) return;
                    try
                    {
                        string desc = $"[{DateTime.UtcNow:HH:mm:ss.fff}] Structure {changeType} → [{element.ControlType}] id=\"{element.AutomationId}\" name=\"{element.Name}\"";
                        lock (session.EventLog)
                        {
                            session.EventLog.Add(desc);
                            if (session.EventLog.Count > 500)
                                session.EventLog.RemoveAt(0);
                        }
                    }
                    catch { }
                });
            }
            else if (eventType.Equals("propertychanged", StringComparison.OrdinalIgnoreCase) || eventType.Equals("property", StringComparison.OrdinalIgnoreCase))
            {
                FlaUI.Core.Identifiers.PropertyId nameProp = _automation.PropertyLibrary.Element.Name;
                scope.RegisterPropertyChangedEvent(FlaUI.Core.Definitions.TreeScope.Subtree, (element, propertyId, newValue) =>
                {
                    if (!session.IsActive) return;
                    try
                    {
                        string desc = $"[{DateTime.UtcNow:HH:mm:ss.fff}] Property {propertyId} → [{element.ControlType}] id=\"{element.AutomationId}\" name=\"{element.Name}\" value=\"{newValue}\"";
                        lock (session.EventLog)
                        {
                            session.EventLog.Add(desc);
                            if (session.EventLog.Count > 500)
                                session.EventLog.RemoveAt(0);
                        }
                    }
                    catch { }
                }, nameProp);
            }
            else
            {
                return $"ERROR: Unsupported event type '{eventType}'. Supported: focus, structurechanged, propertychanged";
            }

            _eventSessions[sessionId] = session;
            return $"Event monitoring started. SessionId: {sessionId} | Type: {eventType} | Use stop_event_monitor to stop, get_event_log to read events.";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public string StopEventMonitor(string? sessionId = null)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            // Stop all sessions
            int count = _eventSessions.Count;
            foreach (EventMonitorSession session in _eventSessions.Values)
                session.IsActive = false;

            try { _automation.UnregisterAllEvents(); } catch { }
            _eventSessions.Clear();
            return $"Stopped {count} event monitoring session(s).";
        }

        if (!_eventSessions.TryGetValue(sessionId, out EventMonitorSession? target))
            return $"ERROR: Session '{sessionId}' not found. Active sessions: {string.Join(", ", _eventSessions.Keys)}";

        target.IsActive = false;
        int eventCount = target.EventLog.Count;
        _eventSessions.Remove(sessionId);

        try { _automation.UnregisterAllEvents(); } catch { }

        // Re-register remaining active sessions (FlaUI doesn't support selective unregister)
        return $"Stopped session {sessionId}. Captured {eventCount} events.";
    }

    public string GetEventLog(string? sessionId = null, int maxCount = 100)
    {
        if (_eventSessions.Count == 0)
            return "No active event monitoring sessions.";

        if (!string.IsNullOrEmpty(sessionId))
        {
            if (!_eventSessions.TryGetValue(sessionId, out EventMonitorSession? session))
                return $"ERROR: Session '{sessionId}' not found.";

            return FormatEventLog(session, maxCount);
        }

        // Return all sessions
        StringBuilder sb = new();
        foreach (EventMonitorSession session in _eventSessions.Values)
        {
            sb.AppendLine(FormatEventLog(session, maxCount));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatEventLog(EventMonitorSession session, int maxCount)
    {
        StringBuilder sb = new();
        sb.AppendLine($"Session: {session.SessionId} | Type: {session.EventType} | Active: {session.IsActive} | Started: {session.StartTime:HH:mm:ss}");

        List<string> events;
        lock (session.EventLog)
        {
            events = session.EventLog.TakeLast(maxCount).ToList();
        }

        sb.AppendLine($"Events ({events.Count} of {session.EventLog.Count} total):");
        foreach (string evt in events)
            sb.AppendLine($"  {evt}");

        if (events.Count == 0)
            sb.AppendLine("  (no events captured yet)");

        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════
    // ── GridPattern — Direct grid cell access ────────────────────────
    // ══════════════════════════════════════════════════════════════════

    public string GetGridItem(string appId, int row, int column, string? automationId = null, string? name = null, string? controlType = null, int index = -1, long windowHandle = 0)
    {
        AutomationElement? el;
        if (windowHandle != 0)
            el = FindElementInHwnd(windowHandle, automationId, name, controlType ?? "DataGrid");
        else
            el = index >= 0 ? FindElementByIndex(appId, automationId, name, controlType ?? "DataGrid", index) : FindElement(appId, automationId, name, controlType ?? "DataGrid");

        if (el is null)
            return $"ERROR: Grid element not found (automationId={automationId}, name={name})";

        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[grid]"; }

        try
        {
            // Try via FlaUI Grid abstraction
            Grid? grid = el.AsGrid();
            if (grid is not null)
            {
                try
                {
                    if (row < 0 || row >= grid.RowCount)
                        return $"ERROR: Row {row} out of range (0-{grid.RowCount - 1})";
                    if (column < 0 || column >= grid.ColumnCount)
                        return $"ERROR: Column {column} out of range (0-{grid.ColumnCount - 1})";
                }
                catch { }
            }

            // Try GridPattern
            if (el.Patterns.Grid.TryGetPattern(out FlaUI.Core.Patterns.IGridPattern? gridPattern))
            {
                AutomationElement cell = gridPattern.GetItem(row, column);
                StringBuilder sb = new();
                try { sb.AppendLine($"ControlType: {cell.ControlType}"); } catch { }
                try { sb.AppendLine($"AutomationId: {cell.AutomationId}"); } catch { }
                try { sb.AppendLine($"Name: {cell.Name}"); } catch { }

                // Try to read cell value
                try
                {
                    if (cell.Patterns.Value.TryGetPattern(out FlaUI.Core.Patterns.IValuePattern? vp))
                        sb.AppendLine($"Value: {vp.Value.Value}");
                }
                catch { }

                try
                {
                    FlaUI.Core.AutomationElements.TextBox? tb = cell.AsTextBox();
                    if (tb?.Text is not null)
                        sb.AppendLine($"Text: {tb.Text}");
                }
                catch { }

                try { sb.AppendLine($"BoundingRectangle: {cell.BoundingRectangle}"); } catch { }
                return $"Grid item at [{row},{column}] in {elDesc}:\n{sb}";
            }

            return $"ERROR: {elDesc} does not support GridPattern";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── ItemContainerPattern — Find item by property ─────────────────
    // ══════════════════════════════════════════════════════════════════

    public string FindItemByProperty(string appId, string? propertyName, string? value, string? automationId = null, string? name = null, string? controlType = null, int index = -1, long windowHandle = 0)
    {
        AutomationElement? container;
        if (windowHandle != 0)
            container = FindElementInHwnd(windowHandle, automationId, name, controlType);
        else
            container = index >= 0 ? FindElementByIndex(appId, automationId, name, controlType, index) : FindElement(appId, automationId, name, controlType);

        if (container is null)
            return $"ERROR: Container element not found (automationId={automationId}, name={name})";

        string elDesc;
        try { elDesc = $"[{container.ControlType}] \"{container.Name}\""; } catch { elDesc = "[container]"; }

        try
        {
            // Try ItemContainerPattern
            if (container.Patterns.ItemContainer.TryGetPattern(out FlaUI.Core.Patterns.IItemContainerPattern? itemContainerPattern))
            {
                FlaUI.Core.Identifiers.PropertyId? propId = null;
                if (!string.IsNullOrEmpty(propertyName))
                {
                    propId = propertyName.ToLowerInvariant() switch
                    {
                        "name" => _automation.PropertyLibrary.Element.Name,
                        "automationid" => _automation.PropertyLibrary.Element.AutomationId,
                        _ => null
                    };
                }

                AutomationElement? found = itemContainerPattern.FindItemByProperty(null, propId ?? _automation.PropertyLibrary.Element.Name, value);
                if (found is not null)
                {
                    StringBuilder sb = new();
                    try { sb.AppendLine($"ControlType: {found.ControlType}"); } catch { }
                    try { sb.AppendLine($"AutomationId: {found.AutomationId}"); } catch { }
                    try { sb.AppendLine($"Name: {found.Name}"); } catch { }
                    try { sb.AppendLine($"IsOffscreen: {found.IsOffscreen}"); } catch { }
                    return $"Found item in {elDesc}:\n{sb}";
                }
                return $"No item found with {propertyName ?? "Name"} = \"{value}\" in {elDesc}";
            }

            // Fallback: search children manually
            AutomationElement[] children = container.FindAllDescendants();
            foreach (AutomationElement child in children)
            {
                try
                {
                    string matchValue = propertyName?.ToLowerInvariant() switch
                    {
                        "automationid" => child.Properties.AutomationId.ValueOrDefault ?? "",
                        _ => child.Properties.Name.ValueOrDefault ?? ""
                    };

                    if (!string.IsNullOrEmpty(value) && matchValue.Contains(value, StringComparison.OrdinalIgnoreCase))
                    {
                        StringBuilder sb = new();
                        try { sb.AppendLine($"ControlType: {child.ControlType}"); } catch { }
                        try { sb.AppendLine($"AutomationId: {child.AutomationId}"); } catch { }
                        try { sb.AppendLine($"Name: {child.Name}"); } catch { }
                        try { sb.AppendLine($"IsOffscreen: {child.IsOffscreen}"); } catch { }
                        return $"Found item in {elDesc} (fallback search):\n{sb}";
                    }
                }
                catch { }
            }

            return $"No item found with {propertyName ?? "Name"} = \"{value}\" in {elDesc}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── WaitForWindowInputIdle ───────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════

    public string WaitForInputIdle(string appId, int timeoutMs = 10000, long windowHandle = 0)
    {
        AutomationElement? root;
        if (windowHandle != 0)
        {
            root = GetElementFromHwnd(windowHandle);
        }
        else
        {
            root = GetMainWindow(appId);
        }
        if (root is null)
            return $"ERROR: Cannot get window for '{appId}'";

        try
        {
            // Try WindowPattern WaitForInputIdle
            if (root.Patterns.Window.TryGetPattern(out FlaUI.Core.Patterns.IWindowPattern? windowPattern))
            {
                bool idle = windowPattern.WaitForInputIdle(timeoutMs);
                return idle ? "Window is idle and ready for input." : $"Window did not become idle within {timeoutMs}ms.";
            }

            // Fallback: wait for the process to be idle
            if (_apps.TryGetValue(appId, out Application? app))
            {
                try
                {
                    Process process = Process.GetProcessById(app.ProcessId);
                    bool waited = process.WaitForInputIdle(timeoutMs);
                    return waited ? "Process is idle and ready for input." : $"Process did not become idle within {timeoutMs}ms.";
                }
                catch { }
            }

            // Last resort: simple poll-wait
            Thread.Sleep(Math.Min(timeoutMs, 2000));
            return "Waited for window idle (fallback delay).";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── HWND-aware click/type/invoke (wrappers) ──────────────────────
    // ══════════════════════════════════════════════════════════════════

    public string ClickElementHwnd(long windowHandle, string? automationId, string? name, string? controlType, bool fuzzyMatch = false)
    {
        AutomationElement? el = FindElementInHwnd(windowHandle, automationId, name, controlType, fuzzyMatch);
        if (el is null)
            return $"ERROR: Element not found in HWND {windowHandle}";

        el.Click();
        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }
        return $"Clicked {elDesc} in HWND {windowHandle}";
    }

    public string SetValueHwnd(long windowHandle, string value, string? automationId, string? name, string? controlType, bool fuzzyMatch = false)
    {
        AutomationElement? el = FindElementInHwnd(windowHandle, automationId, name, controlType, fuzzyMatch);
        if (el is null)
            return $"ERROR: Element not found in HWND {windowHandle}";

        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }

        try
        {
            if (el.Patterns.Value.TryGetPattern(out FlaUI.Core.Patterns.IValuePattern? vp))
            {
                vp.SetValue(value);
                return $"Set value \"{value}\" on {elDesc} in HWND {windowHandle}";
            }
        }
        catch { }

        FlaUI.Core.AutomationElements.TextBox? tb = el.AsTextBox();
        if (tb is not null)
        {
            tb.Text = value;
            return $"Set text \"{value}\" on {elDesc} in HWND {windowHandle}";
        }

        el.Focus();
        Thread.Sleep(50);
        Keyboard.TypeSimultaneously(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL, FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
        Thread.Sleep(30);
        Keyboard.Type(value);
        return $"Typed \"{value}\" into {elDesc} in HWND {windowHandle} (fallback)";
    }

    public string GetSnapshotHwnd(long windowHandle, int maxDepth = 3)
    {
        AutomationElement? root = GetElementFromHwnd(windowHandle);
        if (root is null)
            return $"ERROR: Invalid window handle: {windowHandle}";

        try
        {
            StringBuilder sb = new();
            try { sb.AppendLine($"Window: \"{root.Name}\" (HWND={windowHandle})"); } catch { sb.AppendLine($"Window: HWND={windowHandle}"); }

            AutomationElement[] descendants = root.FindAllDescendants();
            int count = 0;
            foreach (AutomationElement el in descendants)
            {
                if (count >= 500) { sb.AppendLine("... (truncated at 500 elements)"); break; }
                count++;

                string ct = "", aid = "", nm = "", cls = "";
                try { ct = el.Properties.ControlType.ValueOrDefault.ToString(); } catch { }
                try { aid = el.Properties.AutomationId.ValueOrDefault ?? ""; } catch { }
                try { nm = el.Properties.Name.ValueOrDefault ?? ""; } catch { }
                try { cls = el.Properties.ClassName.ValueOrDefault ?? ""; } catch { }

                if (string.IsNullOrEmpty(aid) && string.IsNullOrEmpty(nm))
                    continue;

                sb.Append($"  [{ct}]");
                if (!string.IsNullOrEmpty(aid)) sb.Append($" id=\"{aid}\"");
                if (!string.IsNullOrEmpty(nm)) sb.Append($" name=\"{nm}\"");
                if (!string.IsNullOrEmpty(cls)) sb.Append($" class=\"{cls}\"");
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Offscreen & Locked Session Support ───────────────────────────
    // ══════════════════════════════════════════════════════════════════

    // Win32 P/Invoke for window state management & offscreen capture
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out WRECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [StructLayout(LayoutKind.Sequential)]
    private struct WRECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WPOINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public uint length;
        public uint flags;
        public uint showCmd;
        public WPOINT ptMinPosition;
        public WPOINT ptMaxPosition;
        public WRECT rcNormalPosition;
    }

    private const int SW_RESTORE = 9;
    private const uint PW_RENDERFULLCONTENT = 2;
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;

    /// <summary>
    /// Checks if the Windows desktop session is locked (Win+L).
    /// When locked, mouse/keyboard simulation won't work, but UIA patterns still function.
    /// </summary>
    private static bool IsDesktopLocked()
    {
        try
        {
            IntPtr hDesktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
            if (hDesktop == IntPtr.Zero)
                return true;
            CloseDesktop(hDesktop);
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Gets the native HWND from a FlaUI AutomationElement.
    /// </summary>
    private static IntPtr GetNativeHandle(AutomationElement element)
    {
        try
        {
            return element.Properties.NativeWindowHandle.ValueOrDefault;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Restores a minimized window and brings it to the foreground.
    /// </summary>
    private static bool RestoreAndForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        try
        {
            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
            Thread.Sleep(300);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures the window is interactive for mouse/keyboard input.
    /// Auto-restores minimized windows. Returns false if session is locked.
    /// </summary>
    private bool EnsureInteractiveForInput(string appId)
    {
        if (IsDesktopLocked())
            return false;

        Window? win = GetMainWindow(appId);
        if (win is null)
            return true;

        IntPtr hwnd = GetNativeHandle(win);
        if (hwnd != IntPtr.Zero && IsIconic(hwnd))
            RestoreAndForeground(hwnd);

        return true;
    }

    /// <summary>
    /// Captures a window as a Bitmap. Works for visible, minimized, and locked-session windows.
    /// Strategy: FlaUI screen capture → PrintWindow API → restore + FlaUI.
    /// Caller must dispose the returned Bitmap.
    /// </summary>
    private Bitmap? CaptureWindowBitmap(AutomationElement windowElement)
    {
        IntPtr hwnd = GetNativeHandle(windowElement);
        bool isMinimized = hwnd != IntPtr.Zero && IsIconic(hwnd);
        bool isLocked = IsDesktopLocked();

        // Fast path: FlaUI capture for visible, unlocked windows
        if (!isMinimized && !isLocked)
        {
            try
            {
                FlaUI.Core.Capturing.CaptureImage capture = FlaUI.Core.Capturing.Capture.Element(windowElement);
                Bitmap result = (Bitmap)capture.Bitmap.Clone();
                capture.Dispose();
                return result;
            }
            catch { /* fall through to PrintWindow */ }
        }

        // PrintWindow: works for minimized/offscreen/locked windows
        if (hwnd != IntPtr.Zero)
        {
            Bitmap? printed = CaptureViaPrintWindow(hwnd);
            if (printed is not null)
                return printed;
        }

        // Last resort: restore window and retry FlaUI capture
        if (isMinimized && hwnd != IntPtr.Zero && !isLocked)
        {
            RestoreAndForeground(hwnd);
            try
            {
                FlaUI.Core.Capturing.CaptureImage capture = FlaUI.Core.Capturing.Capture.Element(windowElement);
                Bitmap result = (Bitmap)capture.Bitmap.Clone();
                capture.Dispose();
                return result;
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Uses Win32 PrintWindow API to capture a window bitmap.
    /// Works even when the window is minimized, behind other windows, or the session is locked.
    /// Requires Windows 8.1+ for PW_RENDERFULLCONTENT flag.
    /// </summary>
    private static Bitmap? CaptureViaPrintWindow(IntPtr hwnd)
    {
        int width, height;

        if (IsIconic(hwnd))
        {
            WINDOWPLACEMENT wp = new() { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref wp))
                return null;
            width = wp.rcNormalPosition.Right - wp.rcNormalPosition.Left;
            height = wp.rcNormalPosition.Bottom - wp.rcNormalPosition.Top;
        }
        else
        {
            if (!GetWindowRect(hwnd, out WRECT rect))
                return null;
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }

        if (width <= 0 || height <= 0)
            return null;

        Bitmap bmp = new(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            bool success = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);

            if (!success)
            {
                bmp.Dispose();
                return null;
            }
        }
        return bmp;
    }

    /// <summary>
    /// Attempts to click an element using UIA patterns (InvokePattern, SelectionItemPattern, TogglePattern).
    /// Returns the result string if successful, null if no pattern is available.
    /// </summary>
    private string? TryClickViaPattern(AutomationElement el, string appId)
    {
        string elDesc;
        try { elDesc = $"[{el.ControlType}] \"{el.Name}\""; } catch { elDesc = "[element]"; }

        try
        {
            if (el.Patterns.Invoke.TryGetPattern(out FlaUI.Core.Patterns.IInvokePattern? invokePattern))
            {
                invokePattern.Invoke();
                InvalidateDescendantCache(appId);
                return $"Invoked {elDesc} (pattern-based, window not interactive)";
            }
        }
        catch { }

        try
        {
            if (el.Patterns.SelectionItem.TryGetPattern(out FlaUI.Core.Patterns.ISelectionItemPattern? selPattern))
            {
                selPattern.Select();
                InvalidateDescendantCache(appId);
                return $"Selected {elDesc} (pattern-based, window not interactive)";
            }
        }
        catch { }

        try
        {
            if (el.Patterns.Toggle.TryGetPattern(out FlaUI.Core.Patterns.ITogglePattern? togglePattern))
            {
                togglePattern.Toggle();
                InvalidateDescendantCache(appId);
                return $"Toggled {elDesc} (pattern-based, window not interactive)";
            }
        }
        catch { }

        return null;
    }

    // ── Public API: Window Restore & Session Status ───────────────────

    public string RestoreWindow(string appId)
    {
        Window? win = GetMainWindow(appId);
        if (win is null)
            return $"ERROR: Cannot get main window for '{appId}'";

        IntPtr hwnd = GetNativeHandle(win);
        if (hwnd == IntPtr.Zero)
            return "ERROR: Cannot get native window handle";

        bool wasMinimized = IsIconic(hwnd);
        RestoreAndForeground(hwnd);

        return wasMinimized
            ? $"Restored window \"{win.Title}\" from minimized state and brought to foreground."
            : $"Brought window \"{win.Title}\" to foreground.";
    }

    public string CheckSessionStatus(string appId)
    {
        StringBuilder sb = new();
        bool isLocked = IsDesktopLocked();
        sb.AppendLine($"SessionLocked: {isLocked}");

        if (isLocked)
        {
            sb.AppendLine("Impact: Mouse/keyboard simulation will NOT work. Screenshots use PrintWindow API (may differ from visual).");
            sb.AppendLine("Works: UIA pattern operations (invoke_element, read_element, get_snapshot, find_elements, set_element_value via ValuePattern)");
            sb.AppendLine("Recommendation: Use invoke_element instead of click_element. Use set_element_value instead of type_text.");
        }

        Window? win = GetMainWindow(appId);
        if (win is not null)
        {
            IntPtr hwnd = GetNativeHandle(win);
            bool isMinimized = hwnd != IntPtr.Zero && IsIconic(hwnd);
            sb.AppendLine($"WindowMinimized: {isMinimized}");
            try { sb.AppendLine($"WindowTitle: \"{win.Title}\""); } catch { }

            if (isMinimized && !isLocked)
            {
                sb.AppendLine("Impact: Mouse/keyboard operations will auto-restore the window. Screenshots use PrintWindow API.");
                sb.AppendLine("Recommendation: All operations work automatically. Use restore_window to bring window up manually.");
            }
            else if (isMinimized && isLocked)
            {
                sb.AppendLine("Impact: Window is minimized AND session is locked. Only UIA pattern operations will work.");
            }
            else if (!isMinimized && !isLocked)
            {
                sb.AppendLine("Status: Window is visible and session is active. All operations available.");
            }
        }

        return sb.ToString();
    }
}
