using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SyslogNet.Client;
using SyslogNet.Client.Serialization;
using SyslogNet.Client.Transport;

namespace AzureFunctionSyslogForwarder
{
    public static class HttpTriggerRequest
    {
        [FunctionName("HttpTriggerRequest")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            var appSyslogHost = System.Environment.GetEnvironmentVariable("APP_SYSLOG_HOST");
            if (string.IsNullOrEmpty(appSyslogHost))
            {
                log.LogError($"{nameof(HttpTriggerRequest)}, APP_SYSLOG_HOST is null or empty");
                return new OkObjectResult("");
            }
            var appSyslogPort = System.Environment.GetEnvironmentVariable("APP_SYSLOG_PORT");
            if (string.IsNullOrEmpty(appSyslogPort))
            {
                log.LogError($"{nameof(HttpTriggerRequest)}, APP_SYSLOG_PORT is null or empty");
                return new OkObjectResult("");
            }
            var appVerbose = System.Environment.GetEnvironmentVariable("APP_VERBOSE");
            if (string.IsNullOrEmpty(appVerbose)) appVerbose = "1";

            string outputMessage = $"{nameof(HttpTriggerRequest)}, APP_SYSLOG_HOST: {appSyslogHost}, APP_SYSLOG_PORT: {appSyslogPort}, Method: {req.Method}";

            // === START DEBUG ONLY ===
            // outputMessage = $"{outputMessage}, LocalIP: {debugGetLocalIP()}";
            // outputMessage = $"{outputMessage}, TrySocketConnect-{appSyslogHost}-{appSyslogPort}: {debugTrySocketConnect(appSyslogHost, Convert.ToInt32(appSyslogPort))}";
            // outputMessage = $"{outputMessage}, TrySocketConnect-{"3.192.122.144"}-{appSyslogPort}: {debugTrySocketConnect("3.192.122.144", Convert.ToInt32(appSyslogPort))}";
            // === END DEBUG ONLY ===

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrEmpty(requestBody)) 
            {
                // Ref: https://learn.microsoft.com/en-us/azure/stream-analytics/azure-functions-output
                // Handle empty POST for testing connection
                log.LogInformation($"{outputMessage}, Empty POST data handled");
                return new OkObjectResult("");

                // Ref: https://learn.microsoft.com/en-us/azure/stream-analytics/stream-analytics-with-azure-functions#create-a-function-in-azure-functions-that-can-write-data-to-azure-cache-for-redis
                // Because this function is related to Stream Analytics, returning 204 is not valid.
                // log.LogInformation($"{nameof(HttpTriggerRequest)}, RequestBody is empty");
                // return new StatusCodeResult(204); // 204, ASA connectivity check
            } 

            // Reject if too large, as per the doc
            if (requestBody.Length > 262144) 
            {
                log.LogInformation($"{outputMessage}, Request is too large");
                return new StatusCodeResult(413); //HttpStatusCode.RequestEntityTooLarge
            }

            // Example of requestBody:
            /*
            [
                {
                    "procId": "AzureFirewall",
                    "EventProcessedUtcTime":"2023-04-03T18:02:23.9345201Z",
                    "PartitionId":0,
                    "EventEnqueuedUtcTime":"2023-04-03T18:02:23.769Z",
                    "category":"AzureFirewallNetworkRule",
                    "time":"2023-04-03T17:51:00.395927Z",
                    "resourceId":"/SUBSCRIPTIONS/9FF12CED-B581-4E80-A835-191C0653A65E/RESOURCEGROUPS/RG/PROVIDERS/MICROSOFT.NETWORK/AZUREFIREWALLS/FW001",
                    "operationName":"AzureFirewallNetworkRuleLog",
                    "msg":"TCP request from 10.0.4.4:44432 to 10.0.6.7:80. Action: Allow. Rule Collection: RevProxy. Rule: WebsiteRequest"
                },
                {
                    "procId": "AzureFirewall",
                    "EventProcessedUtcTime":"2023-04-03T18:02:23.9345201Z",
                    "PartitionId":0,
                    "EventEnqueuedUtcTime":"2023-04-03T18:02:23.769Z",
                    "category":"AzureFirewallNetworkRule",
                    "time":"2023-04-03T17:51:00.44382Z",
                    "resourceId":"/SUBSCRIPTIONS/9FF12CED-B581-4E80-A835-191C0653A65E/RESOURCEGROUPS/RG/PROVIDERS/MICROSOFT.NETWORK/AZUREFIREWALLS/FW001",
                    "operationName":"AzureFirewallNetworkRuleLog",
                    "msg":"TCP request from 10.0.4.4:44440 to 10.0.6.7:80. Action: Allow. Rule Collection: RevProxy. Rule: WebsiteRequest"
                }
            ]
            */

