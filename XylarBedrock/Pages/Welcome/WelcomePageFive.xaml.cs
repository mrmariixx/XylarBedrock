using System.Windows;
using System.Windows.Controls;
using XylarBedrock.Pages.General;

namespace XylarBedrock.Pages.Welcome
{
    public partial class WelcomePageFive : Page
    {
        public WelcomePagesSwitcher pageSwitcher = new WelcomePagesSwitcher();

        public WelcomePageFive()
        {
            InitializeComponent();
            BackButton.IsEnabled = false;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            pageSwitcher.MoveToPage(4);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            NextButton.IsEnabled = false;

            try
            {
                StoreAutoUpdatePromptWindow prompt = new StoreAutoUpdatePromptWindow
                {
                    Owner = Window.GetWindow(this)
                };

                prompt.ShowDialog();
                pageSwitcher.MoveToPage(6, BackupCheckbox.IsChecked == true);
            }
            finally
            {
                NextButton.IsEnabled = true;
            }
        }
    }
}
