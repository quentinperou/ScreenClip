using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenClip.Core;

namespace ScreenClip.Services;

/// <summary>What can be done with a capture: copy it, save it, hand it to another app as a file.</summary>
internal static class ClipActions
{
    private const int ClipboardBusy = unchecked((int)0x800401D0); // CLIPBRD_E_CANT_OPEN

    /// <summary>Files handed to other apps by drag and drop (they need a real file); emptied at each start.</summary>
    public static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "ScreenClip");

    /// <summary>
    /// Puts the capture on the clipboard as a bitmap (for every app) and as PNG (lossless, for the apps that read it:
    /// browsers, Office, Slack…). Another app may hold the clipboard for a moment: a few tries.
    /// </summary>
    public static bool Copy(BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", new MemoryStream(EncodePng(image)), autoConvert: false);
        return Copy(data);
    }

    /// <summary>Puts a file on the clipboard (a video): pasted in a folder, a mail or a chat, it comes as the file.</summary>
    public static bool CopyFile(string path)
    {
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
        return Copy(data);
    }

    /// <summary>Puts <paramref name="data"/> on the clipboard, trying again while another app holds it.</summary>
    public static bool Copy(DataObject data)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (COMException ex) when (ex.HResult == ClipboardBusy)
            {
                Thread.Sleep(50);
            }
        }
        return false;
    }

    /// <summary>Saves the capture in the capture folder, under a name never taken; returns its path.</summary>
    public static string Save(BitmapSource image, DateTime when, AppSettings settings)
    {
        string folder = settings.EffectiveSaveFolder;
        Directory.CreateDirectory(folder);
        string path = FileNaming.UniquePath(folder, FileNaming.Expand(settings.EffectiveFileNameTemplate, when), ".png");
        File.WriteAllBytes(path, EncodePng(image));
        return path;
    }

    /// <summary>A file of the capture in the temp folder, for drag and drop and "Open".</summary>
    public static string SaveTemp(BitmapSource image, DateTime when, AppSettings settings)
    {
        Directory.CreateDirectory(TempFolder);
        string path = FileNaming.UniquePath(TempFolder, FileNaming.Expand(settings.EffectiveFileNameTemplate, when), ".png");
        File.WriteAllBytes(path, EncodePng(image));
        return path;
    }

    public static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    /// <summary>Removes the files of the previous runs from the temp folder.</summary>
    public static void CleanTempFolder()
    {
        try
        {
            if (Directory.Exists(TempFolder))
                Directory.Delete(TempFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still open by the app it was dropped on: next time.
        }
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
