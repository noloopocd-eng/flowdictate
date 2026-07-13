using System.IO;
using FlowDictate.Core;
using FlowDictate.Hotkeys;
using FlowDictate.UI;

namespace FlowDictate;

/// <summary>Tray application shell: owns the tray icon, debug window, hotkey, and pipeline.</summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly DebugWindow _debug = new();
    private readonly StatusOverlay _overlay = new();
    private readonly HotkeyMonitor _hotkey = new();
    private readonly AppSettings _settings;
    private readonly DictationPipeline _pipeline;

    private string IdleText => $"FlowDictate — idle (hold {_settings.HotkeyName} to talk)";

    public TrayContext()
    {
        _settings = AppSettings.Load();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Debug Log", null, (_, _) => ShowDebug());
        menu.Items.Add("Open Settings File", null, (_, _) => OpenSettingsFile());
        var startupItem = new ToolStripMenuItem("Start with Windows") { Checked = StartupManager.IsEnabled(), CheckOnClick = true };
        startupItem.CheckedChanged += (_, _) => StartupManager.SetEnabled(startupItem.Checked);
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.Idle,
            Text = "FlowDictate — loading model...",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowDebug();

        _pipeline = new DictationPipeline(_settings, _debug.Log, SynchronizationContext.Current!);
        _pipeline.ProcessingStarted += () =>
        {
            SetStatus(TrayIcons.Processing, "FlowDictate — processing...");
            if (_settings.ShowStatusOverlay) _overlay.ShowProcessing();
        };
        _pipeline.Completed += text =>
        {
            SetStatus(TrayIcons.Idle, IdleText);
            if (_settings.ShowStatusOverlay) _overlay.ShowDone(text.Length > 0);
        };

        _hotkey.VirtualKey = _settings.HotkeyVirtualKey;
        _hotkey.SuppressKey = _settings.HotkeyShouldBeSuppressed;
        // Heavy work must not run inside the keyboard-hook callback (blocks all keyboard
        // input system-wide and risks the hook being removed) — post it to the UI queue.
        var sync = SynchronizationContext.Current!;
        _hotkey.Started += mode => sync.Post(_ =>
        {
            SetStatus(TrayIcons.Listening, $"FlowDictate — listening ({mode})");
            if (_settings.ShowStatusOverlay) _overlay.ShowListening(mode == HotkeyMonitor.ListeningMode.HandsFree);
            if (mode == HotkeyMonitor.ListeningMode.PushToTalk)
            {
                _pipeline.StartRecording(); // first: audio pre-roll covers the gap
                string app = Core.ForegroundApp.GetProcessName();
                _pipeline.CaptureTargetApp(_settings);
                _pipeline.SetSelectionContext(
                    Insertion.SelectionReader.TryGetSelectedText(_settings.ClipboardSelectionFallback, app));
            }
            else
                _debug.Log("Hands-free mode: recording continues until you tap again.");
        }, null);
        _hotkey.Stopped += commit => sync.Post(_ =>
        {
            if (commit)
                _ = _pipeline.StopAndProcessAsync();
            else
            {
                _pipeline.CancelRecording();
                _overlay.HideOverlay();
                SetStatus(TrayIcons.Idle, IdleText);
            }
        }, null);
        _hotkey.Start();

        _debug.Log($"FlowDictate started. Hotkey = {_settings.HotkeyName} (hold = push-to-talk, double-tap = hands-free, tap again = stop)."
            + (_settings.HotkeyShouldBeSuppressed ? $" Shift+{_settings.HotkeyName} keeps its normal function." : ""));
        if (_settings.ShowDebugWindowOnStartup) ShowDebug();

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _pipeline.InitializeAsync();
            SetStatus(TrayIcons.Idle, IdleText);
        }
        catch (FileNotFoundException ex)
        {
            _debug.Log($"MODEL MISSING: {ex.Message}");
            SetStatus(TrayIcons.Idle, "FlowDictate — model missing (see debug log)");
        }
        catch (Exception ex)
        {
            _debug.Log($"Init failed: {ex}");
        }
    }

    private void ShowDebug()
    {
        _debug.Show();
        _debug.WindowState = FormWindowState.Normal;
        _debug.Activate();
    }

    private void OpenSettingsFile()
    {
        _settings.Save(); // ensure it exists
        string path = Path.Combine(AppSettings.AppDataDir, "settings.json");
        // Open in Notepad directly — .json has no default handler on many machines,
        // and Notepad is guaranteed present, so this never fails to find a program.
        System.Diagnostics.Process.Start("notepad.exe", $"\"{path}\"");
    }

    private void SetStatus(Icon icon, string text)
    {
        _tray.Icon = icon;
        _tray.Text = text.Length <= 63 ? text : text[..63]; // NotifyIcon tooltip limit
    }

    private void ExitApp()
    {
        _hotkey.Dispose();
        _pipeline.Dispose();
        _overlay.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }
}
