﻿using System;
using System.Collections.Generic;
using System.IO;
using Calamari.Common.FeatureToggles;
using Calamari.Common.Plumbing.FileSystem;
using Calamari.Common.Plumbing.Logging;
using Calamari.Common.Plumbing.ServiceMessages;
using Calamari.Common.Plumbing.Variables;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Calamari.Kubernetes
{
    public interface IManifestReporter
    {
        void ReportManifestApplied(string filePath);
    }

    public class ManifestReporter : IManifestReporter
    {
        readonly IVariables variables;
        readonly ICalamariFileSystem fileSystem;
        readonly ILog log;

        static readonly ISerializer YamlSerializer = new SerializerBuilder()
                                                     .Build();

        public ManifestReporter(IVariables variables, ICalamariFileSystem fileSystem, ILog log)
        {
            this.variables = variables;
            this.fileSystem = fileSystem;
            this.log = log;
        }

        string GetNamespace(YamlMappingNode yamlRoot)
        {
            var implicitNamespace = variables.Get(SpecialVariables.Namespace) ?? "default";

            if (yamlRoot.Children.TryGetValue("metadata", out var metadataNode) && metadataNode is YamlMappingNode metadataMappingNode && metadataMappingNode.Children.TryGetValue("namespace", out var namespaceNode) && namespaceNode is YamlScalarNode namespaceScalarNode && !string.IsNullOrWhiteSpace(namespaceScalarNode.Value))
            {
                implicitNamespace = namespaceScalarNode.Value;
            }

            return implicitNamespace;
        }

        public void ReportManifestApplied(string filePath)
        {
            if (!FeatureToggle.KubernetesLiveObjectStatusFeatureToggle.IsEnabled(variables) && !OctopusFeatureToggles.KubernetesObjectManifestInspectionFeatureToggle.IsEnabled(variables))
                return;

            using (var yamlFile = fileSystem.OpenFile(filePath, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    var yamlStream = new YamlStream();
                    yamlStream.Load(new StreamReader(yamlFile));

                    foreach (var document in yamlStream.Documents)
                    {
                        if (!(document.RootNode is YamlMappingNode rootNode))
                        {
                            log.Warn("Could not parse manifest, resources will not be added to live object status");
                            continue;
                        }

                        var updatedDocument = SerializeManifest(rootNode);

                        var ns = GetNamespace(rootNode);
                        log.WriteServiceMessage(new ServiceMessage(SpecialVariables.ServiceMessageNames.ManifestApplied.Name,
                                                                   new Dictionary<string, string>
                                                                   {
                                                                       { SpecialVariables.ServiceMessageNames.ManifestApplied.ManifestAttribute, updatedDocument },
                                                                       { SpecialVariables.ServiceMessageNames.ManifestApplied.NamespaceAttribute, ns }
                                                                   }));
                    }
                }
                catch (SemanticErrorException)
                {
                    log.Warn("Invalid YAML syntax found, resources will not be added to live object status");
                }
            }
        }

        static string SerializeManifest(YamlMappingNode node)
        {
           return YamlSerializer.Serialize(node);
        }
    }
}