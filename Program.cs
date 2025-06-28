using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Runtime.Loader;

namespace LinuxWebJobs
{
    internal class Program
    {
        // 終了フラグ
        private static volatile bool _shouldExit = false;
        private static readonly object _lockObject = new object();
        private static IHost? _host;
        private static Thread? _shutdownFileCheckThread;
        private static readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private static TelemetryClient? _telemetryClient;

        /// <summary>
        /// SIGTERMやその他の終了シグナルを処理
        /// </summary>
        static void SetupSignalHandlers()
        {
            // Console.CancelKeyPress (Ctrl+C) の処理
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // プロセスの即座終了を防ぐ
                LogAndTrackImmediate("SIGINT (Ctrl+C) を受信しました。適切に終了処理を開始します...", SeverityLevel.Warning);
                RequestExit();
            };

            // SIGTERM or SIGINT を捕捉（Linux の App Service で重要）
            AssemblyLoadContext.Default.Unloading += ctx =>
            {
                LogAndTrackImmediate("SIGTERM (Unloading) を受信", SeverityLevel.Warning);
                RequestExit();
            };

            var lifetime = _host?.Services.GetRequiredService<IHostApplicationLifetime>();
            if (lifetime != null)
            {
                lifetime.ApplicationStopping.Register(() =>
                {
                    LogAndTrackImmediate("アプリケーション停止通知", SeverityLevel.Information);
                    //RequestExit();
                });
            }

            // AppDomain.ProcessExit でSIGTERMやプロセス終了を処理
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                LogAndTrackImmediate("SIGTERM またはプロセス終了シグナルを受信しました。終了処理を開始します...", SeverityLevel.Warning);
                RequestExit();

                // 終了処理の完了を少し待つ
                Thread.Sleep(1000);
            };

