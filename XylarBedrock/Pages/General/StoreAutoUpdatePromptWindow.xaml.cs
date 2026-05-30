using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Windows.System;

namespace XylarBedrock.Pages.General
{
    public partial class StoreAutoUpdatePromptWindow : Window
    {
        private bool isClosingAnimated;

        public StoreAutoUpdatePromptWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Storyboard storyboard = new Storyboard();

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation scaleXAnimation = new DoubleAnimation
            {
                From = 0.94,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(330),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
            };

            DoubleAnimation scaleYAnimation = new DoubleAnimation
            {
                From = 0.94,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(330),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
            };

            Storyboard.SetTarget(opacityAnimation, DialogCard);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(scaleXAnimation, DialogScale);
            Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("ScaleX"));
            Storyboard.SetTarget(scaleYAnimation, DialogScale);
            Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("ScaleY"));

            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Begin();
        }

        private async void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            await CloseWithAnimationAsync(false);
        }

        private async void YesButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenMicrosoftStoreSettingsAsync();
            await CloseWithAnimationAsync(true);
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                await CloseWithAnimationAsync(false);
            }
        }

        private static async Task OpenMicrosoftStoreSettingsAsync()
        {
            Uri settingsUri = new Uri("ms-windows-store://settings");

            try
            {
                if (await Launcher.LaunchUriAsync(settingsUri))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unable to open Microsoft Store settings URI: {ex}");
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = settingsUri.ToString(),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unable to shell-open Microsoft Store settings URI: {ex}");
                MessageBox.Show(
                    "Open Microsoft Store, go to Settings, then turn off App updates if you want to keep downgraded Minecraft versions from updating automatically.",
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async Task CloseWithAnimationAsync(bool dialogResult)
        {
            if (isClosingAnimated)
            {
                return;
            }

            isClosingAnimated = true;

            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            Storyboard storyboard = new Storyboard();

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            DoubleAnimation scaleXAnimation = new DoubleAnimation
            {
                To = 0.965,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            DoubleAnimation scaleYAnimation = new DoubleAnimation
            {
                To = 0.965,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTarget(opacityAnimation, DialogCard);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(scaleXAnimation, DialogScale);
            Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("ScaleX"));
            Storyboard.SetTarget(scaleYAnimation, DialogScale);
            Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("ScaleY"));

            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Completed += (_, _) => completion.TrySetResult(true);
            storyboard.Begin();

            await completion.Task;
            DialogResult = dialogResult;
            Close();
        }
    }
}
