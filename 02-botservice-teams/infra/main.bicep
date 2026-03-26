targetScope = 'resourceGroup'

@description('Name of the azd environment')
param environmentName string

@description('Primary location for all resources')
param location string = resourceGroup().location

@description('Microsoft App ID for the bot registration')
param microsoftAppId string = ''

@description('Microsoft App Password for the bot registration')
@secure()
param microsoftAppPassword string = ''

var tags = {
  'azd-env-name': environmentName
}

var appServicePlanName = '${environmentName}-plan'
var teamsBotAppName = '${environmentName}-teams-bot'
var enterpriseApiAppName = '${environmentName}-enterprise-api'
var botServiceName = '${environmentName}-bot'

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

resource teamsBotApp 'Microsoft.Web/sites@2023-12-01' = {
  name: teamsBotAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'teams-bot' })
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      appSettings: [
        { name: 'MicrosoftAppId', value: microsoftAppId }
        { name: 'MicrosoftAppPassword', value: microsoftAppPassword }
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
      appSettings: []
    }
  }
}

resource botService 'Microsoft.BotService/botServices@2022-09-15' = {
  name: botServiceName
  location: 'global'
  tags: tags
  kind: 'azurebot'
  sku: {
    name: 'F0'
  }
  properties: {
    displayName: botServiceName
    endpoint: 'https://${teamsBotApp.properties.defaultHostName}/api/messages'
    msaAppId: microsoftAppId
    msaAppType: 'SingleTenant'
  }
}

resource teamsChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: botService
  name: 'MsTeamsChannel'
  location: 'global'
  properties: {
    channelName: 'MsTeamsChannel'
    properties: {
      isEnabled: true
    }
  }
}

@description('Teams bot web app URL')
output teamsBotUrl string = 'https://${teamsBotApp.properties.defaultHostName}'

@description('Enterprise API URL')
output enterpriseApiUrl string = 'https://${enterpriseApiApp.properties.defaultHostName}'
