using System.Management;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIsland;

/// <summary>Raises VolumeChanged / BrightnessChanged (on background threads) with a 0..1 level.</summary>
sealed class SystemLevels : IMMNotificationClient, IDisposable
{
    public event Action<float, bool>? VolumeChanged; // level, muted
    public event Action<float>? BrightnessChanged;

    readonly MMDeviceEnumerator _devices = new();
    readonly ManagementEventWatcher? _brightnessWatcher;
    MMDevice? _speaker;

    public SystemLevels()
    {
        _devices.RegisterEndpointNotificationCallback(this);
        WatchDefaultSpeaker();

        // Only laptop / built-in panels report brightness through WMI; desktops just never raise it
        try
        {
            _brightnessWatcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _brightnessWatcher.EventArrived += (_, e) => BrightnessChanged?.Invoke(Convert.ToByte(e.NewEvent["Brightness"]) / 100f);
            _brightnessWatcher.Start();
        }
        catch (ManagementException) { _brightnessWatcher = null; }
    }

    void WatchDefaultSpeaker()
    {
        if (_speaker != null) _speaker.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
        _speaker?.Dispose();
        try
        {
            _speaker = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _speaker.AudioEndpointVolume.OnVolumeNotification += OnVolume;
        }
        catch (Exception) { _speaker = null; } // no output device plugged in
    }

    /// <summary>Current output loudness 0..1 (all system audio, after volume).</summary>
    public float Peak
    {
        get
        {
            try { return _speaker?.AudioMeterInformation.MasterPeakValue ?? 0; }
            catch (Exception) { return 0; } // device unplugged between ticks
        }
    }

    void OnVolume(AudioVolumeNotificationData data) => VolumeChanged?.Invoke(data.MasterVolume, data.Muted);

    // Switching to headphones / Bluetooth changes the default device
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) WatchDefaultSpeaker();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _brightnessWatcher?.Stop();
        _brightnessWatcher?.Dispose();
        _devices.UnregisterEndpointNotificationCallback(this);
        _speaker?.Dispose();
        _devices.Dispose();
    }
}
