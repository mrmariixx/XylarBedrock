using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using XylarBedrock.Localization.Language;

namespace XylarBedrock.Pages.Skins
{
    public partial class SkinsPage : Page, INotifyPropertyChanged
    {
        private const string FavesSkinPackName = "Fave's Skins";
        private const string FavesSkinPackSerializeName = "xylarbedrock_faves_skins_v5";
        private const string FavesSkinPackFolderName = "xylarbedrock_faves_skins_v5";
        private const string OriginalSkinPackFileName = "Faves-Original-SkinPack.zip";
        private const string OriginalSkinPackResourcePath = "Resources/skinpacks/faves_original_skinpack.zip";
        private const string OriginalSkinPackSourcePath = @"C:\Users\MarioSyri\Downloads\560+ Stolen Skins (2).zip";
        private const string TutorialVideoFileName = "faves_skinpack_tutorial.mp4";
        private const string TutorialVideoResourcePath = "Resources/videos/faves_skinpack_tutorial.mp4";
        private const string SkinMasterDownloadUrl = "https://cdn.discordapp.com/attachments/1505906948384489603/1505909282720452638/SkinMaster.exe?ex=6a1d7a4f&is=6a1c28cf&hm=440a38fc94d8c1a4329ccf3171984444d71f41bd77fa759550c9ecd192b887b1&";
        private static readonly Guid FavesSkinPackHeaderUuid = new Guid("5b9c0143-55e5-46a8-9c98-d5043ddb15a5");
        private static readonly Guid FavesSkinPackModuleUuid = new Guid("a16ee2a0-a50d-4a0f-a531-77d3d17ee718");
        private static readonly string[] SkinResourceRoots =
        {
            "resources/persona/",
            "resources/skins/"
        };
        private static readonly SkinRequiredRegion[] RequiredSkinRegions =
        {
            new SkinRequiredRegion("head", 8, 8, 8, 8, 0.35),
            new SkinRequiredRegion("body", 20, 20, 8, 12, 0.35),
            new SkinRequiredRegion("right arm", 44, 20, 4, 12, 0.25),
            new SkinRequiredRegion("right leg", 4, 20, 4, 12, 0.25),
            new SkinRequiredRegion("left arm", 36, 52, 4, 12, 0.25),
            new SkinRequiredRegion("left leg", 20, 52, 4, 12, 0.25)
        };
        private const int PreviewSkinLimit = 96;
        private const int PreviewWarmupBatchSize = 8;
        private const int MaxSkinsPerMinecraftPack = 80;

        private readonly List<SkinEntry> allSkins = new List<SkinEntry>();
        private readonly DispatcherTimer tutorialProgressTimer;
        private bool hasLoadedSkins;
        private int previewWarmupToken;
        private bool isTutorialPlaying;
        private bool isTutorialSeeking;
        private bool isUpdatingTutorialSlider;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<SkinEntry> VisibleSkins { get; } = new ObservableCollection<SkinEntry>();

        public SkinsPage()
        {
            InitializeComponent();
            DataContext = this;

            tutorialProgressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            tutorialProgressTimer.Tick += TutorialProgressTimer_Tick;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (hasLoadedSkins)
            {
                return;
            }

            hasLoadedSkins = true;
            LoadSkins();
            ShowAllSkins();
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (allSkins.Count == 0)
            {
                DownloadStatusText.Text = T("SkinsPage_NoSkinsFoundShort", "No skins found.");
                return;
            }

            try
            {
                DownloadStatusText.Text = T("SkinsPage_PreparingCollection", "Preparing Fave's Skins...");

                string downloadsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string skinsDirectory = Path.Combine(downloadsDirectory, "XylarBedrock Skins");
                Directory.CreateDirectory(skinsDirectory);

                int exportedCount;
                IReadOnlyList<string> destinationPaths = CreateMinecraftSkinPacks(allSkins, skinsDirectory, out exportedCount);
                List<string> installedPaths = new List<string>();
                for (int i = 0; i < destinationPaths.Count; i++)
                {
                    string folderName = destinationPaths.Count == 1
                        ? FavesSkinPackFolderName
                        : $"{FavesSkinPackFolderName}_part{i + 1:00}";
                    installedPaths.AddRange(InstallSkinPackIntoMinecraftFolders(destinationPaths[i], folderName));
                }

                if (destinationPaths.Count == 1)
                {
                    OpenDownloadedSkinPack(destinationPaths[0]);
                }

                DownloadStatusText.Text = installedPaths.Count == 0
                    ? string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        T("SkinsPage_CollectionDownloadedStatus", "Downloaded Fave's Skins with {0} skins."),
                        exportedCount)
                    : $"Installed Fave's Skins with {exportedCount} skins across {destinationPaths.Count} packs. Restart Minecraft if it was already open.";
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Skin download failed: {ex}");
                DownloadStatusText.Text = T("SkinsPage_DownloadFailed", "Download failed. Try again.");
            }
            finally
            {
            }
        }

