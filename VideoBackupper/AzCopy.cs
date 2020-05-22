using Microsoft.Azure.Storage.Blob;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VideoBackupper
{
    class AzCopy
    {
        private static string _path;
        private static SharedAccessBlobPolicy _policy;

        public static void Initialize(string path)
        {
            _path = path;

            _policy = new SharedAccessBlobPolicy
            {
                SharedAccessExpiryTime = DateTime.UtcNow.AddDays(1),
                Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Add | SharedAccessBlobPermissions.Create | SharedAccessBlobPermissions.Delete,
            };
        }

        public static async Task UploadFileAsync(string fileName, CloudBlockBlob blob)
        {
            var sasBlobToken = blob.GetSharedAccessSignature(_policy);
            var pi = new ProcessStartInfo
            {
                FileName = _path,
                Arguments = $"copy \"{fileName}\" \"{blob.Uri.OriginalString}{sasBlobToken}\" --overwrite=prompt --follow-symlinks --recursive --from-to=LocalBlob --blob-type=Detect --put-md5",
                RedirectStandardOutput = true,
            };

            var tcs = new TaskCompletionSource<bool>();
            using (var p = Process.Start(pi))
            {
                p.Exited += (sender, e) => tcs.TrySetResult(true);
                p.EnableRaisingEvents = true;

                string line;
                bool isCompleted = false;
                pastCl = 0;
                while ((line = await p.StandardOutput.ReadLineAsync()) != null)
                {
                    WriteOver(line);
                    if (line.StartsWith("Final Job Status: Completed")) isCompleted = true;
                }

                WriteOver("");
                Console.CursorLeft = 0;

                if (!isCompleted) throw new Exception();

                await tcs.Task;
            }
        }

        private static int pastCl = 0;

        private static void WriteOver(string str)
        {
            Console.CursorLeft = 0;
            Console.Write(str.PadRight(pastCl));
            pastCl = str.Length;
        }
    }
}
