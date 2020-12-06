using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace arcaea_archive_azfunc
{
    // ReSharper disable once UnusedType.Global
    public static class ScheduledArchive
    {
        [FunctionName("ScheduledArchive")]
        public static void Run([TimerTrigger("0 0 * * * *")] TimerInfo timer, ILogger log)
        {
            log.LogInformation("ScheduledArchive ok: date=");
        }
    }
}
