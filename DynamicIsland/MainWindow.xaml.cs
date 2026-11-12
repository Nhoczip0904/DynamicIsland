using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Media.Control;
using Windows.Storage.Streams;
using XamlAnimatedGif;
using Screen = System.Windows.Forms.Screen;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    enum View { Compact, Media, Level }

    record Settings(string? Screen, string? Character = null);

    static readonly Color IslandBlack = Color.FromRgb(0x05, 0x05, 0x05), DefaultAccent = Color.FromRgb(0x7D, 0xD3, 0xFC);

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", AppName = "DynamicIsland";
    static readonly string SettingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "settings.json");

    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    SystemLevels? _levels;
    Settings _settings = LoadSettings();
    View _view = View.Compact;
    bool _playing, _hasTimeline;
    string _lastTrack = "";
    readonly DispatcherTimer _revertTimer = new();
    readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly DispatcherTimer _danceTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly SolidColorBrush _islandBrush = new(IslandBlack), _accentBrush = new(DefaultAccent);
    double _beat;

    public MainWindow()
    {
        InitializeComponent();
        Island.Background = _islandBrush;
        ProgressFill.Background = _accentBrush;
        Island.SizeChanged += (_, _) =>
        {
            Island.CornerRadius = new CornerRadius(Math.Min(Island.ActualHeight / 2, 26));
            ChibiOffset.X = Island.ActualWidth / 2 + 4 + Chibi.Width / 2; // stand just right of the island
        };
        _revertTimer.Tick += (_, _) => { _revertTimer.Stop(); Show(Island.IsMouseOver ? View.Media : View.Compact); };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _danceTimer.Tick += (_, _) => Dance();
        AnimationBehavior.AddLoadedHandler(Chibi, (_, _) => SyncDance());
        Progress.Visibility = Visibility.Collapsed;
        BuildEqualizer();
        LoadCharacter();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never take focus from the app being used, and stay out of Alt+Tab
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlaceOnScreen();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _levels = new SystemLevels();
        _levels.VolumeChanged += (level, muted) => Dispatcher.InvokeAsync(() => ShowLevel(VolumeGlyph(level, muted), muted ? 0 : level));
        _levels.BrightnessChanged += level => Dispatcher.InvokeAsync(() => ShowLevel("", level));

        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => Dispatcher.InvokeAsync(AttachSession);
        AttachSession();
    }

    void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _levels?.Dispose();
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(PlaceOnScreen);

    // ---------- Media ----------

    void AttachSession()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
        _session = _manager?.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
        _ = RefreshMediaAsync();
        RefreshPlayback();
    }

    void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
        Dispatcher.InvokeAsync(RefreshMediaAsync);

    void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
        Dispatcher.InvokeAsync(RefreshPlayback);

    async Task RefreshMediaAsync()
    {
        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try { if (_session != null) props = await _session.TryGetMediaPropertiesAsync(); }
        catch (COMException) { } // session closed while reading

        string title = string.IsNullOrWhiteSpace(props?.Title) ? "Không có nhạc" : props!.Title;
        SmallTitle.Text = BigTitle.Text = title;
        BigArtist.Text = props?.Artist ?? "";

        ImageSource? cover = await LoadCoverAsync(props?.Thumbnail);
        Brush art = cover != null ? new ImageBrush(cover) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        SmallArt.Background = BigArt.Background = art;
        Tint(cover is BitmapSource bitmap && bitmap.PixelWidth > 0 ? Palette.Accent(Pixels(bitmap)) : null);

        // Like iOS: briefly open the island when a new track starts
        string track = title + "|" + BigArtist.Text;
        if (props != null && track != _lastTrack && _lastTrack != "" && !Island.IsMouseOver) ShowFor(View.Media, TimeSpan.FromSeconds(3));
        _lastTrack = track;
    }

    static async Task<ImageSource?> LoadCoverAsync(IRandomAccessStreamReference? reference)
    {
        if (reference == null) return null;
        try
        {
            using IRandomAccessStreamWithContentType ras = await reference.OpenReadAsync();
            using Stream stream = ras.AsStreamForRead();
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; } // some players expose a broken or empty thumbnail
    }

    void RefreshPlayback()
    {
        _playing = _session?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayGlyph.Text = _playing ? "" : "";
        Equalizer.Visibility = _playing ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress();
        SyncDance();
    }

    // Downscale first: 24×24 is plenty for an accent colour and keeps this cheap
    static byte[] Pixels(BitmapSource image)
    {
        BitmapSource small = new FormatConvertedBitmap(
            new TransformedBitmap(image, new ScaleTransform(24.0 / image.PixelWidth, 24.0 / image.PixelHeight)),
            PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[small.PixelWidth * small.PixelHeight * 4];
        small.CopyPixels(pixels, small.PixelWidth * 4, 0);
        return pixels;
    }

    void Tint(Color? accent)
    {
        // Background stays dark so white text keeps its contrast; bars get a brighter version
        Color background = accent is Color a ? Palette.Mix(a, Colors.Black, 0.78) : IslandBlack;
        Color bars = accent is Color c ? Palette.Mix(c, Colors.White, 0.3) : DefaultAccent;
        Duration duration = TimeSpan.FromMilliseconds(600);
        _islandBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(background, duration));
        _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(bars, duration));
    }

    // ---------- Character: gif beside the island, bounces to the audio ----------

    void LoadCharacter()
    {
        string? path = _settings.Character;
        bool has = path != null && File.Exists(path);
        Chibi.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        AnimationBehavior.SetSourceUri(Chibi, has ? new Uri(path!) : null);
        SyncDance();
    }

    void SyncDance()
    {
        bool dance = _playing && Chibi.Visibility == Visibility.Visible;
        Animator? animator = AnimationBehavior.GetAnimator(Chibi);
        if (dance) { animator?.Play(); _danceTimer.Start(); return; }

        animator?.Pause();
        _danceTimer.Stop();
        _beat = 0;
        ChibiOffset.Y = 0;
        ChibiSquash.ScaleX = ChibiSquash.ScaleY = 1;
    }

    void Dance()
    {
        _beat = Math.Max(_levels?.Peak ?? 0, _beat * 0.85); // jump on a hit, settle smoothly
        ChibiOffset.Y = -_beat * 10;
        ChibiSquash.ScaleY = 1 + _beat * 0.08;
        ChibiSquash.ScaleX = 1 - _beat * 0.05;
    }

    void PickCharacter_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new() { Title = "Chọn nhân vật", Filter = "Ảnh động GIF|*.gif" };
        if (dialog.ShowDialog() != true) return;
        _settings = _settings with { Character = dialog.FileName };
        SaveSettings();
        LoadCharacter();
    }

    void RemoveCharacter_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { Character = null };
        SaveSettings();
        LoadCharacter();
    }

    void UpdateProgress()
    {
        GlobalSystemMediaTransportControlsSessionTimelineProperties? t = _session?.GetTimelineProperties();
        TimeSpan duration = t == null ? TimeSpan.Zero : t.EndTime - t.StartTime;
        bool has = duration > TimeSpan.Zero; // some players don't publish a timeline
        if (has != _hasTimeline)
        {
            _hasTimeline = has;
            Progress.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (_view == View.Media) AnimateSize(SizeOf(View.Media));
        }
        if (t == null || !has || ProgressTrack.IsMouseCaptured) return; // don't fight the user's drag

        TimeSpan position = Timeline.Current(t.Position - t.StartTime, t.LastUpdatedTime, DateTimeOffset.Now, _playing, duration);
        PositionText.Text = Timeline.Format(position);
        DurationText.Text = Timeline.Format(duration);
        ProgressScale.ScaleX = position / duration;
    }

    // ---------- Seek: click or drag the progress bar ----------

    TimeSpan SeekPreview(System.Windows.Input.MouseEventArgs e)
    {
        GlobalSystemMediaTransportControlsSessionTimelineProperties t = _session!.GetTimelineProperties();
        TimeSpan duration = t.EndTime - t.StartTime;
        TimeSpan target = Timeline.AtFraction(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, duration);
        ProgressScale.ScaleX = target / duration;
        PositionText.Text = Timeline.Format(target);
        return t.StartTime + target;
    }

    void Progress_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_session == null || !_hasTimeline) return;
        ProgressTrack.CaptureMouse();
        SeekPreview(e);
        e.Handled = true;
    }

    void Progress_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ProgressTrack.IsMouseCaptured) SeekPreview(e);
    }

    async void Progress_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!ProgressTrack.IsMouseCaptured) return;
        TimeSpan target = SeekPreview(e);
        ProgressTrack.ReleaseMouseCapture();
        // Players that don't support seeking return false; the next tick snaps the bar back
        try { await _session!.TryChangePlaybackPositionAsync(target.Ticks); } catch (Exception) { }
    }

    async void PlayPause_Click(object sender, RoutedEventArgs e) { if (_session != null) await _session.TryTogglePlayPauseAsync(); }

    async void Next_Click(object sender, RoutedEventArgs e) { if (_session != null) await _session.TrySkipNextAsync(); }

    async void Previous_Click(object sender, RoutedEventArgs e) { if (_session != null) await _session.TrySkipPreviousAsync(); }

    // ---------- Volume / brightness ----------

    static string VolumeGlyph(float level, bool muted) =>
        muted || level == 0 ? "" : level < 0.34 ? "" : level < 0.67 ? "" : "";

    void ShowLevel(string glyph, float level)
    {
        LevelGlyph.Text = glyph;
        LevelText.Text = Math.Round(level * 100).ToString();
        LevelScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(level, TimeSpan.FromMilliseconds(120)));
        if (!Island.IsMouseOver) ShowFor(View.Level, TimeSpan.FromSeconds(1.5));
    }

    // ---------- Island shape ----------

    void BuildEqualizer()
    {
        double[] periods = [0.45, 0.6, 0.38, 0.52];
        foreach (double period in periods)
        {
            ScaleTransform scale = new(1, 0.4);
            Equalizer.Children.Add(new Rectangle
            {
                Width = 3, Height = 14, Margin = new Thickness(1, 0, 1, 0), RadiusX = 1.5, RadiusY = 1.5,
                Fill = _accentBrush,
                RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scale,
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(period))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            });
        }
    }

    Size SizeOf(View view) => view switch
    {
        View.Media => new Size(380, _hasTimeline ? 134 : 100),
        View.Level => new Size(280, 40),
        _ => new Size(190, 34),
    };

    void ShowFor(View view, TimeSpan duration)
    {
        Show(view);
        _revertTimer.Stop();
        _revertTimer.Interval = duration;
        _revertTimer.Start();
    }

    void Show(View view)
    {
        _view = view;
        if (view == View.Media) { UpdateProgress(); _progressTimer.Start(); }
        else _progressTimer.Stop();

        AnimateSize(SizeOf(view));
        Fade(Compact, view == View.Compact);
        Fade(Media, view == View.Media);
        Fade(Level, view == View.Level);
        Media.IsHitTestVisible = view == View.Media;
    }

    void AnimateSize(Size size)
    {
        Duration duration = TimeSpan.FromMilliseconds(380);
        IEasingFunction ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        Island.BeginAnimation(WidthProperty, new DoubleAnimation(size.Width, duration) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(size.Height, duration) { EasingFunction = ease });
    }

    static void Fade(UIElement element, bool visible) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 280 : 120)));

    void Island_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _revertTimer.Stop();
        Show(View.Media);
    }

    void Island_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => Show(View.Compact);

    // ---------- Menu: startup, monitor, exit ----------

    void Menu_Opened(object sender, RoutedEventArgs e)
    {
        using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey)) StartupItem.IsChecked = run?.GetValue(AppName) != null;
        RemoveCharacterItem.IsEnabled = _settings.Character != null;

        ScreenMenu.Items.Clear();
        Screen current = CurrentScreen();
        Screen[] screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            Screen screen = screens[i];
            MenuItem item = new()
            {
                Header = $"Màn hình {i + 1}{(screen.Primary ? " (chính)" : "")}",
                IsCheckable = true,
                IsChecked = screen.DeviceName == current.DeviceName,
            };
            item.Click += (_, _) => { _settings = _settings with { Screen = screen.DeviceName }; SaveSettings(); PlaceOnScreen(); };
            ScreenMenu.Items.Add(item);
        }
    }

    void Startup_Click(object sender, RoutedEventArgs e)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (StartupItem.IsChecked) key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(AppName, throwOnMissingValue: false);
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    Screen CurrentScreen() => Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.Screen) ?? Screen.PrimaryScreen!;

    void PlaceOnScreen()
    {
        // Physical pixels via Win32, so it lines up on monitors with different scaling
        System.Drawing.Rectangle bounds = CurrentScreen().Bounds;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        GetWindowRect(hwnd, out RECT rect);
        int x = bounds.X + (bounds.Width - (rect.Right - rect.Left)) / 2;
        SetWindowPos(hwnd, IntPtr.Zero, x, bounds.Y + 6, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    static Settings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new(null); }
        catch (Exception e) when (e is IOException or JsonException) { return new(null); } // first run or damaged file
    }

    void SaveSettings()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings));
    }

    // ---------- Win32 ----------

    const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
