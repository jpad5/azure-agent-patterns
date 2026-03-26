targetScope = 'resourceGroup'

@description('Name of the azd environment')
param environmentName string

@description('Primary location for all resources')
param location string = resourceGroup().location

@description('Azure AD tenant ID for authentication')
param tenantId string = ''

@description('Entra ID client ID for the action endpoint')
param actionEndpointClientId string = ''

@description('Entra ID client ID for the enterprise API')
param enterpriseApiClientId string = ''

@description('Copilot Studio environment ID')
param copilotStudioEnvironmentId string = ''

@description('Copilot Studio bot ID')
param copilotStudioBotId string = ''

var tags = {
  'azd-env-name': environmentName
}

var appServicePlanName = '${environmentName}-plan'
var actionEndpointAppName = '${environmentName}-action-endpoint'
var enterpriseApiAppName = '${environmentName}-enterprise-api'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource actionEndpointApp 'Microsoft.Web/sites@2023-12-01' = {
  name: actionEndpointAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'action-endpoint' })
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      appSettings: [
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: actionEndpointClientId }
        { name: 'CopilotStudio__EnvironmentId', value: copilotStudioEnvironmentId }
        { name: 'CopilotStudio__BotId', value: copilotStudioBotId }
        { name: 'EnterpriseApi__BaseUrl', value: 'https://${enterpriseApiAppName}.azurewebsites.net' }
      ]
    }
  }
}

resource enterpriseApiApp 'Microsoft.Web/sites@2023-12-01' = {
  name: enterpriseApiAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'enterprise-api' })
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      appSettings: [
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: enterpriseApiClientId }
      ]
    }
  }
}

@description('Action endpoint URL')
output actionEndpointUrl string = 'https://${actionEndpointApp.properties.defaultHostName}'

@description('Enterprise API URL')
output enterpriseApiUrl string = 'https://${enterpriseApiApp.properties.defaultHostName}'
