using System;
using System.Windows;
using XylarBedrock.Handlers;

namespace XylarBedrock
{
    public partial class App : Application
    {
        public static string Version => "0.0.0.3";
        public static string DisplayName => $"XylarBedrock v{Version}";
        public static string StartupNotice =>
            "IF U SEE THIS MESSAGE, IT MEANS THAT THIS APP IS MADE BY Xylar Inc. and Mrmariix." +
            "\n\nIF A RANDOM DUDE SAYS ITS HIS. LEAVE AND REPORT THE SERVER! ALSO BECAUSE THEY CAN PUT SPYWARE OR VIRUSES, THANKS." +
            "\n\n-Mrmariix";

        public App() : base()
        {
            DispatcherUnhandledException += RuntimeHandler.OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += RuntimeHandler.OnCurrentDomainUnhandledException;
        }
    }
}
