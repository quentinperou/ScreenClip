using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace ScreenClip.Video;

/// <summary>
/// The microphones Windows has, and the one a video records (asked by the author): the one chosen in Settings ›
/// Capture or by a right click on the capture bar's microphone, else Windows' default input (Settings › Sound, not the
/// “communications” one, which a headset may have taken).
/// </summary>
internal static class Microphones
{
    /// <summary>A microphone to choose: its id (null: Windows' default) and its name.</summary>
    public sealed record Choice(string? Id, string Name);

    /// <summary>Windows' default first (with the name of the one it is now), then every microphone plugged in.</summary>
    public static IReadOnlyList<Choice> All()
    {
        var choices = new List<Choice>();
        string? current = null;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            try
            {
                current = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).FriendlyName;
            }
            catch (COMException)
            {
                // No microphone at all.
            }
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                choices.Add(new Choice(device.ID, device.FriendlyName));
        }
        catch (COMException)
        {
        }
        choices.Insert(0, new Choice(null, current is null ? Strings.Get("MicrophoneDefault") : Strings.Format("MicrophoneDefaultNamed", current)));
        return choices;
    }

    /// <summary>The name of the microphone chosen, as the settings keep it (no device asked).</summary>
    public static string ChosenName => App.Settings.MicrophoneName is { } name && App.Settings.MicrophoneId is not null
        ? name
        : Strings.Get("MicrophoneDefault");

    /// <summary>Records <paramref name="choice"/> from now on.</summary>
    public static void Choose(Choice choice)
    {
        App.Settings.MicrophoneId = choice.Id;
        App.Settings.MicrophoneName = choice.Id is null ? null : choice.Name;
        App.Settings.Save();
    }

    /// <summary>The microphone to record: the one chosen while it is plugged in, else Windows' default.</summary>
    public static MMDevice Open()
    {
        var enumerator = new MMDeviceEnumerator();
        if (App.Settings.MicrophoneId is { } id)
        {
            try
            {
                var device = enumerator.GetDevice(id);
                if (device.State == DeviceState.Active)
                    return device;
            }
            catch (COMException)
            {
                // Unplugged since: Windows' default.
            }
        }
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
    }
}
