using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace arcaea_archive_azfunc
{
    // ReSharper disable once UnusedType.Global
    public static class ScheduledArchive
    {
        [FunctionName("ScheduledArchive")]
        // ReSharper disable once UnusedMember.Global
        public static void Run([TimerTrigger("0 0 * * * *")]
            // ReSharper disable once UnusedParameter.Global
            TimerInfo timer, ILogger log)
        {
            var date = DateTime.UtcNow.ToString("s");
            var dsn = Environment.GetEnvironmentVariable("COSMOS_CONN_CONFIG");
            var account = CloudStorageAccount.Parse(dsn);
            var client = account.CreateCloudTableClient(new TableClientConfiguration());
            var table = client.GetTableReference("arcaea");

            try
            {
                var task = ArcaeaProberService.Get();
                task.Wait();
                var results = task.Result;

                var titleMsg = results[0];
                results.RemoveAt(0);
                var titles = JObject.Parse(titleMsg)["data"];

                var userMsg = results[0];
                results.RemoveAt(0);
                var user = JObject.Parse(userMsg)["data"];

                var songs = results.Select(m => (JArray) JObject.Parse(m)["data"])
                    .Aggregate(new JArray(), (array, tokens) =>
                    {
                        foreach (var token in tokens)
                        {
                            array.Add(token);
                        }

                        return array;
                    })
                    .Select(s =>
                    {
                        var song = (JObject) s;
                        var titleKey = (string) s["song_id"];
                        if (titleKey == "worldvanquisher")
                        {
                            titleKey = "worldvanquier";
                        }

                        song.Add("title", titles[titleKey]);
                        return song;
                    });

                var res = new JObject(new JProperty("user", user), new JProperty("songs", songs)).ToString();
                using var output = new MemoryStream();
                using var gzip = new GZipStream(output, CompressionMode.Compress);
                using var input = new MemoryStream(Encoding.UTF8.GetBytes(res));
                input.CopyTo(gzip);
                gzip.Close();
                var bin = output.ToArray();
                var serveEntity = new ArcaeaRes("serve", "latest") {Res = bin};
                var archiveEntity = new ArcaeaRes("archive", date) {Res = bin};
                table.ExecuteAsync(TableOperation.InsertOrReplace(serveEntity)).Wait();
                table.ExecuteAsync(TableOperation.InsertOrReplace(archiveEntity)).Wait();
                log.LogInformation($"ScheduledArchive ok: date={date}");
            }
            catch (ArchiveException e)
            {
                log.LogError($"ScheduledArchive fail: {e.Reason}");
            }
        }
    }

    public class ArcaeaRes : TableEntity
    {
        public byte[] Res { get; set; }

        public ArcaeaRes(string type, string date)
        {
            PartitionKey = type;
            RowKey = date;
        }
    }

    public class ArchiveException : Exception
    {
        public string Reason { get; }

        public ArchiveException(string reason)
        {
            Reason = reason;
        }
    }
}
