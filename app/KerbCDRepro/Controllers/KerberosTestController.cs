using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Web.Http;

namespace KerbCDRepro.Controllers
{
    [RoutePrefix("kerberosTest")]
    public class KerberosTestController : ApiController
    {
        private string ComponentId => Request.RequestUri.Host.Substring(0, 1).ToUpperInvariant();

        [Route("")]
        [HttpGet]
        public IHttpActionResult KerberosTest(string targets = "")
        {
            Log($"Running Kerberos CD test on component {ComponentId}...");

            LogIdentity();

            var targetValues = targets.Split(',');

            if (targetValues.Length == 0)
            {
                var identityName = WindowsIdentity.GetCurrent().Name;
                Log($"Returning name of current windows identity {identityName}...");

                return Ok($"{ComponentId}: {identityName.Replace("\\", "/")}");
            }

            var remainingTargets = string.Join(",", targetValues.Skip(1));

            var targetCompId = targetValues[0].ToUpperInvariant();

            var componentUrlTemplate = ConfigurationManager.AppSettings["ComponentUrlTemplate"];

            var queryParams = remainingTargets.Any() ? "" : $"?targets={remainingTargets}";
            var url = $"{string.Format(componentUrlTemplate, targetCompId.ToLowerInvariant())}/kerberosTest{queryParams}";

            using (var client = new WebClient { UseDefaultCredentials = true })
            {
                Log($"Calling component {targetCompId}...");

                try
                {
                    var response = client.DownloadString(url);

                    Log($"Call to component {targetCompId} was successful.");

                    return Ok($"{ComponentId} -> {response.Replace("\"", string.Empty)}");
                }
                catch(Exception e)
                {
                    Log($"Call to component {targetCompId} failed.\n{e}");

                    return Ok($"{ComponentId} -> {targetCompId}: failed\n\n{e}");
                }
            }
        }

        private void LogIdentity()
        {
            Log(WindowsIdentity.GetCurrent().Name);
            Log(WindowsIdentity.GetCurrent().AuthenticationType);
            Log(WindowsIdentity.GetCurrent().ImpersonationLevel.ToString());
        }

        public void Log(string message)
        {
            var logFilePathTemplate = ConfigurationManager.AppSettings["LogFilePathTemplate"];
            var logFilePath = string.Format(logFilePathTemplate, ComponentId);

            var directoryName = new FileInfo(logFilePath).DirectoryName;

            if (string.IsNullOrEmpty(directoryName))
            {
                throw new ArgumentNullException(nameof(logFilePath));
            }

            Directory.CreateDirectory(directoryName);

            File.AppendAllText(logFilePath, $"{DateTimeOffset.UtcNow:s}|{message}\n");
        }
    }
}