using System;
using System.IO;
using System.Threading;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

namespace LinuxWebJobs
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Application Insights のインストルメンテーションキーを設定
            string instrumentationKey = "566d9abe-f93a-408d-9315-4a002da9d32b";
            var telemetryConfiguration = new TelemetryConfiguration(instrumentationKey);
            var telemetryClient = new TelemetryClient(telemetryConfiguration);

            //"output" フォルダのパスを設定
            string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "output");

            //フォルダが存在しなければ作成
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            //"output.txt" ファイルのパスを設定
            string filePath = Path.Combine(folderPath, "output.txt");
            //ファイルストリームを開き、StreamWriterを使用して書き込む
            using (StreamWriter writer = new StreamWriter(filePath, true))
            {
                while (true)
                {
                    // トレースメッセージの送信
                    telemetryClient.TrackTrace("Hello, Application Insights!");

                    // メトリックの送信
                    telemetryClient.TrackMetric("SampleMetric", 150);

                    // データをすぐに送信
                    telemetryClient.Flush();

                    //現在の日時を取得
                    var now = DateTime.Now;

                    //日時をファイルに書き込む
                    writer.WriteLine(now.ToString());
                    writer.Flush(); // ファイルに直ちに書き出す

                    //1秒間待機
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