        private void OriginalSkinPackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string downloadsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloadsDirectory);

                string destinationPath = GetUniqueDownloadPath(Path.Combine(downloadsDirectory, OriginalSkinPackFileName));
                StreamResourceInfo resourceInfo = Application.GetResourceStream(new Uri(OriginalSkinPackResourcePath, UriKind.Relative)) ??
                                                  Application.GetResourceStream(new Uri(OriginalSkinPackResourcePath.ToLowerInvariant(), UriKind.Relative));

                if (resourceInfo?.Stream != null)
                {
                    using (resourceInfo.Stream)
                    using (FileStream output = File.Create(destinationPath))
                    {
                        resourceInfo.Stream.CopyTo(output);
                    }
                }
                else if (File.Exists(OriginalSkinPackSourcePath))
                {
                    File.Copy(OriginalSkinPackSourcePath, destinationPath, true);
                }
                else
                {
                    throw new FileNotFoundException("Original skin pack zip was not found.", OriginalSkinPackSourcePath);
                }

                DownloadStatusText.Text = $"Original skin pack saved to {destinationPath}";
                ShowFileInExplorer(destinationPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Original skin pack export failed: {ex}");
                DownloadStatusText.Text = "Could not export the original skin pack.";
            }
        }

        private async void TutorialButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TutorialOverlay.Visibility = Visibility.Visible;
                isTutorialPlaying = true;
                TutorialPlayPauseButton.Content = "PAUSE";
                ResetTutorialProgress();

                await TutorialPlayer.EnsureCoreWebView2Async();
                TutorialPlayer.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                TutorialPlayer.CoreWebView2.Settings.AreDevToolsEnabled = false;
                TutorialPlayer.CoreWebView2.Navigate(new Uri(EnsureTutorialPlayerHtmlFile()).AbsoluteUri);
                tutorialProgressTimer.Start();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not open tutorial video: {ex}");
                DownloadStatusText.Text = "Could not load the tutorial video inside XylarBedrock.";
                TutorialOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseTutorialButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                tutorialProgressTimer.Stop();
                isTutorialPlaying = false;
                TutorialPlayPauseButton.Content = "PLAY";
                _ = ExecuteTutorialScriptAsync("window.xylarPause && window.xylarPause();");
                if (TutorialPlayer.CoreWebView2 != null)
                {
                    TutorialPlayer.CoreWebView2.NavigateToString("<!doctype html><html><body style='background:#000'></body></html>");
                }
                ResetTutorialProgress();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not stop tutorial video cleanly: {ex}");
            }

            TutorialOverlay.Visibility = Visibility.Collapsed;
        }

        private async void TutorialPlayer_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                DownloadStatusText.Text = "Could not load the local tutorial player.";
                return;
            }

            await ExecuteTutorialScriptAsync("window.xylarPlay && window.xylarPlay();");
            isTutorialPlaying = true;
            TutorialPlayPauseButton.Content = "PAUSE";
            tutorialProgressTimer.Start();
            await UpdateTutorialProgressAsync();
        }

        private async void TutorialBackButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteTutorialScriptAsync("window.xylarSeekBy && window.xylarSeekBy(-10);");
            await UpdateTutorialProgressAsync();
        }

        private async void TutorialForwardButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteTutorialScriptAsync("window.xylarSeekBy && window.xylarSeekBy(10);");
            await UpdateTutorialProgressAsync();
        }

        private async void TutorialPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            string result = await ExecuteTutorialScriptAsync("window.xylarToggle ? window.xylarToggle() : false;");
            isTutorialPlaying = result?.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0;
            TutorialPlayPauseButton.Content = isTutorialPlaying ? "PAUSE" : "PLAY";
            if (isTutorialPlaying)
            {
                tutorialProgressTimer.Start();
            }

            await UpdateTutorialProgressAsync();
        }

        private void TutorialSeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            isTutorialSeeking = true;
        }

        private async void TutorialSeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            await SeekTutorialToSliderAsync();
            isTutorialSeeking = false;
        }

        private async void TutorialSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!isUpdatingTutorialSlider && isTutorialSeeking)
            {
                await SeekTutorialToSliderAsync();
            }
        }

        private async void TutorialProgressTimer_Tick(object sender, EventArgs e)
        {
            await UpdateTutorialProgressAsync();
        }

        private async Task SeekTutorialToSliderAsync()
        {
            if (TutorialPlayer.CoreWebView2 == null)
            {
                return;
            }

            string seconds = TutorialSeekSlider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await ExecuteTutorialScriptAsync($"window.xylarSeekTo && window.xylarSeekTo({seconds});");
            await UpdateTutorialProgressAsync();
        }

        private async Task UpdateTutorialProgressAsync()
        {
            if (TutorialPlayer.CoreWebView2 == null)
            {
                ResetTutorialProgress();
                return;
            }

            string result = await ExecuteTutorialScriptAsync("window.xylarState ? window.xylarState() : null;");
            if (string.IsNullOrWhiteSpace(result) || result == "null")
            {
                ResetTutorialProgress();
                return;
            }

            Newtonsoft.Json.Linq.JObject state;
            try
            {
                state = Newtonsoft.Json.Linq.JObject.Parse(result);
            }
            catch
            {
                return;
            }

            double durationSeconds = state.Value<double?>("duration") ?? 0;
            double positionSeconds = state.Value<double?>("position") ?? 0;
            bool paused = state.Value<bool?>("paused") ?? true;

            if (durationSeconds <= 0)
            {
                return;
            }

            isTutorialPlaying = !paused;
            TutorialPlayPauseButton.Content = isTutorialPlaying ? "PAUSE" : "PLAY";

            isUpdatingTutorialSlider = true;
            TutorialSeekSlider.Maximum = Math.Max(1, durationSeconds);
            if (!isTutorialSeeking)
            {
                TutorialSeekSlider.Value = Math.Min(TutorialSeekSlider.Maximum, Math.Max(0, positionSeconds));
            }
            isUpdatingTutorialSlider = false;

            TutorialTimeText.Text = $"{FormatTutorialTime(TimeSpan.FromSeconds(positionSeconds))} / {FormatTutorialTime(TimeSpan.FromSeconds(durationSeconds))}";
        }

        private void ResetTutorialProgress()
        {
            isUpdatingTutorialSlider = true;
            TutorialSeekSlider.Maximum = 1;
            TutorialSeekSlider.Value = 0;
            isUpdatingTutorialSlider = false;
            TutorialTimeText.Text = "00:00 / 00:00";
        }

        private async Task<string> ExecuteTutorialScriptAsync(string script)
        {
            try
            {
                return TutorialPlayer.CoreWebView2 == null
                    ? null
                    : await TutorialPlayer.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Tutorial script failed: {ex}");
                return null;
            }
        }

        private static string FormatTutorialTime(TimeSpan time)
        {
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours:0}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes:00}:{time.Seconds:00}";
        }

        private void SkinMasterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SkinMasterDownloadUrl,
                    UseShellExecute = true
                });
                DownloadStatusText.Text = "SkinMaster download link opened. XylarBedrock will not run external EXE files automatically.";
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not open SkinMaster link: {ex}");
                DownloadStatusText.Text = "Could not open the SkinMaster download link.";
            }
        }

        private void LoadSkins()
        {
            allSkins.Clear();

            foreach (string resourcePath in GetEmbeddedSkinResourcePaths())
            {
                string fileName = GetResourceFileName(resourcePath);
                if (ShouldSkipSkinFile(fileName) || allSkins.Any(skin => string.Equals(skin.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                SkinEntry skin = SkinEntry.FromResource(resourcePath);
                if (IsCompleteMinecraftSkin(skin))
                {
                    allSkins.Add(skin);
                }
            }

            foreach (string directory in GetSkinDirectories())
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                IEnumerable<string> skinFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(path => !ShouldSkipSkinFile(path))
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

                foreach (string skinFile in skinFiles)
                {
                    string resolvedSkinFile = Path.GetFullPath(skinFile);
                    string fileName = Path.GetFileName(resolvedSkinFile);
                    if (allSkins.Any(skin => string.Equals(skin.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    SkinEntry skin = SkinEntry.FromFile(resolvedSkinFile);
                    if (IsCompleteMinecraftSkin(skin))
                    {
                        allSkins.Add(skin);
                    }
                }
            }
        }

        private void ShowAllSkins()
        {
            VisibleSkins.Clear();

            if (allSkins.Count == 0)
            {
                EmptySkinsText.Visibility = Visibility.Visible;
                SkinCounterText.Text = string.Empty;
                DownloadStatusText.Text = T("SkinsPage_NoSkinsFoundShort", "No skins found.");
                return;
            }

            EmptySkinsText.Visibility = Visibility.Collapsed;
            previewWarmupToken++;

            List<SkinEntry> previewSkins = allSkins.Take(PreviewSkinLimit).ToList();
            foreach (SkinEntry skin in previewSkins)
            {
                VisibleSkins.Add(skin);
            }

            SkinCounterText.Text = allSkins.Count <= PreviewSkinLimit
                ? string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    T("SkinsPage_CollectionCountFormat", "{0} skins included in one pack."),
                    allSkins.Count)
                : $"{allSkins.Count} skins included in one pack. Showing {previewSkins.Count} previews for speed.";
            DownloadStatusText.Text = T("SkinsPage_AllSkinsHelp", "Click DOWNLOADS to save the original skin pack zip.");

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleSkins)));
            BeginPreviewWarmup(previewSkins, previewWarmupToken);
        }

        private void BeginPreviewWarmup(IReadOnlyList<SkinEntry> skins, int token, int startIndex = 0)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token != previewWarmupToken || startIndex >= skins.Count)
                {
                    return;
                }

                int endIndex = Math.Min(startIndex + PreviewWarmupBatchSize, skins.Count);
                for (int i = startIndex; i < endIndex; i++)
                {
                    skins[i].EnsurePreview();
                }

                if (endIndex < skins.Count)
                {
                    BeginPreviewWarmup(skins, token, endIndex);
                }
            }), DispatcherPriority.Background);
        }

        private static string T(string key, string fallback)
        {
            return LanguageManager.GetResource(key) as string ?? fallback;
        }

        private static string GetUniqueDownloadPath(string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                return destinationPath;
            }

            string directory = Path.GetDirectoryName(destinationPath);
            string fileName = Path.GetFileNameWithoutExtension(destinationPath);
            string extension = Path.GetExtension(destinationPath);

            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, $"{fileName} ({i}){extension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return destinationPath;
        }

        private static void ShowFileInExplorer(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not select exported file '{filePath}': {ex}");
            }
        }

        private static string EnsureTutorialVideoFile()
        {
            string cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XylarBedrock",
                "videos");
            Directory.CreateDirectory(cacheDirectory);

            string destinationPath = Path.Combine(cacheDirectory, TutorialVideoFileName);
            StreamResourceInfo resourceInfo = Application.GetResourceStream(new Uri(TutorialVideoResourcePath, UriKind.Relative)) ??
                                              Application.GetResourceStream(new Uri(TutorialVideoResourcePath.ToLowerInvariant(), UriKind.Relative));
            if (resourceInfo?.Stream == null)
            {
                throw new FileNotFoundException("Embedded tutorial video was not found.", TutorialVideoResourcePath);
            }

            using (resourceInfo.Stream)
            {
                long embeddedLength = resourceInfo.Stream.CanSeek ? resourceInfo.Stream.Length : -1;
                if (File.Exists(destinationPath))
                {
                    long cachedLength = new FileInfo(destinationPath).Length;
                    if (cachedLength > 0 && (embeddedLength < 0 || cachedLength == embeddedLength))
                    {
                        return destinationPath;
                    }
                }

                using FileStream output = File.Create(destinationPath);
                resourceInfo.Stream.CopyTo(output);
            }

            return destinationPath;
        }

        private static string EnsureTutorialPlayerHtmlFile()
        {
            string videoPath = EnsureTutorialVideoFile();
            string playerDirectory = Path.GetDirectoryName(videoPath);
            string htmlPath = Path.Combine(playerDirectory, "faves_skinpack_tutorial_player.html");
            File.WriteAllText(htmlPath, BuildTutorialPlayerHtml(TutorialVideoFileName), Encoding.UTF8);
            return htmlPath;
        }

        private static string BuildTutorialPlayerHtml(string videoFileName)
        {
            string safeVideoFileName = System.Net.WebUtility.HtmlEncode(videoFileName);
            return @"<!doctype html>
<html>
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<style>
html,body{margin:0;width:100%;height:100%;background:#000;overflow:hidden;font-family:Arial,sans-serif;}
#wrap{position:fixed;inset:0;background:#000;display:flex;align-items:center;justify-content:center;}
video{width:100%;height:100%;background:#000;object-fit:contain;}
#hint{position:fixed;left:14px;bottom:14px;color:#fff;background:rgba(0,0,0,.55);border:1px solid rgba(255,255,255,.18);border-radius:6px;padding:7px 10px;font-size:12px;opacity:.9;transition:opacity .25s;}
body.playing #hint{opacity:0;}
</style>
</head>
<body>
<div id=""wrap"">
<video id=""video"" src=""" + safeVideoFileName + @""" autoplay controls playsinline preload=""auto""></video>
</div>
<div id=""hint"">If it does not start, press PLAY.</div>
<script>
const video = document.getElementById('video');
function mark(){ document.body.classList.toggle('playing', !video.paused); }
function forcePlay(){
  try {
    video.muted = false;
    const attempt = video.play();
    if (attempt && attempt.catch) {
      attempt.catch(() => {
        video.muted = true;
        video.play().catch(() => {});
      });
    }
  } catch (e) {
    try {
      video.muted = true;
      video.play().catch(() => {});
    } catch (_) {}
  }
  mark();
  return !video.paused;
}
window.xylarPlay = forcePlay;
window.xylarPause = function(){ video.pause(); mark(); return false; };
window.xylarToggle = function(){
  if (video.paused || video.ended) {
    if (video.ended) video.currentTime = 0;
    forcePlay();
    return true;
  }
  video.pause();
  mark();
  return false;
};
window.xylarSeekBy = function(seconds){
  if (!Number.isFinite(video.duration)) return video.currentTime || 0;
  video.currentTime = Math.max(0, Math.min(video.duration, (video.currentTime || 0) + seconds));
  return video.currentTime;
};
window.xylarSeekTo = function(seconds){
  if (!Number.isFinite(video.duration)) return video.currentTime || 0;
  video.currentTime = Math.max(0, Math.min(video.duration, seconds));
  return video.currentTime;
};
window.xylarState = function(){
  return {
    position: video.currentTime || 0,
    duration: Number.isFinite(video.duration) ? video.duration : 0,
    paused: video.paused
  };
};
video.addEventListener('loadedmetadata', forcePlay);
video.addEventListener('canplay', forcePlay, { once:true });
video.addEventListener('play', mark);
video.addEventListener('pause', mark);
video.addEventListener('ended', mark);
setTimeout(forcePlay, 150);
setTimeout(forcePlay, 650);
</script>
</body>
</html>";
        }

        private static IReadOnlyList<string> CreateMinecraftSkinPacks(IEnumerable<SkinEntry> skins, string skinsDirectory, out int exportedCount)
        {
            List<SkinEntry> skinList = skins.ToList();
            int totalPacks = Math.Max(1, (int)Math.Ceiling(skinList.Count / (double)MaxSkinsPerMinecraftPack));
            List<string> packPaths = new List<string>();
            exportedCount = 0;

            for (int packIndex = 0; packIndex < totalPacks; packIndex++)
            {
                List<SkinEntry> packSkins = skinList
                    .Skip(packIndex * MaxSkinsPerMinecraftPack)
                    .Take(MaxSkinsPerMinecraftPack)
                    .ToList();
                if (packSkins.Count == 0)
                {
                    continue;
                }

                bool isSplitPack = totalPacks > 1;
                string partSuffix = $"part{packIndex + 1:00}";
                string packName = isSplitPack
                    ? $"{FavesSkinPackName} {packIndex + 1}/{totalPacks}"
                    : FavesSkinPackName;
                string serializeName = isSplitPack
                    ? $"{FavesSkinPackSerializeName}_{partSuffix}"
                    : FavesSkinPackSerializeName;
                string packFileName = isSplitPack
                    ? $"Faves-Skins-V5-Part{packIndex + 1:00}.mcpack"
                    : "Faves-Skins.mcpack";
                string destinationPath = GetUniqueDownloadPath(Path.Combine(skinsDirectory, packFileName));

                exportedCount += CreateMinecraftSkinPack(
                    packSkins,
                    destinationPath,
                    packName,
                    serializeName,
                    isSplitPack ? Guid.NewGuid() : FavesSkinPackHeaderUuid,
                    isSplitPack ? Guid.NewGuid() : FavesSkinPackModuleUuid);
                packPaths.Add(destinationPath);
            }

            return packPaths;
        }

        private static int CreateMinecraftSkinPack(
            IEnumerable<SkinEntry> skins,
            string destinationPath,
            string packName,
            string serializeName,
            Guid headerUuid,
            Guid moduleUuid)
        {
            List<SkinPackEntry> packEntries = new List<SkinPackEntry>();

            foreach (SkinEntry skin in skins)
            {
                try
                {
                    using (Stream skinStream = skin.OpenRead())
                    {
                        string displayName = Path.GetFileNameWithoutExtension(skin.FileName);
                        string slug = Slugify(displayName);
                        int index = packEntries.Count + 1;
                        SkinPackEntry packEntry = new SkinPackEntry
                        {
                            DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Skin {index}" : displayName,
                            LocalizationName = $"skin_{index:000}_{slug}",
                            TexturePath = $"skin_{index:000}_{slug}.png",
                            PngBytes = BuildMinecraftReadySkinPng(skinStream)
                        };

                        Stream capeStream = TryOpenCapeStreamForSkin(skin);
                        if (capeStream != null)
                        {
                            using (capeStream)
                            {
                                try
                                {
                                    packEntry.CapePath = $"cape_{index:000}_{slug}.png";
                                    packEntry.CapePngBytes = BuildMinecraftReadyCapePng(capeStream);
                                }
                                catch (Exception capeEx)
                                {
                                    Debug.WriteLine($"Skipping cape for {skin.FileName}: {capeEx.Message}");
                                    packEntry.CapePath = null;
                                    packEntry.CapePngBytes = null;
                                }
                            }
                        }

                        packEntries.Add(packEntry);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Skipping invalid skin '{skin.FileName}': {ex}");
                }
            }

            if (packEntries.Count == 0)
            {
                throw new InvalidDataException("No valid Minecraft skins were found for the pack.");
            }

            using (ZipArchive archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
            {
                WriteTextEntry(archive, "manifest.json", BuildSkinPackManifest(headerUuid, moduleUuid));
                WriteTextEntry(archive, "skins.json", BuildSkinPackJson(serializeName, packEntries));
                WriteTextEntry(archive, "texts/languages.json", BuildSkinPackLanguagesJson());
                WriteTextEntry(archive, "texts/en_US.lang", BuildSkinPackLang(serializeName, packName, packEntries));

                foreach (SkinPackEntry entry in packEntries)
                {
                    WriteBytesEntry(archive, entry.TexturePath, entry.PngBytes);
                    if (!string.IsNullOrWhiteSpace(entry.CapePath) && entry.CapePngBytes != null)
                    {
                        WriteBytesEntry(archive, entry.CapePath, entry.CapePngBytes);
                    }
                }

                WriteBytesEntry(archive, "pack_icon.png", packEntries[0].PngBytes);
            }

            return packEntries.Count;
        }

        private static IReadOnlyList<string> InstallSkinPackIntoMinecraftFolders(string skinPackPath, string folderName = FavesSkinPackFolderName)
        {
            List<string> installedPaths = new List<string>();

            foreach (string skinPackDirectory in GetMinecraftSkinPackDirectories())
            {
                try
                {
                    Directory.CreateDirectory(skinPackDirectory);
                    string targetDirectory = Path.Combine(skinPackDirectory, folderName);

                    if (Directory.Exists(targetDirectory))
                    {
                        Directory.Delete(targetDirectory, true);
                    }

                    Directory.CreateDirectory(targetDirectory);
                    ZipFile.ExtractToDirectory(skinPackPath, targetDirectory, true);
                    installedPaths.Add(targetDirectory);
                    Trace.WriteLine($"Installed Fave's Skins directly to '{targetDirectory}'.");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Could not install Fave's Skins into '{skinPackDirectory}': {ex}");
                }
            }

            return installedPaths;
        }

        private static IEnumerable<string> GetMinecraftSkinPackDirectories()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string packagesDirectory = Path.Combine(localAppData, "Packages");
            HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (Directory.Exists(packagesDirectory))
                {
                    foreach (string packageDirectory in Directory.EnumerateDirectories(packagesDirectory, "*Minecraft*", SearchOption.TopDirectoryOnly))
                    {
                        string packageName = Path.GetFileName(packageDirectory);
                        if (packageName.EndsWith("MinecraftUWP_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase) ||
                            packageName.EndsWith("MinecraftWindowsBeta_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase))
                        {
                            AddMojangSkinPackDirectories(directories, Path.Combine(packageDirectory, "LocalState", "games", "com.mojang"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not inspect Minecraft package folders for skin packs: {ex}");
            }

            string defaultMojangDirectory = Path.Combine(
                localAppData,
                "Packages",
                "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                "LocalState",
                "games",
                "com.mojang");
            AddMojangSkinPackDirectories(directories, defaultMojangDirectory);

            return directories;
        }

        private static void AddMojangSkinPackDirectories(ISet<string> directories, string mojangDirectory)
        {
            AddSkinPackDirectoryPair(directories, mojangDirectory);

            try
            {
                if (!Directory.Exists(mojangDirectory))
                {
                    return;
                }

                DirectoryInfo mojangDirectoryInfo = new DirectoryInfo(mojangDirectory);
                if ((mojangDirectoryInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    return;
                }

                FileSystemInfo resolvedTarget = mojangDirectoryInfo.ResolveLinkTarget(true);
                if (!string.IsNullOrWhiteSpace(resolvedTarget?.FullName))
                {
                    AddSkinPackDirectoryPair(directories, resolvedTarget.FullName);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not resolve Minecraft com.mojang link '{mojangDirectory}': {ex}");
            }
        }

        private static void AddSkinPackDirectoryPair(ISet<string> directories, string mojangDirectory)
        {
            if (string.IsNullOrWhiteSpace(mojangDirectory))
            {
                return;
            }

            directories.Add(Path.Combine(mojangDirectory, "skin_packs"));
            directories.Add(Path.Combine(mojangDirectory, "development_skin_packs"));
        }

        private static void OpenDownloadedSkinPack(string destinationPath)
        {
            if (!destinationPath.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = destinationPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not auto-open skin pack '{destinationPath}': {ex}");
            }
        }

        private static string BuildSkinPackManifest(Guid headerUuid, Guid moduleUuid)
        {
            return "{\n" +
                   "  \"format_version\": 1,\n" +
                   "  \"header\": {\n" +
                   "    \"name\": \"pack.name\",\n" +
                   $"    \"uuid\": \"{headerUuid}\",\n" +
                   "    \"version\": [1, 0, 0]\n" +
                   "  },\n" +
                   "  \"modules\": [\n" +
                   "    {\n" +
                   "      \"type\": \"skin_pack\",\n" +
                   $"      \"uuid\": \"{moduleUuid}\",\n" +
                   "      \"version\": [1, 0, 0]\n" +
                   "    }\n" +
                   "  ]\n" +
                   "}\n";
        }

        private static string BuildSkinPackJson(string serializeName, IReadOnlyList<SkinPackEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine($"  \"serialize_name\": \"{JsonEscape(serializeName)}\",");
            builder.AppendLine($"  \"localization_name\": \"{JsonEscape(serializeName)}\",");
            builder.AppendLine("  \"skins\": [");

            for (int i = 0; i < entries.Count; i++)
            {
                SkinPackEntry entry = entries[i];
                builder.AppendLine("    {");
                builder.AppendLine($"      \"localization_name\": \"{JsonEscape(entry.LocalizationName)}\",");
                builder.AppendLine("      \"geometry\": \"geometry.humanoid.customSlim\",");
                builder.AppendLine($"      \"texture\": \"{JsonEscape(entry.TexturePath)}\",");
                if (!string.IsNullOrWhiteSpace(entry.CapePath))
                {
                    builder.AppendLine($"      \"cape\": \"{JsonEscape(entry.CapePath)}\",");
                }

                builder.AppendLine("      \"type\": \"free\"");
                builder.Append("    }");
                builder.AppendLine(i == entries.Count - 1 ? string.Empty : ",");
            }

            builder.AppendLine("  ]");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string BuildSkinPackLanguagesJson()
        {
            return "[\n  \"en_US\"\n]\n";
        }

        private static string BuildSkinPackLang(string serializeName, string packName, IReadOnlyList<SkinPackEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"pack.name={packName}");
            builder.AppendLine("pack.description=Skins by xFaveXEditz, packed by XylarBedrock");
            builder.AppendLine($"skinpack.{serializeName}={packName}");

            foreach (SkinPackEntry entry in entries)
            {
                builder.AppendLine($"skin.{serializeName}.{entry.LocalizationName}={entry.DisplayName}");
            }

            return builder.ToString();
        }

        private static byte[] BuildMinecraftReadySkinPng(Stream sourceStream)
        {
            byte[] sourceBytes = ReadAllBytes(sourceStream);
            BitmapSource source = DecodeBitmap(sourceBytes);
            ValidateMinecraftSkin(source);

            return IsPng(sourceBytes) ? sourceBytes : EncodePng(source);
        }

        private static bool IsCompleteMinecraftSkin(SkinEntry skin)
        {
            try
            {
                using (Stream stream = skin.OpenRead())
                {
                    ValidateMinecraftSkin(DecodeBitmap(ReadAllBytes(stream)));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Skipping incomplete skin '{skin.FileName}': {ex.Message}");
                return false;
            }
        }

        private static void ValidateMinecraftSkin(BitmapSource source)
        {
            int sourceWidth = source.PixelWidth;
            int sourceHeight = source.PixelHeight;
            bool isSquareSkin = sourceWidth == sourceHeight && sourceWidth % 64 == 0;
            bool isClassicSkin = sourceWidth == sourceHeight * 2 && sourceWidth % 64 == 0;

            if (!isSquareSkin && !isClassicSkin)
            {
                throw new InvalidDataException($"Unsupported Minecraft skin size: {sourceWidth}x{sourceHeight}.");
            }

            if (!HasRequiredSkinParts(source))
            {
                throw new InvalidDataException("Skin is missing one or more required body parts.");
            }
        }

        private static bool HasRequiredSkinParts(BitmapSource source)
        {
            FormatConvertedBitmap converted = new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                null,
                0);
            converted.Freeze();

            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            double scale = converted.PixelWidth / 64.0;

            foreach (SkinRequiredRegion region in RequiredSkinRegions)
            {
                if (GetOpaqueCoverage(pixels, stride, scale, region) < region.MinimumCoverage)
                {
                    return false;
                }
            }

            return true;
        }

        private static double GetOpaqueCoverage(byte[] pixels, int stride, double scale, SkinRequiredRegion region)
        {
            int x = (int)Math.Round(region.X * scale);
            int y = (int)Math.Round(region.Y * scale);
            int width = Math.Max(1, (int)Math.Round(region.Width * scale));
            int height = Math.Max(1, (int)Math.Round(region.Height * scale));
            int opaquePixels = 0;
            int totalPixels = width * height;

            for (int row = y; row < y + height; row++)
            {
                for (int column = x; column < x + width; column++)
                {
                    int alphaIndex = row * stride + column * 4 + 3;
                    if (alphaIndex >= 0 && alphaIndex < pixels.Length && pixels[alphaIndex] > 8)
                    {
                        opaquePixels++;
                    }
                }
            }

            return totalPixels == 0 ? 0 : opaquePixels / (double)totalPixels;
        }

        private static byte[] BuildMinecraftReadyCapePng(Stream sourceStream)
        {
            byte[] sourceBytes = ReadAllBytes(sourceStream);
            BitmapSource source = DecodeBitmap(sourceBytes);

            if (source.PixelWidth == 64 && source.PixelHeight == 32)
            {
                return IsPng(sourceBytes) ? sourceBytes : EncodePng(source);
            }

            if (source.PixelWidth == 64 && source.PixelHeight > 32)
            {
                CroppedBitmap cropped = new CroppedBitmap(source, new Int32Rect(0, 0, 64, 32));
                cropped.Freeze();
                return EncodePng(cropped);
            }

            throw new InvalidDataException($"Unsupported Minecraft cape size: {source.PixelWidth}x{source.PixelHeight}.");
        }

        private static BitmapSource DecodeBitmap(byte[] sourceBytes)
        {
            using (MemoryStream memoryStream = new MemoryStream(sourceBytes))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    memoryStream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                BitmapSource source = decoder.Frames[0];
                source.Freeze();
                return source;
            }
        }

        private static byte[] EncodePng(BitmapSource source)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (MemoryStream outputStream = new MemoryStream())
            {
                encoder.Save(outputStream);
                return outputStream.ToArray();
            }
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        private static bool IsPng(byte[] bytes)
        {
            return bytes != null &&
                   bytes.Length >= 8 &&
                   bytes[0] == 0x89 &&
                   bytes[1] == 0x50 &&
                   bytes[2] == 0x4E &&
                   bytes[3] == 0x47 &&
                   bytes[4] == 0x0D &&
                   bytes[5] == 0x0A &&
                   bytes[6] == 0x1A &&
                   bytes[7] == 0x0A;
        }

        private static Stream TryOpenCapeStreamForSkin(SkinEntry skin)
        {
            foreach (string capeFileName in GetCapeCandidateFileNames(skin.FileName))
            {
                if (!string.IsNullOrWhiteSpace(skin.FilePath))
                {
                    string capeFilePath = Path.Combine(Path.GetDirectoryName(skin.FilePath), capeFileName);
                    if (File.Exists(capeFilePath))
                    {
                        return File.OpenRead(capeFilePath);
                    }
                }

                if (!string.IsNullOrWhiteSpace(skin.ResourcePath))
                {
                    string capeResourcePath = BuildManifestSkinResourcePath(capeFileName, GetSkinResourceRoot(skin.ResourcePath));
                    StreamResourceInfo resourceInfo = Application.GetResourceStream(new Uri(capeResourcePath, UriKind.Relative));
                    if (resourceInfo?.Stream != null)
                    {
                        return resourceInfo.Stream;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> GetCapeCandidateFileNames(string skinFileName)
        {
            HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string fileName = Path.GetFileName(skinFileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return candidates;
            }

            candidates.Add(Regex.Replace(fileName, "skin", "Cape", RegexOptions.IgnoreCase));
            candidates.Add(Regex.Replace(fileName, "-skin", "-Cape", RegexOptions.IgnoreCase));
            return candidates.Where(candidate =>
                !string.Equals(candidate, fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static byte[] ResizeNearestNeighbor(byte[] sourcePixels, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            byte[] targetPixels = new byte[targetWidth * targetHeight * 4];

            for (int y = 0; y < targetHeight; y++)
            {
                int sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / targetHeight);
                for (int x = 0; x < targetWidth; x++)
                {
                    int sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / targetWidth);
                    int sourceIndex = (sourceY * sourceWidth + sourceX) * 4;
                    int targetIndex = (y * targetWidth + x) * 4;

                    targetPixels[targetIndex] = sourcePixels[sourceIndex];
                    targetPixels[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                    targetPixels[targetIndex + 2] = sourcePixels[sourceIndex + 2];
                    targetPixels[targetIndex + 3] = sourcePixels[sourceIndex + 3];
                }
            }

            return targetPixels;
        }

        private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
        {
            WriteBytesEntry(archive, entryName, Encoding.UTF8.GetBytes(content));
        }

        private static void WriteBytesEntry(ZipArchive archive, string entryName, byte[] content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream entryStream = entry.Open())
            {
                entryStream.Write(content, 0, content.Length);
            }
        }

        private static string Slugify(string value)
        {
            string slug = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(slug) ? "skin" : slug;
        }

        private static string JsonEscape(string value)
        {
            return value?
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"") ?? string.Empty;
        }

        private static IEnumerable<string> GetSkinDirectories()
        {
            HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string baseDirectory in GetBaseDirectories())
            {
                AddSkinDirectoryCandidates(directories, baseDirectory);
            }

            foreach (string directory in directories)
            {
                yield return directory;
            }
        }

        private static IEnumerable<string> GetBaseDirectories()
        {
            yield return AppContext.BaseDirectory;
            yield return Environment.CurrentDirectory;

            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                yield return Path.GetDirectoryName(Environment.ProcessPath);
            }

            string entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                yield return Path.GetDirectoryName(entryAssemblyPath);
            }

            string mainModulePath = null;
            try
            {
                mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch
            {
                // Some sandboxed environments do not expose MainModule. Other paths above still cover normal runs.
            }

            if (!string.IsNullOrWhiteSpace(mainModulePath))
            {
                yield return Path.GetDirectoryName(mainModulePath);
            }
        }

        private static void AddSkinDirectoryCandidates(ISet<string> directories, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return;
            }

            TryAddSkinDirectory(directories, Path.Combine(baseDirectory, "Resources", "persona"));
            TryAddSkinDirectory(directories, Path.Combine(baseDirectory, "Resources", "skins"));

            try
            {
                DirectoryInfo current = new DirectoryInfo(baseDirectory);
                for (int i = 0; i < 8 && current != null; i++)
                {
                    TryAddSkinDirectory(directories, Path.Combine(current.FullName, "Resources", "persona"));
                    TryAddSkinDirectory(directories, Path.Combine(current.FullName, "Resources", "skins"));
                    current = current.Parent;
                }
            }
            catch
            {
                // Invalid base paths are ignored so a single bad directory cannot break the Skins page.
            }
        }

        private static void TryAddSkinDirectory(ISet<string> directories, string path)
        {
            try
            {
                directories.Add(Path.GetFullPath(path));
            }
            catch
            {
                // Keep the loader resilient when Windows reports unusual app/extraction paths.
            }
        }

        private static bool IsSupportedSkinImage(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipSkinFile(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            return !IsSupportedSkinImage(path) ||
                   Path.GetFileName(path).IndexOf("cape", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<string> GetEmbeddedSkinResourcePaths()
        {
            List<string> resourcePaths = new List<string>();

            foreach (string resourcePath in GetSkinManifestResourcePaths())
            {
                AddEmbeddedSkinResourcePath(resourcePaths, resourcePath);
            }

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resourceName = $"{assembly.GetName().Name}.g.resources";
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return resourcePaths;
                    }

                    using (ResourceReader reader = new ResourceReader(stream))
                    {
                        foreach (DictionaryEntry entry in reader)
                        {
                            string resourcePath = entry.Key as string;
                            if (string.IsNullOrWhiteSpace(resourcePath) ||
                                !IsSkinResourcePath(resourcePath) ||
                                ShouldSkipSkinFile(resourcePath))
                            {
                                continue;
                            }

                            AddEmbeddedSkinResourcePath(resourcePaths, resourcePath);
                        }
                    }
                }
            }
            catch
            {
                // If WPF resource enumeration fails, the file-system fallback below still works in development builds.
            }

            return resourcePaths.OrderBy(GetResourceFileName, StringComparer.OrdinalIgnoreCase);
        }

        private static void AddEmbeddedSkinResourcePath(IList<string> resourcePaths, string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath) || ShouldSkipSkinFile(resourcePath))
            {
                return;
            }

            string fileName = GetResourceFileName(resourcePath);
            if (resourcePaths.Any(existingPath =>
                    string.Equals(GetResourceFileName(existingPath), fileName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            resourcePaths.Add(resourcePath);
        }

        private static IEnumerable<string> GetSkinManifestResourcePaths()
        {
            List<string> resourcePaths = new List<string>();

            foreach (string resourceRoot in SkinResourceRoots)
            {
                try
                {
                    StreamResourceInfo manifestInfo = Application.GetResourceStream(new Uri($"{resourceRoot}skins.json", UriKind.Relative)) ??
                                                      Application.GetResourceStream(new Uri($"{ToResourceDisplayRoot(resourceRoot)}skins.json", UriKind.Relative));
                    if (manifestInfo?.Stream == null)
                    {
                        continue;
                    }

                    using (StreamReader reader = new StreamReader(manifestInfo.Stream, Encoding.UTF8))
                    {
                        string manifestJson = reader.ReadToEnd();
                        foreach (Match match in Regex.Matches(manifestJson, "\"texture\"\\s*:\\s*\"(?<texture>[^\"]+)\""))
                        {
                            string resourcePath = BuildManifestSkinResourcePath(match.Groups["texture"].Value, resourceRoot);

                            if (!ShouldSkipSkinFile(resourcePath) &&
                                !resourcePaths.Contains(resourcePath, StringComparer.OrdinalIgnoreCase))
                            {
                                resourcePaths.Add(resourcePath);
                            }
                        }
                    }
                }
                catch
                {
                    // The resource table fallback still covers builds without a skin manifest.
                }
            }

            return resourcePaths;
        }

        private static string BuildManifestSkinResourcePath(string texturePath)
        {
            return BuildManifestSkinResourcePath(texturePath, SkinResourceRoots[0]);
        }

        private static string BuildManifestSkinResourcePath(string texturePath, string resourceRoot)
        {
            string normalizedTexturePath = (texturePath ?? string.Empty)
                .Replace("\\/", "/", StringComparison.Ordinal)
                .Replace('\\', '/')
                .TrimStart('/');

            string escapedTexturePath = string.Join(
                "/",
                normalizedTexturePath
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(segment => Uri.EscapeDataString(segment)));

            return $"{NormalizeResourceRoot(resourceRoot)}{escapedTexturePath}".ToLowerInvariant();
        }

        private static bool IsSkinResourcePath(string resourcePath)
        {
            return SkinResourceRoots.Any(root =>
                resourcePath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetSkinResourceRoot(string resourcePath)
        {
            return SkinResourceRoots.FirstOrDefault(root =>
                       resourcePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) ??
                   SkinResourceRoots[0];
        }

        private static string NormalizeResourceRoot(string resourceRoot)
        {
            string normalizedRoot = string.IsNullOrWhiteSpace(resourceRoot)
                ? SkinResourceRoots[0]
                : resourceRoot.Replace('\\', '/').TrimStart('/');

            return normalizedRoot.EndsWith("/", StringComparison.Ordinal)
                ? normalizedRoot
                : normalizedRoot + "/";
        }

        private static string ToResourceDisplayRoot(string resourceRoot)
        {
            string normalizedRoot = NormalizeResourceRoot(resourceRoot);
            return normalizedRoot.StartsWith("resources/", StringComparison.OrdinalIgnoreCase)
                ? "Resources/" + normalizedRoot.Substring("resources/".Length)
                : normalizedRoot;
        }

        private static string GetResourceFileName(string resourcePath)
        {
            string fileName = resourcePath?.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "skin.png";
            return Uri.UnescapeDataString(fileName);
        }

        private sealed class SkinPackEntry
        {
            public string DisplayName { get; set; }

            public string LocalizationName { get; set; }

            public string TexturePath { get; set; }

            public byte[] PngBytes { get; set; }

            public string CapePath { get; set; }

            public byte[] CapePngBytes { get; set; }
        }

        private sealed class SkinRequiredRegion
        {
            public SkinRequiredRegion(string name, int x, int y, int width, int height, double minimumCoverage)
            {
                Name = name;
                X = x;
                Y = y;
                Width = width;
                Height = height;
                MinimumCoverage = minimumCoverage;
            }

            public string Name { get; }

            public int X { get; }

            public int Y { get; }

            public int Width { get; }

            public int Height { get; }

            public double MinimumCoverage { get; }
        }

        public class SkinEntry : INotifyPropertyChanged
        {
            private bool isSelected;
            private ImageSource previewImage;
            private readonly Func<Stream> openStream;

            private SkinEntry(string fileName, string filePath, string resourcePath, Func<Stream> openStream)
            {
                FileName = fileName;
                FilePath = filePath;
                ResourcePath = resourcePath;
                this.openStream = openStream;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public string FileName { get; }

            public string FilePath { get; }

            public string ResourcePath { get; }

            public ImageSource PreviewImage
            {
                get => previewImage;
                private set
                {
                    previewImage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewImage)));
                }
            }

            public bool IsSelected
            {
                get => isSelected;
                set
                {
                    if (isSelected == value)
                    {
                        return;
                    }

                    isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public static SkinEntry FromFile(string filePath)
            {
                string resolvedPath = Path.GetFullPath(filePath);
                return new SkinEntry(Path.GetFileName(resolvedPath), resolvedPath, null, () => File.OpenRead(resolvedPath));
            }

            public static SkinEntry FromResource(string resourcePath)
            {
                string normalizedPath = resourcePath.Replace('\\', '/');
                return new SkinEntry(GetResourceFileName(normalizedPath), null, normalizedPath, () =>
                {
                    StreamResourceInfo resourceInfo = Application.GetResourceStream(new Uri(normalizedPath, UriKind.Relative));
                    if (resourceInfo?.Stream == null)
                    {
                        throw new FileNotFoundException("Embedded skin resource was not found.", normalizedPath);
                    }

                    return resourceInfo.Stream;
                });
            }

            public Stream OpenRead()
            {
                return openStream();
            }

            public void EnsurePreview()
            {
                if (PreviewImage != null)
                {
                    return;
                }

                PreviewImage = SkinPreviewRenderer.CreatePreview(this) ?? SkinPreviewRenderer.CreateFallbackPreview(this);
            }
        }

        private static class SkinPreviewRenderer
        {
            private const int PreviewWidth = 210;
            private const int PreviewHeight = 310;

            public static ImageSource CreatePreview(SkinEntry skinEntry)
            {
                try
                {
                    BitmapImage skin = LoadBitmap(skinEntry);

                    DrawingVisual visual = new DrawingVisual();
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
                    RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);

                    using (DrawingContext dc = visual.RenderOpen())
                    {
                        bool hasOuterLayer = skin.PixelHeight >= skin.PixelWidth;

                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(76, 0, 0, 0)), null, new Point(105, 292), 48, 10);
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), null, new Rect(46, 14, 118, 278), 18, 18);

                        DrawFlatPart(dc, skin, new SkinRegion(44, 20, 4, 12), new Rect(41, 94, 32, 96), 1.0);
                        DrawFlatPart(dc, skin, new SkinRegion(20, 20, 8, 12), new Rect(73, 94, 64, 96), 1.0);
                        DrawFlatPart(dc, skin, new SkinRegion(36, 52, 4, 12), new Rect(137, 94, 32, 96), 1.0);

                        DrawFlatPart(dc, skin, new SkinRegion(4, 20, 4, 12), new Rect(73, 190, 32, 96), 1.0);
                        DrawFlatPart(dc, skin, new SkinRegion(20, 52, 4, 12), new Rect(105, 190, 32, 96), 1.0);

                        DrawFlatPart(dc, skin, new SkinRegion(8, 8, 8, 8), new Rect(73, 30, 64, 64), 1.0);

                        if (hasOuterLayer)
                        {
                            DrawFlatPart(dc, skin, new SkinRegion(44, 36, 4, 12), Inflate(new Rect(41, 94, 32, 96), 2), 0.95);
                            DrawFlatPart(dc, skin, new SkinRegion(20, 36, 8, 12), Inflate(new Rect(73, 94, 64, 96), 2), 0.95);
                            DrawFlatPart(dc, skin, new SkinRegion(52, 52, 4, 12), Inflate(new Rect(137, 94, 32, 96), 2), 0.95);

                            DrawFlatPart(dc, skin, new SkinRegion(4, 36, 4, 12), Inflate(new Rect(73, 190, 32, 96), 2), 0.95);
                            DrawFlatPart(dc, skin, new SkinRegion(4, 52, 4, 12), Inflate(new Rect(105, 190, 32, 96), 2), 0.95);

                            DrawFlatPart(dc, skin, new SkinRegion(40, 8, 8, 8), Inflate(new Rect(73, 30, 64, 64), 3), 0.94);
                        }
                    }

                    RenderTargetBitmap preview = new RenderTargetBitmap(PreviewWidth, PreviewHeight, 96, 96, PixelFormats.Pbgra32);
                    preview.Render(visual);
                    preview.Freeze();
                    return preview;
                }
                catch
                {
                    return null;
                }
            }

            private static void DrawFlatPart(DrawingContext dc, BitmapSource skin, SkinRegion source, Rect destination, double opacity)
            {
                DrawImage(dc, Crop(skin, source.X, source.Y, source.Width, source.Height), PixelSnap(destination), opacity);
            }

            public static ImageSource CreateFallbackPreview(SkinEntry skinEntry)
            {
                try
                {
                    return LoadBitmap(skinEntry, 128);
                }
                catch
                {
                    return null;
                }
            }

            private static BitmapImage LoadBitmap(SkinEntry skinEntry, int decodePixelWidth = 0)
            {
                using (Stream stream = skinEntry.OpenRead())
                {
                    BitmapImage skin = new BitmapImage();
                    skin.BeginInit();
                    skin.CacheOption = BitmapCacheOption.OnLoad;
                    skin.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    if (decodePixelWidth > 0)
                    {
                        skin.DecodePixelWidth = decodePixelWidth;
                    }

                    skin.StreamSource = stream;
                    skin.EndInit();
                    skin.Freeze();
                    return skin;
                }
            }

            private static void DrawBox(DrawingContext dc, BitmapSource skin, SkinRegion front, SkinRegion side, SkinRegion top, Rect frontRect, double depth, bool sideOnLeft, double opacity)
            {
                BitmapSource frontTexture = Crop(skin, front.X, front.Y, front.Width, front.Height);
                BitmapSource sideTexture = Crop(skin, side.X, side.Y, side.Width, side.Height);
                BitmapSource topTexture = Crop(skin, top.X, top.Y, top.Width, top.Height);

                Point[] topPoints;
                Point[] sidePoints;

                if (sideOnLeft)
                {
                    topPoints = new[]
                    {
                        new Point(frontRect.Left, frontRect.Top),
                        new Point(frontRect.Left - depth, frontRect.Top - depth),
                        new Point(frontRect.Right - depth, frontRect.Top - depth),
                        new Point(frontRect.Right, frontRect.Top)
                    };
                    sidePoints = new[]
                    {
                        new Point(frontRect.Left, frontRect.Top),
                        new Point(frontRect.Left - depth, frontRect.Top - depth),
                        new Point(frontRect.Left - depth, frontRect.Bottom - depth),
                        new Point(frontRect.Left, frontRect.Bottom)
                    };
                }
                else
                {
                    topPoints = new[]
                    {
                        new Point(frontRect.Left, frontRect.Top),
                        new Point(frontRect.Left + depth, frontRect.Top - depth),
                        new Point(frontRect.Right + depth, frontRect.Top - depth),
                        new Point(frontRect.Right, frontRect.Top)
                    };
                    sidePoints = new[]
                    {
                        new Point(frontRect.Right, frontRect.Top),
                        new Point(frontRect.Right + depth, frontRect.Top - depth),
                        new Point(frontRect.Right + depth, frontRect.Bottom - depth),
                        new Point(frontRect.Right, frontRect.Bottom)
                    };
                }

                DrawImageInPolygon(dc, topTexture, topPoints, opacity * 0.88);
                DrawImageInPolygon(dc, sideTexture, sidePoints, opacity * 0.68);
                DrawImage(dc, frontTexture, frontRect, opacity);

                Pen outlinePen = new Pen(new SolidColorBrush(Color.FromArgb(95, 0, 0, 0)), 1);
                outlinePen.Freeze();
                dc.DrawRectangle(null, outlinePen, frontRect);
                DrawPolygonOutline(dc, topPoints, outlinePen);
                DrawPolygonOutline(dc, sidePoints, outlinePen);

                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), null, new Rect(frontRect.Left, frontRect.Top, frontRect.Width, Math.Max(2, frontRect.Height * 0.08)));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), null, new Rect(frontRect.Left, frontRect.Bottom - Math.Max(2, frontRect.Height * 0.08), frontRect.Width, Math.Max(2, frontRect.Height * 0.08)));
            }

            private static void DrawImage(DrawingContext dc, ImageSource image, Rect destination, double opacity)
            {
                if (image == null)
                {
                    return;
                }

                dc.PushOpacity(opacity);
                dc.DrawImage(image, destination);
                dc.Pop();
            }

            private static void DrawImageInPolygon(DrawingContext dc, ImageSource image, Point[] points, double opacity)
            {
                if (image == null || points == null || points.Length < 4)
                {
                    return;
                }

                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(points[0], true, true);
                    context.PolyLineTo(new[] { points[1], points[2], points[3] }, true, true);
                }

                geometry.Freeze();

                ImageBrush brush = new ImageBrush(image)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                brush.Freeze();

                dc.PushOpacity(opacity);
                dc.DrawGeometry(brush, null, geometry);
                dc.Pop();
            }

            private static void DrawPolygonOutline(DrawingContext dc, Point[] points, Pen pen)
            {
                if (points == null || points.Length < 4)
                {
                    return;
                }

                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(points[0], false, true);
                    context.PolyLineTo(new[] { points[1], points[2], points[3] }, true, true);
                }

                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }

            private static void DrawRimLight(DrawingContext dc)
            {
                Pen lightPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1);
                lightPen.Freeze();
                dc.DrawLine(lightPen, new Point(55, 68), new Point(91, 20));
                dc.DrawLine(lightPen, new Point(132, 28), new Point(158, 98));
            }

            private static Rect Inflate(Rect rect, double amount)
            {
                rect.Inflate(amount, amount);
                return rect;
            }

            private static Rect PixelSnap(Rect rect)
            {
                return new Rect(
                    Math.Round(rect.X),
                    Math.Round(rect.Y),
                    Math.Round(rect.Width),
                    Math.Round(rect.Height));
            }

            private static BitmapSource Crop(BitmapSource source, int x, int y, int width, int height)
            {
                double scaleX = source.PixelWidth / 64.0;
                double scaleY = source.PixelHeight >= source.PixelWidth ? source.PixelHeight / 64.0 : source.PixelHeight / 32.0;

                int cropX = Clamp((int)Math.Round(x * scaleX), 0, source.PixelWidth - 1);
                int cropY = Clamp((int)Math.Round(y * scaleY), 0, source.PixelHeight - 1);
                int cropWidth = Math.Max(1, Math.Min((int)Math.Round(width * scaleX), source.PixelWidth - cropX));
                int cropHeight = Math.Max(1, Math.Min((int)Math.Round(height * scaleY), source.PixelHeight - cropY));

                CroppedBitmap cropped = new CroppedBitmap(source, new Int32Rect(cropX, cropY, cropWidth, cropHeight));
                cropped.Freeze();
                return cropped;
            }

            private static int Clamp(int value, int min, int max)
            {
                if (value < min)
                {
                    return min;
                }

                return value > max ? max : value;
            }

            private struct SkinRegion
            {
                public SkinRegion(int x, int y, int width, int height)
                {
                    X = x;
                    Y = y;
                    Width = width;
                    Height = height;
                }

                public int X { get; }
                public int Y { get; }
                public int Width { get; }
                public int Height { get; }
            }
        }
    }
}
