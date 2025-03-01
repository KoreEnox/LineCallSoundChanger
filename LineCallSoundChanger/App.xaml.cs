using System.Configuration;
using System.Data;
using System.Windows;

using System;
using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;

namespace LineCallSoundChanger
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _mutex = null;
        private const string MutexName = "LineCallSoundChangerAppInstance";

        protected override void OnStartup(StartupEventArgs e)
        {
            // アプリケーション名に基づいたMutexを作成
            _mutex = new Mutex(true, MutexName, out bool createdNew);

            // すでに起動している場合
            if (!createdNew)
            {
                MessageBox.Show("アプリケーションはすでに実行中です。", "多重起動エラー",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                _mutex.Close();
                _mutex = null;
                Shutdown(); // アプリケーションを終了
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // アプリケーション終了時にMutexを解放
            _mutex?.ReleaseMutex();
            _mutex?.Close();
            base.OnExit(e);
        }
    }
}
