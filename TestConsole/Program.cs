using System.Text.Json;
using SyslogNet.Client;
using SyslogNet.Client.Serialization;
using SyslogNet.Client.Transport;
using System.Net;
using System.Net.Sockets;

var json = @"
            [
            {
                ""EventProcessedUtcTime"":""2023-04-03T18:02:23.9345201Z"",
                ""PartitionId"":0,
                ""EventEnqueuedUtcTime"":""2023-04-03T18:02:23.769Z"",
                ""category"":""AzureFirewallNetworkRule"",
                ""time"":""2023-04-03T17:51:00.395927Z"",
                ""resourceId"":""/SUBSCRIPTIONS/9FF12CED-B581-4E80-A835-191C0653A65E/RESOURCEGROUPS/RG/PROVIDERS/MICROSOFT.NETWORK/AZUREFIREWALLS/FW001"",
                ""operationName"":""AzureFirewallNetworkRuleLog"",
                ""msg"":""TCP request from 10.0.4.4:44432 to 10.0.6.7:80. Action: Allow. Rule Collection: RevProxy. Rule: WebsiteRequest""
            },
            {
                ""EventProcessedUtcTime"":""2023-04-03T18:02:23.9345201Z"",
                ""PartitionId"":0,
                ""EventEnqueuedUtcTime"":""2023-04-03T18:02:23.769Z"",
                ""category"":""AzureFirewallNetworkRule"",
                ""time"":""2023-04-03T17:51:00.44382Z"",
                ""resourceId"":""/SUBSCRIPTIONS/9FF12CED-B581-4E80-A835-191C0653A65E/RESOURCEGROUPS/RG/PROVIDERS/MICROSOFT.NETWORK/AZUREFIREWALLS/FW001"",
                ""operationName"":""AzureFirewallNetworkRuleLog"",
                ""msg"":""TCP request from 10.0.4.4:44440 to 10.0.6.7:80. Action: Allow. Rule Collection: RevProxy. Rule: WebsiteRequest""
            }
            ]
";

var list = JsonSerializer.Deserialize<List<JsonElement>>(json)!;

for (int i = 0; i < list.Count; i++)
{
    bool isOk = list[i].TryGetProperty("PartitionId", out JsonElement testout);
    Console.WriteLine(JsonSerializer.Serialize(list[i]));
}

ISyslogMessageSerializer serializer = new SyslogRfc5424MessageSerializer();
ISyslogMessageSender sender = new SyslogUdpSender("127.0.0.1", 514);
Func<SyslogMessage> createMsg = () => 
{ 
    SyslogMessage message = new SyslogMessage(
        DateTimeOffset.Now,
        Facility.UserLevelMessages,
        Severity.Informational,
        Environment.MachineName,
        "TestAppName",
        "TestProcId",
        "TestMsgId",
        "Test message at " + DateTime.Now
    );
    return message;
};
SyslogMessage msg1 = createMsg();
sender.Send(msg1, serializer);
SyslogMessage msg2 = createMsg();
sender.Send(msg2, serializer);
SyslogMessage msg3 = createMsg();
sender.Send(msg3, serializer);

string localIP;
using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
{
    // Connect to Google Public DNS and one of its port
    socket.Connect("8.8.8.8", 65530);
    IPEndPoint endPoint = (IPEndPoint)socket.LocalEndPoint!;
    localIP = endPoint.Address.ToString();
}

Console.WriteLine("Finished!");