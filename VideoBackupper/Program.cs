using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Realms;
using ShComp;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VideoBackupper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var immutableFileExtensions = new string[] { ".mp4", ".wav", ".ts" };

            var dbPath = args[0];
            AzCopy.Initialize(args[1]);
            var connectionString = args[2];
            var containerName = args[3];
            var sourceDirName = args[4];
            var backupDirName = args[5];

            var account = CloudStorageAccount.Parse(connectionString);
            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);

            var realmConfig = new RealmConfiguration(Path.Combine(dbPath, "db.realm"));
            var fileLastWriteTimes = await RealmContext.InvokeAsync(realmConfig, realm => realm.All<Item>().ToDictionary(t => t.Name, t => t.LastWriteTime));

            foreach (var sourceSeriesName in Directory.EnumerateDirectories(sourceDirName))
            {
                var sourceSeriesUri = new Uri(sourceSeriesName);
                var seriesName = Path.GetFileName(sourceSeriesName);

                var backupSeriesName = Path.Combine(backupDirName, seriesName);
                var backupSeriesUri = new Uri(backupSeriesName);

                foreach (var fileName in Directory.EnumerateFiles(sourceSeriesName, "*", SearchOption.AllDirectories))
                {
                    if (fileName.Contains("Adobe Premiere Pro Auto-Save")) continue;
                    if (fileName.Contains("Adobe Premiere Pro Audio Previews")) continue;
                    if (fileName.Contains("Adobe Premiere Pro Video Previews")) continue;
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
                            await AzCopy.UploadFileAsync(fileName, blob);

                            if (immutableFileExtensions.Contains(Path.GetExtension(name)))
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

            if (fileLastWriteTimes.Count != 0)
            {
                Console.WriteLine("層をアーカイブに変更します。");
                foreach (var removeFileName in fileLastWriteTimes.Keys)
                {
                    var blob = container.GetBlockBlobReference(removeFileName);
                    if (await blob.ExistsAsync() && blob.Properties.StandardBlobTier != StandardBlobTier.Archive)
                    {
                        Console.WriteLine($"{removeFileName}");
                        await blob.SetStandardBlobTierAsync(StandardBlobTier.Archive);
                    }

                    await RealmContext.InvokeAsync(realmConfig, realm =>
                    {
                        var item = realm.All<Item>().FirstOrDefault(t => t.Name == removeFileName);
                        realm.Write(() => realm.Remove(item));
                    });
                }
            }
        }
    }
}
