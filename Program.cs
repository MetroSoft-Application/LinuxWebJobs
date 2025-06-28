using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

namespace LinuxWebJobs
{
    internal class Program
    {
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
            Trace.WriteLine($"=== LinuxWebJob開始: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }

        static void Main(string[] args)
        {
            // TraceListenerの設定
            SetupTraceListeners();

            // Application Insights の接続文字列を設定（新しい推奨方法）
            string connectionString = "";
            var config = new TelemetryConfiguration();
            config.ConnectionString = connectionString;
            var telemetryClient = new TelemetryClient(config);

            //"output" フォルダのパスを設定
            string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "output");

            //フォルダが存在しなければ作成
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            //"output.txt" ファイルのパスを設定
            string filePath = Path.Combine(folderPath, "output.txt");

            try
            {
                //ファイルストリームを開き、StreamWriterを使用して書き込む
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    while (true)
                    {
                        // TraceListenerを使用したログ出力
                        string message = $"LinuxWebJob実行中: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                        Trace.WriteLine(message);

                        // Application Insights へのテレメトリ送信
                        telemetryClient.TrackTrace("Hello, Application Insights!");
                        telemetryClient.TrackMetric("SampleMetric", 150);
                        telemetryClient.Flush();

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
                Trace.WriteLine($"エラーが発生しました: {ex.Message}");
                Console.WriteLine($"エラー: {ex.Message}");
            }
            finally
            {
                // TraceListenerのクリーンアップ
                CleanupTraceListeners();
            }
        }

        /// <summary>
        /// TraceListenerのリソースを適切に解放
        /// </summary>
        static void CleanupTraceListeners()
        {
            Trace.WriteLine($"=== LinuxWebJob終了: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Trace.Flush();

            // ファイルリスナーをクローズ
            foreach (TraceListener listener in Trace.Listeners)
            {
                if (listener is TextWriterTraceListener fileListener && listener.Name == "FileTraceListener")
                {
                    fileListener.Close();
                }
            }

            Trace.Listeners.Clear();
        }
    }
}