            bool isError = false;

            try
            {
                var list = JsonSerializer.Deserialize<List<JsonElement>>(requestBody)!;
                outputMessage = $"{outputMessage}, List Count: {list.Count}";

                ISyslogMessageSerializer serializer = new SyslogRfc5424MessageSerializer();
                Func<string, string, string, string, SyslogMessage> createSyslogMessage = (hostName, procId, msgId, message) => 
                {
                    string hostNameValue = Environment.MachineName;
                    if (!string.IsNullOrEmpty(hostName) && hostName != "-") hostNameValue = hostName;
                    SyslogMessage syslogMessage = new SyslogMessage(
                        DateTimeOffset.Now,
                        Facility.UserLevelMessages,
                        Severity.Informational,
                        hostNameValue,
                        nameof(AzureFunctionSyslogForwarder),
                        procId,
                        msgId,
                        message
                    );
                    return syslogMessage;
                };

                for (int i = 0; i < list.Count; i++)
                {
                    string outputMessageInner = $"{outputMessage}, Index: {i}";

                    try
                    {
                        string hostName = "-";
                        if (list[i].TryGetProperty("hostName", out JsonElement hostNameElement))
                        {
                            if (hostNameElement.ValueKind == JsonValueKind.String)
                            {
                                hostName = hostNameElement.GetString();
                            }
                        }
                        outputMessageInner = $"{outputMessageInner}, hostName: {hostName}";

                        string procId = "-";
                        if (list[i].TryGetProperty("procId", out JsonElement procIdElement))
                        {
                            if (procIdElement.ValueKind == JsonValueKind.String)
                            {
                                procId = procIdElement.GetString();
                            }
                        }
                        outputMessageInner = $"{outputMessageInner}, procId: {procId}";

                        string msgId = "-";
                        if (list[i].TryGetProperty("msgId", out JsonElement msgIdElement))
                        {
                            if (msgIdElement.ValueKind == JsonValueKind.String)
                            {
                                msgId = msgIdElement.GetString();
                            }
                        }
                        outputMessageInner = $"{outputMessageInner}, msgId: {msgId}";

                        string json = JsonSerializer.Serialize(list[i]);

                        SyslogMessage syslogMessage = createSyslogMessage(hostName, procId, msgId, json);
                        using (ISyslogMessageSender sender = new SyslogUdpSender(appSyslogHost, Convert.ToInt32(appSyslogPort)))
                        {
                            sender.Send(syslogMessage, serializer);
                        }
                    }
                    catch (Exception eInner)
                    {
                        if (appVerbose == "2") log.LogError($"{outputMessageInner}, Error: {eInner.Message}");
                        isError = true;
                    }

                    if (appVerbose == "2") log.LogInformation($"{outputMessageInner}");
                }
            }
            catch (Exception e)
            {
                log.LogError($"{outputMessage}, Error: {e.Message}");   
            }

            if (appVerbose == "1")
            {
                log.LogInformation($"{outputMessage}{(isError ? ", Errors detected in events, set Verbose to detail to view the errors" : "")}");
            }

            return new OkObjectResult("");
        }

        private static string debugGetLocalIP()
        {
            string localIP = "[N/A]";
            try
            {
                // Connect to Google Public DNS and one of its UDP ports
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = (IPEndPoint)socket.LocalEndPoint!;
                    localIP = endPoint.Address.ToString();
                }
            }
            catch (Exception e)
            {
                localIP = $"Error: {e.Message}";
            }
            return localIP;
        }

        private static bool debugTrySocketConnect(string ip, int port)
        {
            try
            {
                IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                using (Socket client = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
                {
                    client.Connect(ipEndPoint);
                    client.Shutdown(SocketShutdown.Both);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
    }
}
