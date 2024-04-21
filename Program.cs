using System;
using System.IO;
using System.Threading;

namespace LinuxWebJobs
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // "output" フォルダのパスを設定
            string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "output");

            // フォルダが存在しなければ作成
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // "output.txt" ファイルのパスを設定
            string filePath = Path.Combine(folderPath, "output.txt");

            // ファイルストリームを開き、StreamWriterを使用して書き込む
            using (StreamWriter writer = new StreamWriter(filePath, true))
            {
                while (true)
                {
                    // 現在の日時を取得
                    var now = DateTime.Now;

                    // 日時をファイルに書き込む
                    writer.WriteLine(now.ToString());
                    writer.Flush(); // ファイルに直ちに書き出す

                    // 1秒間待機
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
