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

@description('Direct Line secret for the web client')
@secure()
param directLineSecret string = ''

var tags = {
  'azd-env-name': environmentName
}

var appServicePlanName = '${environmentName}-plan'
var directLineBotAppName = '${environmentName}-directline-bot'
var webClientAppName = '${environmentName}-web-client'
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

resource directLineBotApp 'Microsoft.Web/sites@2023-12-01' = {
  name: directLineBotAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'directline-bot' })
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

resource webClientApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webClientAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'web-client' })
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'NODE|20-lts'
      alwaysOn: true
      appSettings: [
        { name: 'DIRECT_LINE_SECRET', value: directLineSecret }
        { name: 'BOT_ENDPOINT', value: 'https://${directLineBotAppName}.azurewebsites.net' }
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
    endpoint: 'https://${directLineBotApp.properties.defaultHostName}/api/messages'
    msaAppId: microsoftAppId
    msaAppType: 'SingleTenant'
  }
}

resource directLineChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: botService
  name: 'DirectLineChannel'
  location: 'global'
  properties: {
    channelName: 'DirectLineChannel'
    properties: {
      sites: [
        {
          siteName: 'default'
          isEnabled: true
          isV1Enabled: false
          isV3Enabled: true
        }
      ]
    }
  }
}

@description('Direct Line bot URL')
output directLineBotUrl string = 'https://${directLineBotApp.properties.defaultHostName}'

@description('Web client URL')
output webClientUrl string = 'https://${webClientApp.properties.defaultHostName}'

@description('Enterprise API URL')
output enterpriseApiUrl string = 'https://${enterpriseApiApp.properties.defaultHostName}'
