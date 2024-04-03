﻿using System;
using System.Threading.Tasks;
using Calamari.AzureAppService.Azure;
using Calamari.CloudAccounts;
using Calamari.Common.Commands;
using Calamari.Common.Plumbing.Logging;
using Calamari.Common.Plumbing.Pipeline;
using Octopus.CoreUtilities.Extensions;
using AccountVariables = Calamari.AzureAppService.Azure.AccountVariables;

namespace Calamari.AzureAppService.Behaviors
{
    class AzureAppServiceContainerDeployBehaviour : IDeployBehaviour
    {
        private ILog Log { get; }

        public AzureAppServiceContainerDeployBehaviour(ILog log)
        {
            Log = log;
        }

        public bool IsEnabled(RunningDeployment context) => true;

        public async Task Execute(RunningDeployment context)
        {
            var variables = context.Variables;

            var hasAccessToken = !variables.Get(AccountVariables.Jwt).IsNullOrEmpty();
            var account = hasAccessToken ? (IAzureAccount)new AzureOidcAccount(variables) : new AzureServicePrincipalAccount(variables);

            var webAppName = variables.Get(SpecialVariables.Action.Azure.WebAppName);
            var slotName = variables.Get(SpecialVariables.Action.Azure.WebAppSlot);
            var resourceGroupName = variables.Get(SpecialVariables.Action.Azure.ResourceGroupName);

            var targetSite = new AzureTargetSite(account.SubscriptionNumber, resourceGroupName, webAppName, slotName);

            var image = variables.Get(SpecialVariables.Action.Package.Image);
            var registryHost = variables.Get(SpecialVariables.Action.Package.Registry);
            var regUsername = variables.Get(SpecialVariables.Action.Package.Feed.Username);
            var regPwd = variables.Get(SpecialVariables.Action.Package.Feed.Password);

            var armClient = account.CreateArmClient();

            Log.Info($"Updating web app to use image {image} from registry {registryHost}");

            Log.Verbose("Retrieving config (this is required to update image)");
            var config = await armClient.GetSiteConfigDataAsync(targetSite);
            config.LinuxFxVersion = $@"DOCKER|{image}";

            Log.Verbose("Retrieving app settings");
            var appSettings = await armClient.GetAppSettingsAsync(targetSite);

            appSettings.Properties["DOCKER_REGISTRY_SERVER_URL"] = "https://" + registryHost;
            appSettings.Properties["DOCKER_REGISTRY_SERVER_USERNAME"] = regUsername;
            appSettings.Properties["DOCKER_REGISTRY_SERVER_PASSWORD"] = regPwd;

            Log.Info("Updating app settings with container registry");
            await armClient.UpdateAppSettingsAsync(targetSite, appSettings);

            Log.Info("Updating configuration with container image");
            await armClient.UpdateSiteConfigDataAsync(targetSite, config);
        }
    }
}