using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Realms;
using ShComp;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VideoBackupper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var azcopyPath = args[0];
            var connectionString = args[1];
            var containerName = args[2];
            var sourceDirName = args[3];
            var backupDirName = args[4];

            var account = CloudStorageAccount.Parse(connectionString);
            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);

            var sasPolicy = new SharedAccessBlobPolicy
            {
                SharedAccessExpiryTime = DateTime.UtcNow.AddDays(1),
                Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Add | SharedAccessBlobPermissions.Create | SharedAccessBlobPermissions.Delete,
            };

            var realmConfig = new RealmConfiguration(Path.Combine(Environment.CurrentDirectory, "db.realm"));
            var fileLastWriteTimes = await RealmContext.InvokeAsync(realmConfig, realm => realm.All<Item>().ToDictionary(t => t.Name, t => t.LastWriteTime));

            foreach (var sourceSeriesName in Directory.EnumerateDirectories(sourceDirName))
            {
                var sourceSeriesUri = new Uri(sourceSeriesName);
                var seriesName = Path.GetFileName(sourceSeriesName);

                var backupSeriesName = Path.Combine(backupDirName, seriesName);
                var backupSeriesUri = new Uri(backupSeriesName);
                Directory.CreateDirectory(backupSeriesName);

                foreach (var fileName in Directory.EnumerateFiles(sourceSeriesName, "*", SearchOption.AllDirectories))
                {
                    if (fileName.Contains("Adobe Premiere Pro Auto-Save")) continue;
                    if (fileName.Contains("desktop.ini")) continue;

                    var lastWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(fileName));

                    var fileUri = new Uri(fileName);
                    var relativeUri = sourceSeriesUri.MakeRelativeUri(fileUri);
                    var name = relativeUri.ToString();

                    DateTimeOffset past;
                    if (!fileLastWriteTimes.TryGetValue(name, out past) || past != lastWriteTime)
                    {
                        Console.WriteLine($"{name}");

                        var backupFileUri = new Uri(backupSeriesUri, relativeUri);
                        var backupFileName = backupFileUri.LocalPath;

                        Directory.CreateDirectory(Path.GetDirectoryName(backupFileName));
                        File.Copy(fileName, backupFileName, true);

                        var blob = container.GetBlockBlobReference(name);
                        if (!await blob.ExistsAsync() || blob.Properties.StandardBlobTier != StandardBlobTier.Archive)
                        {
                            await blob.DeleteIfExistsAsync();

                            var sasBlobToken = blob.GetSharedAccessSignature(sasPolicy);
                            var u = blob.Uri.OriginalString + sasBlobToken;
                            var p = Process.Start(azcopyPath, $"copy \"{fileName}\" \"{u}\" --overwrite=prompt --follow-symlinks --recursive --from-to=LocalBlob --blob-type=Detect --put-md5");
                            p.WaitForExit();

                            //await blob.UploadFromFileAsync(fileName);
                            if (Path.GetExtension(name) != ".prproj")
                            {
                                await blob.SetStandardBlobTierAsync(StandardBlobTier.Archive);
                            }
                        }

                        await RealmContext.InvokeAsync(realmConfig, realm =>
                        {
                            var item = realm.All<Item>().FirstOrDefault(t => t.Name == name);
                            realm.Write(() =>
                            {
                                if (item == null)
                                {
                                    item = new Item { Name = name };
                                    item = realm.Add(item);
                                }

                                item.LastWriteTime = lastWriteTime;
                            });
                        });
                    }

                    fileLastWriteTimes.Remove(name);
                }
            }
        }
    }
}
