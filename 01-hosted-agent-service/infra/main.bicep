targetScope = 'resourceGroup'

@description('Name of the azd environment')
param environmentName string

@description('Primary location for all resources')
param location string = resourceGroup().location

@description('Azure AD tenant ID for authentication')
param tenantId string = ''

@description('Entra ID client ID for the frontend app')
param frontendClientId string = ''

@description('Entra ID client ID for the agent service')
param agentServiceClientId string = ''

@description('Entra ID client ID for the enterprise API')
param enterpriseApiClientId string = ''

var tags = {
  'azd-env-name': environmentName
}

var appServicePlanName = '${environmentName}-plan'
var frontendAppName = '${environmentName}-frontend'
var agentServiceAppName = '${environmentName}-agent-service'
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

resource frontendApp 'Microsoft.Web/sites@2023-12-01' = {
  name: frontendAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'frontend' })
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      appSettings: [
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: frontendClientId }
        { name: 'AgentService__BaseUrl', value: 'https://${agentServiceAppName}.azurewebsites.net' }
      ]
    }
  }
}

resource agentServiceApp 'Microsoft.Web/sites@2023-12-01' = {
  name: agentServiceAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'agent-service' })
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      appSettings: [
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: agentServiceClientId }
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

@description('Frontend app URL')
output frontendUrl string = 'https://${frontendApp.properties.defaultHostName}'

@description('Agent service URL')
output agentServiceUrl string = 'https://${agentServiceApp.properties.defaultHostName}'

@description('Enterprise API URL')
output enterpriseApiUrl string = 'https://${enterpriseApiApp.properties.defaultHostName}'
