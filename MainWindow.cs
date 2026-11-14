using System.Text.Json;

namespace KeypadLayers;

internal sealed class MainWindow : Form
{
    private readonly Settings settings;
    private readonly SettingsStore store;
    internal readonly LayerEngine Engine;
    private readonly bool testing;
    private readonly IStartupRegistration startupRegistration;
    private readonly ListBox layers = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 46, IntegralHeight = false };
    private readonly TextBox layerName = Theme.TextBox();
    private readonly Label live = Theme.Label("", 12);
    private readonly Label editing = Theme.Label("", 18);
    private readonly Label status = Theme.Label("Ready", 9, true);
    private readonly Label editorTitle = Theme.Label("Key 1", 18);
    private readonly Label valueTitle = Theme.Label("Shortcut", 10, true);
    private readonly Label editorHint = Theme.Label("", 9, true);
    private readonly ComboBox action = Theme.Combo(), choice = Theme.Combo();
    private readonly TextBox value = Theme.TextBox(true);
    private readonly TextBox workingDirectory = Theme.TextBox();
    private readonly Label workingTitle = Theme.Label("Working directory", 10, true);
    private readonly CheckBox runHidden = new() { Text = "Run hidden", AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
    private readonly Button apply = Theme.Button("Save binding", true), record = Theme.Button("Record shortcut"), activate = Theme.Button("Make active", true), add = Theme.Button("+ Add layer"), duplicate = Theme.Button("Duplicate"), remove = Theme.Button("Delete"), pause = Theme.Button("Pause"), test = Theme.Button("Test in 3 seconds");
    private readonly CheckBox startup = new() { Text = "Use this layer on startup", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 4, 0, 16) };
    private readonly CheckBox overlayToggle = new() { Text = "Layer indicator", AutoSize = true, Margin = new Padding(0, 14, 0, 12) };
    private readonly Button themeToggle = Theme.Button("Light theme");
    private readonly ListView mappings = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly Dictionary<ControlId, Button> padButtons = [];
    private readonly TextBox testBox = Theme.TextBox(true);
    private readonly NotifyIcon? tray;
    private readonly LayerOverlay overlay = new();
    private KeypadHook? hook;
    private string editingId;
    private ControlId selectedControl;
    private bool loading, dirty, quitting, recording, saveFailure, runtimeStarted;
    private readonly System.Windows.Forms.Timer actionTimer = new() { Interval = 3000 };
    private ControlId testControl;

    internal MainWindow(Settings settings, SettingsStore store, IActionOutput output, bool testing = false, IStartupRegistration? startupRegistration = null)
    {
        this.settings = settings; this.store = store; this.testing = testing;
        this.startupRegistration = startupRegistration ?? new StartupRegistration();
        Theme.Set(settings.Theme);
        Engine = new(settings, output); editingId = settings.StartupLayerId;
        Text = "Keypad Layers"; Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text;
        MinimumSize = new Size(1240, 820); Size = new Size(1360, 900); StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi; Icon = AppIcon.Value;
        BuildLayout();
        WireEvents();
        Engine.LayerChanged += () => { RefreshView(); if (!testing && settings.ShowOverlay) overlay.ShowLayer(Engine.ActiveLayer.Name); };
        if (!testing)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Keypad Layers", null, (_, _) => OpenEditor());
            menu.Items.Add("Next layer", null, (_, _) => CycleFromTray());
            menu.Items.Add("Pause / resume", null, (_, _) => pause.PerformClick());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, (_, _) => { if (!ConfirmDiscard()) return; quitting = true; Close(); });
            tray = new() { Icon = AppIcon.Value, Visible = true, Text = "Keypad Layers", ContextMenuStrip = menu };
            tray.DoubleClick += (_, _) => OpenEditor();
        }
        ApplyTheme(); RefreshView(true); LoadEditor();
    }

    private Layer EditingLayer => settings.Layers.First(l => l.Id == editingId);

    private void BuildLayout()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3, BackColor = Theme.Background };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); page.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 530));
        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        brand.Controls.Add(Theme.Label("Keypad Layers", 26));
        var headerActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var setup = Theme.Button("Set up keypad", true); setup.Width = 150;
        setup.Click += (_, _) => { using var dialog = new DeviceSetupDialog(settings, SaveSettings, testing); dialog.ShowDialog(this); };
        var options = Theme.Button("Settings"); options.Width = 105;
        options.Click += (_, _) => { using var dialog = new PreferencesDialog(settings, startupRegistration, SaveSettings); dialog.ShowDialog(this); ApplyTheme(); };
        themeToggle.Width = 115; themeToggle.Click += (_, _) => ToggleTheme();
        pause.Width = 90; headerActions.Controls.Add(setup); headerActions.Controls.Add(options); headerActions.Controls.Add(themeToggle); headerActions.Controls.Add(pause);
        header.Controls.Add(brand, 0, 0); header.Controls.Add(headerActions, 1, 0);
        page.Controls.Add(header, 0, 0);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 202)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 318));
        columns.Controls.Add(BuildSidebar(), 0, 0); columns.Controls.Add(BuildCenter(), 1, 0); columns.Controls.Add(BuildEditor(), 2, 0);
        page.Controls.Add(columns, 0, 1);
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; status.AutoSize = false;
        page.Controls.Add(status, 0, 2); Controls.Add(page);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(0, 0, 18, 0), Margin = Padding.Empty };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 139)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        sidebar.Controls.Add(Theme.Label("Layers", 10, true), 0, 0); sidebar.Controls.Add(layers, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var button in new[] { add, duplicate, remove }) { button.Width = 180; buttons.Controls.Add(button); }
        sidebar.Controls.Add(buttons, 0, 2);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var import = Theme.Button("Import profile"); var export = Theme.Button("Export profile");
        import.Width = export.Width = 180; import.Click += (_, _) => ImportProfile(); export.Click += (_, _) => ExportProfile();
        bottom.Controls.Add(import); bottom.Controls.Add(export); sidebar.Controls.Add(bottom, 0, 3);
        return sidebar;
    }

    private Control BuildCenter()
    {
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(0, 0, 18, 0), Margin = Padding.Empty };
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 224)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); center.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); center.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        editing.Dock = DockStyle.Fill; center.Controls.Add(editing, 0, 0);
        var pad = new TableLayoutPanel { Dock = DockStyle.Fill, Tag = ThemeRole.Surface, BackColor = Theme.Card, Padding = new Padding(16), ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 0, 0, 16) };
        for (int c = 0; c < 3; c++) pad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        pad.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); pad.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        ControlId[] display = [ControlId.Key1, ControlId.Key2, ControlId.Key3, ControlId.Counterclockwise, ControlId.KnobPress, ControlId.Clockwise];
        for (int i = 0; i < display.Length; i++)
        {
            var id = display[i]; var button = Theme.Button(Labels.Control(id)); button.Dock = DockStyle.Fill; button.Margin = new Padding(5); button.AutoEllipsis = true;
            button.Click += (_, _) => SelectControl(id); padButtons[id] = button; pad.Controls.Add(button, i % 3, i / 3);
        }
        center.Controls.Add(pad, 0, 1); center.Controls.Add(Theme.Label("Bindings", 10, true), 0, 2);
        mappings.Columns.Add("Control", 112); mappings.Columns.Add("Action", 160); mappings.Columns.Add("Assignment", 220);
        mappings.OwnerDraw = true;
        mappings.DrawColumnHeader += (_, e) => { using var brush = new SolidBrush(Theme.Card); e.Graphics.FillRectangle(brush, e.Bounds); TextRenderer.DrawText(e.Graphics, e.Header?.Text, Font, Rectangle.Inflate(e.Bounds, -5, 0), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); };
        mappings.DrawSubItem += (_, e) => { bool selected = e.Item?.Selected == true; using var brush = new SolidBrush(selected ? Theme.Text : Theme.Card); e.Graphics.FillRectangle(brush, e.Bounds); TextRenderer.DrawText(e.Graphics, e.SubItem?.Text, Font, Rectangle.Inflate(e.Bounds, -5, 0), selected ? Theme.Background : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); };
        center.Controls.Add(mappings, 0, 3);
        var testingPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0, 14, 0, 0) };
        testingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); testingPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        testingPanel.Controls.Add(Theme.Label("Test area", 9, true), 0, 0);
        testBox.Dock = DockStyle.Fill; testBox.Width = 400; testBox.ScrollBars = ScrollBars.Vertical;
        testingPanel.Controls.Add(testBox, 0, 1); center.Controls.Add(testingPanel, 0, 4);
        return center;
    }

    private Control BuildEditor()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, Tag = ThemeRole.Surface, BackColor = Theme.Card, AutoScroll = true, Margin = Padding.Empty };
        var editor = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18), Width = 310 };
        editor.Controls.Add(Theme.Label("Active layer", 9, true)); editor.Controls.Add(live);
        activate.Width = 278; editor.Controls.Add(activate);
        editor.Controls.Add(Theme.Label("Layer name", 9, true)); layerName.Width = 278; layerName.MaxLength = 60; editor.Controls.Add(layerName);
        editor.Controls.Add(startup);
        editor.Controls.Add(editorTitle); editor.Controls.Add(Theme.Label("Action", 10, true));
        foreach (var kind in Enum.GetValues<ActionKind>()) action.Items.Add(Labels.Kind(kind));
        editor.Controls.Add(action); editor.Controls.Add(valueTitle); editor.Controls.Add(value); editor.Controls.Add(choice); editor.Controls.Add(record);
        editor.Controls.Add(workingTitle); editor.Controls.Add(workingDirectory); editor.Controls.Add(runHidden);
        editorHint.MaximumSize = new Size(278, 0); editor.Controls.Add(editorHint); apply.Width = 278; editor.Controls.Add(apply); test.Width = 278; editor.Controls.Add(test);
        overlayToggle.Checked = settings.ShowOverlay;
        overlayToggle.CheckedChanged += (_, _) => { settings.ShowOverlay = overlayToggle.Checked; SaveSettings(); };
        editor.Controls.Add(overlayToggle);
        scroll.Controls.Add(editor); return scroll;
    }

    private void WireEvents()
    {
        layers.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var layer = (Layer)layers.Items[e.Index];
            using var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Field : Theme.Background);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, (layer.Id == Engine.ActiveLayerId ? "●  " : "    ") + layer.Name, Font, Rectangle.Inflate(e.Bounds, -8, 0), layer.Id == Engine.ActiveLayerId ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        layers.SelectedIndexChanged += (_, _) =>
        {
            if (loading || layers.SelectedItem is not Layer selected || selected.Id == editingId) return;
            if (!ConfirmDiscard()) { RefreshView(true); return; }
            editingId = selected.Id; RefreshView(); LoadEditor();
        };
        mappings.SelectedIndexChanged += (_, _) =>
        {
            if (loading || mappings.SelectedIndices.Count == 0) return;
            SelectControl((ControlId)mappings.SelectedIndices[0]);
        };
        layerName.Validated += (_, _) => RenameLayer();
        layerName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { RenameLayer(); e.SuppressKeyPress = true; } };
        startup.CheckedChanged += (_, _) => { if (loading || !startup.Checked) return; settings.StartupLayerId = editingId; SaveSettings(); };
        add.Click += (_, _) =>
        {
            if (!ConfirmDiscard()) return;
            if (settings.Layers.Count == 50) { ShowError("A profile supports up to 50 layers."); return; }
            var layer = new Layer { Name = "Layer " + (settings.Layers.Count + 1) }; layer.Bindings[ControlId.KnobPress].Kind = ActionKind.NextLayer;
            settings.Layers.Add(layer); editingId = layer.Id; SaveSettings(); RefreshView(true); LoadEditor();
        };
        duplicate.Click += (_, _) =>
        {
            if (!ConfirmDiscard()) return;
            if (settings.Layers.Count == 50) { ShowError("A profile supports up to 50 layers."); return; }
            var copy = EditingLayer.Duplicate(); if (copy.Name.Length > 60) copy.Name = copy.Name[..60];
            settings.Layers.Add(copy); editingId = copy.Id; SaveSettings(); RefreshView(true); LoadEditor();
        };
        remove.Click += (_, _) =>
        {
            if (!ConfirmDiscard()) return;
            if (!testing && MessageBox.Show(this, "Delete this layer? Actions that jump to it will become Next layer.", "Delete layer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Engine.Remove(editingId); editingId = Engine.ActiveLayerId; SaveSettings(); RefreshView(true); LoadEditor();
        };
        activate.Click += (_, _) => { Engine.Activate(editingId); RefreshView(); };
        action.SelectedIndexChanged += (_, _) => { if (loading) return; dirty = true; UpdateActionFields(); };
        value.TextChanged += (_, _) => { if (!loading) { dirty = true; apply.Text = "Save binding *"; } };
        choice.SelectedIndexChanged += (_, _) => { if (!loading) { dirty = true; apply.Text = "Save binding *"; } };
        workingDirectory.TextChanged += (_, _) => { if (!loading) { dirty = true; apply.Text = "Save binding *"; } };
        runHidden.CheckedChanged += (_, _) => { if (!loading) { dirty = true; apply.Text = "Save binding *"; } };
        record.Click += (_, _) => { recording = !recording; record.Text = recording ? "Press your shortcut..." : "Record shortcut"; value.Focus(); };
        value.KeyDown += (_, e) =>
        {
            if (!recording) return;
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl"); if (e.Alt) parts.Add("Alt"); if (e.Shift) parts.Add("Shift");
            if ((Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0) parts.Add("Win");
            parts.Add(e.KeyCode.ToString()); value.Text = string.Join('+', parts); recording = false; record.Text = "Record shortcut";
        };
        apply.Click += (_, _) => ApplyBinding();
        test.Click += (_, _) =>
        {
            if (dirty) { ShowError("Save the binding before testing it."); return; }
            Engine.Activate(editingId); testControl = selectedControl; test.Enabled = false;
            status.Text = "Test runs in 3 seconds. Focus the scratchpad or the app you want to control."; actionTimer.Start();
        };
        actionTimer.Tick += (_, _) => { actionTimer.Stop(); test.Enabled = true; RunControl(testControl); };
        pause.Click += (_, _) => { if (hook is not null) hook.Enabled = !hook.Enabled; pause.Text = pause.Text == "Pause" ? "Resume" : "Pause"; status.Text = pause.Text == "Resume" ? "Paused. Keypad signals pass through without running actions." : "Listening for your keypad."; };
        FormClosing += (_, e) =>
        {
            if (testing || quitting || e.CloseReason == CloseReason.WindowsShutDown) return;
            e.Cancel = true; Hide();
            tray?.ShowBalloonTip(1500, "Keypad Layers is running", "Use the tray icon to reopen the editor or quit.", ToolTipIcon.Info);
        };
    }

    private void RenameLayer()
    {
        if (loading) return;
        string name = layerName.Text.Trim();
        if (name.Length == 0) { layerName.Text = EditingLayer.Name; return; }
        EditingLayer.Name = name; SaveSettings(); RefreshView(true);
    }

    internal void StartRuntime()
    {
        if (runtimeStarted) return;
        runtimeStarted = true;
        if (testing) return;
        try
        {
            hook = new();
            hook.Pressed += id =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(() => { if (!IsDisposed) RunControl(id); });
            };
            status.Text = settings.DeviceConfigured ? "Listening" : "Set up your keypad to begin.";
        }
        catch (Exception error) { pause.Enabled = false; ShowError(error.Message); }
    }

    private void RunControl(ControlId id)
    {
        try { var mapping = Engine.Trigger(id); status.Text = $"{Labels.Control(id)}  →  {Labels.Describe(mapping, settings)}    |    Live: {Engine.ActiveLayer.Name}"; }
        catch (Exception error) { ShowError(error.Message); }
    }

    private void CycleFromTray()
    {
        int index = settings.Layers.FindIndex(l => l.Id == Engine.ActiveLayerId);
        Engine.Activate(settings.Layers[(index + 1) % settings.Layers.Count].Id);
    }

    private void SelectControl(ControlId id)
    {
        if (id == selectedControl) return;
        if (id != selectedControl && !ConfirmDiscard()) { RefreshView(); return; }
        selectedControl = id; RefreshView(); LoadEditor();
    }

    private void RefreshView(bool rebuildLayers = false)
    {
        loading = true;
        try
        {
            // Remove() changes the active layer before deleting the old layer.
            if (!settings.Layers.Any(l => l.Id == editingId)) editingId = Engine.ActiveLayerId;
            if (rebuildLayers)
            {
                layers.Items.Clear(); foreach (var layer in settings.Layers) layers.Items.Add(layer);
                layers.SelectedItem = EditingLayer;
            }
            layers.Invalidate();
            live.Text = Engine.ActiveLayer.Name; live.ForeColor = Theme.Accent;
            editing.Text = EditingLayer.Name; layerName.Text = EditingLayer.Name;
            startup.Checked = settings.StartupLayerId == editingId; remove.Enabled = settings.Layers.Count > 1;
            activate.Enabled = Engine.ActiveLayerId != editingId;
            mappings.BeginUpdate(); mappings.Items.Clear();
            foreach (var id in Enum.GetValues<ControlId>())
            {
                var mapping = EditingLayer.Bindings[id];
                var item = new ListViewItem([Labels.Control(id), Labels.Kind(mapping.Kind), Labels.Describe(mapping, settings)]);
                mappings.Items.Add(item); item.Selected = id == selectedControl;
                padButtons[id].Text = Labels.Control(id) + "\n\n" + Labels.Describe(mapping, settings);
                padButtons[id].BackColor = id == selectedControl ? Theme.Text : Theme.Field;
                padButtons[id].ForeColor = id == selectedControl ? Theme.Background : Theme.Text;
            }
            mappings.EndUpdate();
            if (tray is not null) tray.Text = ("Keypad Layers · " + Engine.ActiveLayer.Name)[..Math.Min(63, 16 + Engine.ActiveLayer.Name.Length)];
        }
        finally { loading = false; }
    }

    private void LoadEditor()
    {
        loading = true;
        try
        {
            var mapping = EditingLayer.Bindings[selectedControl];
            editorTitle.Text = Labels.Control(selectedControl); action.SelectedIndex = (int)mapping.Kind;
            value.Text = mapping.Value; workingDirectory.Text = mapping.WorkingDirectory; runHidden.Checked = mapping.RunHidden; UpdateActionFields();
            if (mapping.Kind == ActionKind.Media) choice.SelectedItem = mapping.Value;
            if (mapping.Kind == ActionKind.SwitchLayer) choice.SelectedItem = settings.Layers.FirstOrDefault(l => l.Id == mapping.Value);
            recording = false; record.Text = "Record shortcut"; dirty = false; apply.Text = "Save binding";
        }
        finally { loading = false; }
    }

    private void UpdateActionFields()
    {
        var kind = (ActionKind)action.SelectedIndex;
        bool previousLoading = loading; loading = true;
        value.Visible = kind is ActionKind.Shortcut or ActionKind.Text or ActionKind.Launch or ActionKind.Command;
        value.MaxLength = kind == ActionKind.Command ? 32767 : 4096;
        workingTitle.Visible = workingDirectory.Visible = runHidden.Visible = kind == ActionKind.Command;
        choice.Visible = kind is ActionKind.Media or ActionKind.SwitchLayer; record.Visible = kind == ActionKind.Shortcut;
        valueTitle.Visible = value.Visible || choice.Visible;
        valueTitle.Text = kind switch { ActionKind.Shortcut => "Shortcut", ActionKind.Text => "Text", ActionKind.Launch => "App path or URL", ActionKind.Media => "Media action", ActionKind.Command => "Command line", _ => "Target layer" };
        choice.Items.Clear();
        if (kind == ActionKind.Media) foreach (string media in WindowsActions.MediaKeys.Keys) choice.Items.Add(media);
        if (kind == ActionKind.SwitchLayer) foreach (var layer in settings.Layers) choice.Items.Add(layer);
        if (choice.Items.Count > 0) choice.SelectedIndex = 0;
        editorHint.Text = kind switch
        {
            ActionKind.Shortcut => "Example: Ctrl+Shift+S. F13–F18 are reserved.",
            ActionKind.Launch => "An executable path or full URL.",
            ActionKind.Command => "Quote executable paths that contain spaces. Arguments are passed as written.",
            _ => ""
        };
        loading = previousLoading; apply.Text = dirty ? "Save binding *" : "Save binding";
    }

    private void ApplyBinding()
    {
        var old = EditingLayer.Bindings[selectedControl];
        var mapping = new Mapping { Kind = (ActionKind)action.SelectedIndex, Value = value.Text, WorkingDirectory = workingDirectory.Text, RunHidden = runHidden.Checked };
        if (mapping.Kind == ActionKind.Media) mapping.Value = choice.SelectedItem as string ?? "";
        if (mapping.Kind == ActionKind.SwitchLayer) mapping.Value = (choice.SelectedItem as Layer)?.Id ?? "";
        if (mapping.Kind is ActionKind.None or ActionKind.NextLayer or ActionKind.PreviousLayer) mapping.Value = "";
        EditingLayer.Bindings[selectedControl] = mapping;
        try { settings.Validate(); }
        catch (Exception error) { EditingLayer.Bindings[selectedControl] = old; ShowError(error.Message); return; }
        if (!SaveSettings()) { EditingLayer.Bindings[selectedControl] = old; return; }
        dirty = false; RefreshView(); LoadEditor(); status.Text = $"Saved {Labels.Control(selectedControl)} in {EditingLayer.Name}.";
    }

    private bool SaveSettings()
    {
        try { store.Save(settings); saveFailure = false; return true; }
        catch (Exception error) { saveFailure = true; ShowError("Could not save your profile: " + error.Message); return false; }
    }

    private bool ConfirmDiscard()
    {
        if (!dirty) return true;
        if (testing) { dirty = false; return true; }
        if (MessageBox.Show(this, "Discard changes you have not saved to this binding?", "Unsaved binding", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        dirty = false; return true;
    }

    private void ExportProfile()
    {
        if (dirty) { ShowError("Save your current binding before exporting."); return; }
        using var dialog = new SaveFileDialog { Filter = "Keypad Layers profile (*.json)|*.json", FileName = "my-keypad-profile.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(settings, SettingsStore.JsonOptions)); status.Text = "Profile exported."; }
        catch (Exception error) { ShowError(error.Message); }
    }
    private void ImportProfile()
    {
        if (!ConfirmDiscard()) return;
        using var dialog = new OpenFileDialog { Filter = "Keypad Layers profile (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = SettingsStore.Deserialize(File.ReadAllText(dialog.FileName));
            // Device setup is local to this PC, not a property of the imported profile.
            imported.DeviceConfigured = settings.DeviceConfigured;
            imported.StartWithWindows = settings.StartWithWindows; imported.StartMinimized = settings.StartMinimized; imported.Theme = settings.Theme;
            store.Save(imported);
            settings.Layers = imported.Layers; settings.StartupLayerId = imported.StartupLayerId; settings.ShowOverlay = imported.ShowOverlay;
            Engine.Activate(settings.StartupLayerId); editingId = settings.StartupLayerId;
            RefreshView(true); LoadEditor(); status.Text = "Profile imported.";
        }
        catch (Exception error) { ShowError("Import failed: " + error.Message); }
    }

    internal void OpenEditor() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void ToggleTheme()
    {
        settings.Theme = settings.Theme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
        ApplyTheme(); SaveSettings();
    }
    private void ApplyTheme()
    {
        Theme.Set(settings.Theme); Theme.Apply(this); Theme.Apply(overlay);
        themeToggle.Text = settings.Theme == ThemeMode.Dark ? "Light theme" : "Dark theme";
        mappings.Invalidate(); RefreshView();
    }
    private void ShowError(string message) { status.Text = message; if (!testing) MessageBox.Show(this, message, "Keypad Layers", MessageBoxButtons.OK, MessageBoxIcon.Warning); }

    internal void ExerciseUi()
    {
        add.PerformClick();
        if (settings.Layers.Count != 4) throw new InvalidOperationException("Add layer button failed.");
        layerName.Text = "Custom test layer"; RenameLayer();
        activate.PerformClick();
        if (Engine.ActiveLayer.Name != "Custom test layer") throw new InvalidOperationException("Make active button failed.");
        SelectControl(ControlId.Key1); action.SelectedIndex = (int)ActionKind.Text; value.Text = "Saved through the editor"; apply.PerformClick();
        if (Engine.ActiveLayer.Bindings[ControlId.Key1].Value != "Saved through the editor" || dirty || saveFailure) throw new InvalidOperationException("Save binding button failed.");
        SelectControl(ControlId.KnobPress); action.SelectedIndex = (int)ActionKind.SwitchLayer; choice.SelectedItem = settings.Layers[0]; apply.PerformClick();
        Engine.Trigger(ControlId.KnobPress);
        if (Engine.ActiveLayerId != settings.Layers[0].Id) throw new InvalidOperationException("Editor-created switch layer action failed.");
        duplicate.PerformClick();
        if (settings.Layers.Count != 5) throw new InvalidOperationException("Duplicate button failed.");
        remove.PerformClick();
        if (settings.Layers.Count != 4) throw new InvalidOperationException("Delete button failed.");
        pause.PerformClick(); if (pause.Text != "Resume") throw new InvalidOperationException("Pause button failed."); pause.PerformClick();
        SelectControl(ControlId.Key1); action.SelectedIndex = (int)ActionKind.Command;
        value.Text = "\"C:\\Program Files\\Example\\tool.exe\" --label \"a b\""; workingDirectory.Text = Path.GetTempPath(); runHidden.Checked = true; apply.PerformClick();
        var command = EditingLayer.Bindings[ControlId.Key1];
        if (command.Kind != ActionKind.Command || command.Value != value.Text || command.WorkingDirectory != Path.GetTempPath() || !command.RunHidden || dirty) throw new InvalidOperationException("Command editor failed to persist the full command and options.");
        if (store.Load().Settings.Layers.Count != 4) throw new InvalidOperationException("Editor changes were not persisted.");
    }

    internal void CaptureThemes(string directory)
    {
        foreach (var mode in Enum.GetValues<ThemeMode>())
        {
            settings.Theme = mode; ApplyTheme(); Application.DoEvents();
            if (BackColor != Theme.Background || padButtons[ControlId.Key1].ForeColor == padButtons[ControlId.Key1].BackColor) throw new InvalidOperationException("Theme contrast failed.");
            using var screenshot = new Bitmap(Width, Height); DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(directory, mode.ToString().ToLowerInvariant() + ".png"));
        }
        settings.Theme = ThemeMode.Dark; ApplyTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { hook?.Dispose(); tray?.Dispose(); overlay.Dispose(); actionTimer.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class LayerOverlay : Form
{
    private readonly Label text = Theme.Label("", 16);
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1500 };
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000080; return cp; } }
    public LayerOverlay()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Theme.Card; Size = new Size(320, 90); Padding = new Padding(18);
        text.Dock = DockStyle.Fill; text.ForeColor = Theme.Accent; text.AutoSize = false; text.TextAlign = ContentAlignment.MiddleLeft; text.AutoEllipsis = true; Controls.Add(text);
        timer.Tick += (_, _) => { timer.Stop(); Hide(); };
    }
    public void ShowLayer(string name)
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
        text.Text = name; timer.Stop(); Show(); timer.Start();
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
