using System;
using System.Windows;

namespace ObjectDetectionApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 전역 예외 처리
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                MessageBox.Show($"예기치 않은 오류가 발생했습니다: {exception?.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"UI 오류가 발생했습니다: {args.Exception.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}