﻿using System.Linq;
using System.Threading.Tasks;
using Calamari.Common.Variables;
using Calamari.Deployment;
using Calamari.Integration.FileSystem;

namespace Calamari.CommonTemp
{
    internal class ConfigurationVariablesBehaviour : IBehaviour
    {
        readonly ICalamariFileSystem fileSystem;
        readonly IConfigurationVariablesReplacer replacer;
        readonly ILog log;

        public ConfigurationVariablesBehaviour(ICalamariFileSystem fileSystem, IConfigurationVariablesReplacer replacer, ILog log)
        {
            this.fileSystem = fileSystem;
            this.replacer = replacer;
            this.log = log;
        }

        public Task Execute(RunningDeployment deployment)
        {
            if (deployment.Variables.GetFlag(KnownVariables.Package.AutomaticallyUpdateAppSettingsAndConnectionStrings) == false)
            {
                return this.CompletedTask();
            }

            var appliedAsTransforms = deployment.Variables.GetStrings(KnownVariables.AppliedXmlConfigTransforms, '|');

            log.Verbose("Looking for appSettings, applicationSettings, and connectionStrings in any .config files");

            if (deployment.Variables.GetFlag(KnownVariables.Package.IgnoreVariableReplacementErrors))
                log.Info("Variable replacement errors are suppressed because the variable Octopus.Action.Package.IgnoreVariableReplacementErrors has been set.");

            foreach (var configurationFile in MatchingFiles(deployment))
            {
                if (appliedAsTransforms.Contains(configurationFile))
                {
                    log.VerboseFormat("File '{0}' was interpreted as an XML configuration transform; variable substitution won't be attempted.", configurationFile);
                    continue;
                }

                replacer.ModifyConfigurationFile(configurationFile, deployment.Variables);
            }

            return this.CompletedTask();
        }

        string[] MatchingFiles(RunningDeployment deployment)
        {
            var files = fileSystem.EnumerateFilesRecursively(deployment.CurrentDirectory, "*.config");

            var additional = deployment.Variables.GetStrings(ActionVariables.AdditionalPaths)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .SelectMany(p => fileSystem.EnumerateFilesRecursively(p, "*.config"));


            return files.Concat(additional).Distinct().ToArray();
        }
    }
}