            // Windows環境での追加のシグナル処理
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows固有のシグナル処理をここに追加可能
                LogAndTrackImmediate("Windows環境で実行中");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                LogAndTrackImmediate("Linux環境で実行中 - SIGTERM処理が有効");
            }
        }

        /// <summary>
        /// 安全な終了要求
        /// </summary>
        static void RequestExit()
        {
            lock (_lockObject)
            {
                if (!_shouldExit)
                {
                    _shouldExit = true;
                    LogAndTrackImmediate("終了フラグが設定されました");

                    // IHostApplicationLifetimeを使用したGraceful Shutdown
                    //if (_host != null)
                    //{
                    //    var lifetime = _host.Services.GetService<IHostApplicationLifetime>();
                    //    lifetime?.StopApplication();
                    //}
                }
            }
        }

        /// <summary>
        /// TraceListenerを設定してログ出力を複数の出力先に送信
        /// </summary>
        static void SetupTraceListeners()
        {
            // コンソールへの出力
            Trace.Listeners.Add(new ConsoleTraceListener());

            // ファイルへの出力
            string logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            string logFilePath = Path.Combine(logDirectory, $"trace_{DateTime.Now:yyyyMMdd}.log");
            var fileListener = new TextWriterTraceListener(logFilePath)
            {
                Name = "FileTraceListener"
            };
            Trace.Listeners.Add(fileListener);

            // トレースレベルの設定
            Trace.AutoFlush = true;

            // 起動メッセージ
            LogAndTrackImmediate($"=== LinuxWebJob開始: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }

        static void Main(string[] args)
        {
            // 設定の読み込み
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            // IHostApplicationLifetimeを使用するためのHostの作成（Graceful Shutdownのみ）
            _host = Host.CreateDefaultBuilder(args)
                .UseConsoleLifetime()
                .Build();

            // シグナルハンドラーの設定
            SetupSignalHandlers();

            // TraceListenerの設定
            SetupTraceListeners();

            // Application Insights の接続文字列を設定（JSONから読み込み）
            string? connectionString = configuration.GetSection("ApplicationInsights:ConnectionString").Value;
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Application Insights接続文字列が設定されていません。appsettings.jsonを確認してください。");
            }
            
            var config = new TelemetryConfiguration();
            config.ConnectionString = connectionString;
            _telemetryClient = new TelemetryClient(config);

            //"output" フォルダのパスを設定
            string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "output");

            //フォルダが存在しなければ作成
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            //"output.txt" ファイルのパスを設定
            string filePath = Path.Combine(folderPath, "output.txt");

            // Azure WebJobsシャットダウンファイル監視の設定
            SetupWebJobsShutdownFileWatcher();

            LogAndTrackImmediate("####################Start Application####################");

            try
            {
                // シグナルハンドラの設定
                SetupSignalHandlers();

                //ファイルストリームを開き、StreamWriterを使用して書き込む
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    while (true)
                    {
                        // 終了フラグが立っていたらループを抜ける
                        lock (_lockObject)
                        {
                            if (_shouldExit)
                            {
                                break;
                            }
                        }

                        // TraceListenerとApplication Insightsを使用した統合ログ出力
                        string message = $"LinuxWebJob Running: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                        LogAndTrackImmediate(message);

                        // Application Insights へのメトリック送信
                        _telemetryClient?.TrackMetric("SampleMetric", 150);
                        _telemetryClient?.Flush();

                        //現在の日時を取得
                        var now = DateTime.Now;

                        //日時をファイルに書き込む
                        writer.WriteLine(now.ToString());
                        writer.Flush(); // ファイルに直ちに書き出す

                        Console.WriteLine($"now:{now.ToString()}");

                        //1秒間待機
                        Thread.Sleep(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                LogAndTrackImmediate($"エラーが発生しました: {ex.Message}", SeverityLevel.Error);
                Console.WriteLine($"エラー: {ex.Message}");
            }
            finally
            {
                // TraceListenerのクリーンアップ
                CleanupTraceListeners();

                // シャットダウンファイルチェックスレッドの停止
                _cancellationTokenSource.Cancel();
                _shutdownFileCheckThread?.Join(5000); // 最大5秒待機

                // Hostのクリーンアップ
                _host?.Dispose();
            }
        }

        /// <summary>
        /// TraceListenerのリソースを適切に解放
        /// </summary>
        static void CleanupTraceListeners()
        {
            LogAndTrackImmediate($"=== LinuxWebJob End: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Trace.Flush();

            // 30秒間、1秒ごとにログ出力
            LogAndTrackImmediate("=== 終了処理中: 30秒間のログ出力を開始 ===");
            for (int i = 1; i <= 30; i++)
            {
                string shutdownMessage = $"終了処理中... {i}/30秒 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                LogAndTrackImmediate(shutdownMessage);
                Console.WriteLine(shutdownMessage);

                Thread.Sleep(1000); // 1秒間待機
            }

            // ファイルリスナーをクローズ
            foreach (TraceListener listener in Trace.Listeners)
            {
                if (listener is TextWriterTraceListener fileListener && listener.Name == "FileTraceListener")
                {
                    fileListener.Close();
                }
            }

            LogAndTrackImmediate("=== 30秒間のログ出力完了 ===");

            Trace.Listeners.Clear();
        }

        /// <summary>
        /// WEBJOBS_SHUTDOWN_FILE環境変数で指定されたファイルの監視を設定
        /// 別スレッドで1秒ごとにファイル存在をチェック
        /// </summary>
        static void SetupWebJobsShutdownFileWatcher()
        {
            string? shutdownFilePath = Environment.GetEnvironmentVariable("WEBJOBS_SHUTDOWN_FILE");

            if (string.IsNullOrEmpty(shutdownFilePath))
            {
                LogAndTrackImmediate("WEBJOBS_SHUTDOWN_FILE環境変数が設定されていません。Azure WebJobsのシャットダウンファイル監視をスキップします。");
                return;
            }

            LogAndTrackImmediate($"Azure WebJobsシャットダウンファイル監視を開始: {shutdownFilePath}");

            // 既にファイルが存在する場合は即座にシャットダウン
            if (File.Exists(shutdownFilePath))
            {
                LogAndTrackImmediate($"シャットダウンファイルが既に存在します: {shutdownFilePath}", SeverityLevel.Warning);
                RequestExit();
                return;
            }

            // 別スレッドでファイル存在を定期的にチェック
            _shutdownFileCheckThread = new Thread(() =>
            {
                try
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            if (File.Exists(shutdownFilePath))
                            {
                                LogAndTrackImmediate($"Azure WebJobsシャットダウンファイルが検出されました: {shutdownFilePath}", SeverityLevel.Warning);
                                RequestExit();
                                break;
                            }

                            // 1秒間待機（キャンセレーション対応）
                            _cancellationTokenSource.Token.WaitHandle.WaitOne(1000);
                        }
                        catch (Exception ex)
                        {
                            LogAndTrackImmediate($"シャットダウンファイルチェック中にエラーが発生: {ex.Message}", SeverityLevel.Error);
                            // エラーが発生しても継続して監視
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogAndTrackImmediate($"シャットダウンファイル監視スレッドでエラーが発生: {ex.Message}", SeverityLevel.Error);
                }
                finally
                {
                    LogAndTrackImmediate("シャットダウンファイル監視スレッドが終了しました");
                }
            })
            {
                IsBackground = true, // バックグラウンドスレッドとして設定
                Name = "ShutdownFileWatcher"
            };

            _shutdownFileCheckThread.Start();
            LogAndTrackImmediate("シャットダウンファイル監視スレッドを開始しました");
        }

        /// <summary>
        /// Trace.WriteLineとTelemetryClient.TrackTraceを同時に実行し、即座にFlushするヘルパーメソッド
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        /// <param name="severityLevel">テレメトリの重要度レベル（省略可能）</param>
        static void LogAndTrackImmediate(string message, SeverityLevel severityLevel = SeverityLevel.Information)
        {
            Trace.WriteLine(message);
            _telemetryClient?.TrackTrace(message, severityLevel);
            Trace.Flush();
            _telemetryClient?.Flush();
        }
    }
}
