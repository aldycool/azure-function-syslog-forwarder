# azure-function-syslog-forwarder

- This is an Azure Function to forward Azure Event Hub events that has been processed by Azure Analytics Job into a Syslog Server.
- In the Azure Analytics Job resource, create a new Input that specifies the Event Hub that you want to process as the input, and then create a new Output with type Azure Function (the Azure Function itself must have been provisioned first, using deployment from this project), and its Query must specify `INTO` the newly created Output Alias, and start the Analytics Job resource.
- In the Azure Analytics Job resource's Query, we can specify custom fields such as `procId` and `msgId` (these are standard fields from Syslog format), read more about this in [Custom Info on Posted Data](#custom-info-on-posted-data).

## Folders

- App: The main Azure Function codes, generated with VSCode Extension: Azure Functions (Command Pallette: Azure Functions: Create New Project)
- TestConsole: Console app to test various functions

## Required Environment Variables

- In Azure Portal, go to Function App, Configuration, Application Settings tab, add these new Application Settings with their values:
  - APP_SYSLOG_HOST: 3.192.122.144 (must use the Azure VM's Public IP Address, because private IP is not reachable from Function App)
  - APP_SYSLOG_PORT: 514
  - (Optional) APP_VERBOSE: either 1 (only display one log line for posted list of events) or 2 (display all events in the posted list), default is 1
- NOTE: Check the "Deployment App Setting" during creation of the Application Settings. This is to ensure that this environment variable can be consumed by the Deployment slots.

## Custom Info on Posted Data

- Add these fields into the Azure Analytics Job's Query:
  - `procId`: will be set as Syslog procId, if not set, this will be Syslog NILVALUE (character: `-`)
  - `msgId`: will be set as Syslog msgId, if not set, this will be Syslog NILVALUE (character: `-`)
- For example, the Query will be: `SELECT 'myInfo1' as procId, 'myTag1' as msgId, [other fields] FROM [eventhub] ...snip...`
- These new Query fields will add new keys in the posted JSON data, which then will be read by the Azure Function.

## Flattening Event Hub As Input

- This is an optional step. Because of Azure Event Hub JSON data always encapsulate the real logs in a JSON array called `records` in each of the root JSON object, we want to "flatten" the original input so that we can query easily. For example, the original posted JSON data from Azure Event Hub is like this:

```json
[
  {
    "EventProcessedUtcTime": "2023-04-03T18:02:23.9345201Z",
    "PartitionId": 0,
    "EventEnqueuedUtcTime": "2023-04-03T18:02:23.769Z",
    "records": 
    [
      {
        "key1": "value1",
      },
      {
        "key2": "value2",
      }
    ]
  },
  {
    "EventProcessedUtcTime": "2023-04-03T18:02:23.9345201Z",
    "PartitionId": 0,
    "EventEnqueuedUtcTime": "2023-04-03T18:02:23.769Z",
    "records": 
    [
      {
        "key1": "value1",
      },
      {
        "key2": "value2",
      }
    ]
  }
]
```

- So based on the original JSON above, we want to extract the `records` array and combine them together as a single list of JSON objects. For this, the Analytics Job's query can written as this example:

```sql
SELECT
    'AzureFirewall' AS procId,
    event.EventProcessedUtcTime,
    event.PartitionId,
    event.EventEnqueuedUtcTime,
    event.data.category,
    event.data.time,
    event.data.resourceId,
    event.data.operationName,
    event.data.properties.msg
INTO
    [syslog-azurefirewall]
FROM
(
SELECT   
    a.*,
    b.ArrayValue AS data
FROM [event-hub-azurefirewall] AS a  
CROSS APPLY GetArrayElements(a.records) AS b
) AS event
```

- The `[syslog-azurefirewall]` as INTO above must match the alias of the Azure Function Output.
- The output of the Query above will result into something like this:

```json
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
```

- The processed JSON above is the Posted JSON data that is fed into the Azure Function in `App/HttpTriggerRequest.cs`.

## Monitor Running Function

- In Azure Portal, go to Function App, Functions, click the function, Monitor, Logs tab. NOTE: You had to have Application Insight resource created for this Function App to be able to view the logs.

## Test With Log Server

- From: <https://hub.docker.com/r/balabit/syslog-ng>: `docker run --rm -p 514:514/udp -p 601:601 --name syslog-ng balabit/syslog-ng:latest -edv`. This will launch a Syslog Server where we can send log into it with host: 127.0.0.1, port: 514, protocol: UDP.
