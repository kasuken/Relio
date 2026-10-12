param location string
param tags object
param logAnalyticsWorkspaceId string

@description('Operator email for Relio failure alerts. Empty (default) deploys no action group and no alert rule.')
param alertEmailAddress string = ''

var alertsEnabled = !empty(alertEmailAddress)

// Keep this query identical to the one in docs/operations/monitoring.md. The event ids are the contract
// defined by Relio.Web.Diagnostics.RelioOperationalEvents; the payload never contains private data.
var failureQuery = '''
let relioEvents = datatable(EventId:int, EventName:string) [
    9001, 'RelioUnhandledHttpFailure',
    9002, 'RelioCircuitFailure',
    9003, 'RelioReminderJobFailure',
    9004, 'RelioProductMetricsRetentionFailure',
    9005, 'RelioBackupFailure'
];
AppServiceConsoleLogs
| where ResultDescription contains '"EventId":90'
| extend entry = parse_json(ResultDescription)
| extend EventId = toint(entry.EventId), ExceptionType = tostring(entry.State.ExceptionType)
| join kind=inner relioEvents on EventId
| project TimeGenerated, EventName, ExceptionType
'''

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (alertsEnabled) {
  name: 'ag-relio-operations'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'relio-ops'
    enabled: true
    emailReceivers: [
      {
        name: 'operator'
        emailAddress: alertEmailAddress
        useCommonAlertSchema: true
      }
    ]
  }
}

resource failureAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = if (alertsEnabled) {
  name: 'alert-relio-operational-failures'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: 'Relio operational failures'
    description: 'Unhandled HTTP errors, interactive circuit failures, background job and backup failures (event ids 9001-9005). See docs/operations/monitoring.md.'
    severity: 2
    enabled: true
    scopes: [
      logAnalyticsWorkspaceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    autoMitigate: true
    criteria: {
      allOf: [
        {
          query: failureQuery
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          dimensions: [
            {
              name: 'EventName'
              operator: 'Include'
              values: [
                '*'
              ]
            }
          ]
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroup.id
      ]
    }
  }
}

output actionGroupId string = alertsEnabled ? actionGroup.id : ''
