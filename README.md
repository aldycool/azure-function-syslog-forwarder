# azure-function-syslog-forwarder

This is an Azure Function to forward Azure Event Hub events that has been processed by Azure Analytics Job into a Syslog Server. In the Azure Analytics Job resource, create a new Output with type Azure Function (the Azure Function itself must have been provisioned first, using deployment from this project), and its Query must specify `INTO` the newly created Output Alias, and start the Analytics Job resource. In the Azure Analytics Job resource's Query, we can specify custom fields such as `procId` and `msgId` (these are standard fields from Syslog format), read more about this in [Custom Info on Posted Data](#custom-info-on-posted-data).

## Folders

- App: The main Azure Function codes, generated with VSCode Extension: Azure Functions (Command Pallette: Azure Functions: Create New Project)
- TestConsole: Console app to test various functions

## Required Environment Variables

- In Azure Portal, go to Function App, Configuration, Application Settings tab, add these new Application Settings with their values:
  - APP_SYSLOG_HOST: 4.193.123.145 (must use the VM's Public IP Address, because private IP is not reachable from Function App)
  - APP_SYSLOG_PORT: 514
  - (Optional) APP_VERBOSE: either 1 (only display one log line for posted list of events) or 2 (display all events in the posted list)
- NOTE: Check the "Deployment App Setting" during creation of Application Settings. This is to ensure that this environment variable can be consumed by the Deployment slots.

## Custom Info on Posted Data

- Add these fields into the Azure Analytics Job's Query:
  - `procId`: will be set as Syslog procId, if not set, this will be Syslog NILVALUE (character: `-`)
  - `msgId`: will be set as Syslog msgId, if not set, this will be Syslog NILVALUE (character: `-`)
- For example, the Query will be: `SELECT 'myInfo1' as procId, 'myTag1' as msgId, [other fields] FROM [eventhub] ...snip...`
- These new Query fields will add new keys in the posted JSON data, which then will be read by the Azure Function.

## Monitor Running Function

- In Azure Portal, go to Function App, Functions, click the function, Monitor, Logs tab. NOTE: You had to have Application Insight resouce created for this Function App to be able to view the logs.

## Test With Log Server

- From: <https://hub.docker.com/r/balabit/syslog-ng>: `docker run --rm -p 514:514/udp -p 601:601 --name syslog-ng balabit/syslog-ng:latest -edv`. This will launch a Syslog Server where we can send log into it with host: 127.0.0.1, port: 514, protocol: UDP.
