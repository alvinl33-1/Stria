// Cost alerts for the demo resource group (PHASE_3_PLAN.md §5). A budget never stops spending, it
// only notifies: stopping PostgreSQL or tearing the group down is still a person's job.
param name string

@description('Monthly amount in the billing currency.')
param amount int

@description('yyyy-MM-01. Azure rejects any change to it after creation, so it is set once and never derived from the clock.')
param startDate string

@description('Optional extra address. Owners of the resource group are always notified.')
param contactEmail string = ''

var contactEmails = empty(contactEmail) ? [] : [contactEmail]

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: name
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      actual50: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: contactEmails
        contactRoles: ['Owner']
      }
      actual90: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 90
        thresholdType: 'Actual'
        contactEmails: contactEmails
        contactRoles: ['Owner']
      }
      // Warns before the money is spent, while there is still time to stop the server.
      forecast100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: contactEmails
        contactRoles: ['Owner']
      }
    }
  }
}
