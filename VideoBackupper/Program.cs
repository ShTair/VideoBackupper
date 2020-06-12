using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Realms;
using ShComp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VideoBackupper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var immutableFileExtensions = new string[] { ".mp4", ".wav", ".ts", ".mp3", ".mov" };

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

            var diskSem = new SemaphoreSlim(1);
            var blobSem = new SemaphoreSlim(1);
            var tasks = new List<Task>();

            await diskSem.LockAsync(() =>
            {
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
                        if (File.GetAttributes(fileName).HasFlag(FileAttributes.Hidden)) continue;

                        var lastWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(fileName));

                        var fileUri = new Uri(fileName);
                        var relativeUri = sourceSeriesUri.MakeRelativeUri(fileUri);
                        var name = relativeUri.ToString();

                        DateTimeOffset past;
                        if (!fileLastWriteTimes.TryGetValue(name, out past) || past != lastWriteTime)
                        {
                            Utils.WriteLine($"bcup {name}");

                            tasks.Add(Task.Run(async () =>
                            {
                                var backupFileUri = new Uri(backupSeriesUri, relativeUri);
                                var backupFileName = backupFileUri.LocalPath;

                                await diskSem.LockAsync(() =>
                                {
                                    Utils.WriteLine($"copy {name}");
                                    Directory.CreateDirectory(Path.GetDirectoryName(backupFileName));
                                    File.Copy(fileName, backupFileName, true);
                                });

                                await blobSem.LockAsync(async () =>
                                {
                                    Utils.WriteLine($"blob {name}");
                                    var blob = container.GetBlockBlobReference(name);
                                    if (!await blob.ExistsAsync() || blob.Properties.StandardBlobTier != StandardBlobTier.Archive)
                                    {
                                        await blob.DeleteIfExistsAsync();
                                        await AzCopy.UploadFileAsync(fileName, blob);

                                        if (immutableFileExtensions.Contains(Path.GetExtension(name).ToLower()))
                                        {
                                            await blob.SetStandardBlobTierAsync(StandardBlobTier.Archive);
                                        }
                                    }
                                });

                                await diskSem.LockAsync(async () =>
                                {
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

                                    fileLastWriteTimes.Remove(name);
                                });
                            }));
                        }
                        else
                        {
                            Utils.WriteLine($"pass {name}");
                            fileLastWriteTimes.Remove(name);
                        }
                    }
                }
            });

            await Task.WhenAll(tasks);

            if (fileLastWriteTimes.Count != 0)
            {
                foreach (var removeFileName in fileLastWriteTimes.Keys)
                {
                    Utils.WriteLine($"removed {removeFileName}");

                    var blob = container.GetBlockBlobReference(removeFileName);
                    if (await blob.ExistsAsync() && blob.Properties.StandardBlobTier != StandardBlobTier.Archive)
                    {
                        Utils.WriteLine($"archive {removeFileName}");
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
